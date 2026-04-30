using System.Text;
using Backend.api.Configuration;
using Backend.api.Database;
using Backend.api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using Backend.api.Middleware;
using Openai.Library.Phases;
using Microsoft.EntityFrameworkCore;
using Openai.Library.Options;
using Amazon.S3;

JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IS3StorageService, S3StorageService>();
builder.Services.AddScoped<IFileService, FileService>();
builder.Services.AddScoped<IConsentService, ConsentService>();
builder.Services.AddScoped<IRequirementsPhase, RequirementsPhase>();
builder.Services.AddScoped<ICandidateEvidencePhase, CandidateEvidencePhase>();
builder.Services.AddScoped<ICompetenceMatchingPhase, CompetenceMatchingPhase>();
builder.Services.AddScoped<IApplicationGenerationPhase, ApplicationGenerationPhase>();
builder.Services.AddScoped<IAiJobService, AiJobService>();

//lets me inject 2 different s3 clients so we can use aws's s3 library to interact with backblaze without running into issues with the library trying to validate against aws endpoints which causes issues when downloading files from backblaze, using a seperate client without validation for downloading files solves this issue
builder.Services.AddKeyedScoped<IAmazonS3>("S3Uploader", (sp, key) => {
    var conf = sp.GetRequiredService<IConfiguration>();
    var credentials = new Amazon.Runtime.BasicAWSCredentials(conf["BackBlaze:Keyid"], conf["BackBlaze:ApplicationKey"]);
    var config = new AmazonS3Config 
    { 
        ServiceURL = "https://s3.eu-central-003.backblazeb2.com",
        ForcePathStyle = true,
        AuthenticationRegion = "eu-central-003",
        UseHttp = false
    };
    return new AmazonS3Client(credentials, config);
});
builder.Services.AddKeyedScoped<IAmazonS3>("S3Downloader", (sp, key) => {
    var conf = sp.GetRequiredService<IConfiguration>();
    var credentials = new Amazon.Runtime.BasicAWSCredentials(conf["BackBlaze:Keyid"], conf["BackBlaze:ApplicationKey"]);
    var config = new AmazonS3Config 
    { 
        ServiceURL = "https://s3.eu-central-003.backblazeb2.com",
        AuthenticationRegion = "eu-central-1", 
        RegionEndpoint = Amazon.RegionEndpoint.EUCentral1,
        ForcePathStyle = true
    };
    return new AmazonS3Client(credentials, config);
});

//gets appsettings for jwt and binds it to JwtSettings class, also makes it available for injection via IOptions<JwtSettings>
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("JwtSettings"));
var jwtSettings = builder.Configuration.GetSection("JwtSettings").Get<JwtSettings>();
builder.Services.Configure<OpenAiLibraryOptions>(
    builder.Configuration.GetSection("OpenAi")
);

if (jwtSettings == null || string.IsNullOrEmpty(jwtSettings.Key))
{
    throw new Exception("JWT Settings failed to bind! check section name.");
}

// Add services to the container.
builder.Services.AddDbContext<ApplyAiDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

//get jwt token from cookie for easy use in controllers and also ensure "sub" claim is used as NameIdentifier claim type so we can get it easily in controllers via User.Identity.Name
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.MapInboundClaims = false;
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context => {
            context.Token = context.Request.Cookies["AccessToken"];
            return Task.CompletedTask;
        },
        OnTokenValidated = context => {
            // Check the console now. It SHOULD say "sub" instead of the URL.
            return Task.CompletedTask;
        },
        OnAuthenticationFailed = context =>
        {
            Console.WriteLine($"FAILURE: {context.Exception.Message}");
            return Task.CompletedTask;
        }
    };

    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings.Issuer,
        ValidAudience = jwtSettings.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(
        Encoding.UTF8.GetBytes(jwtSettings.Key)),
        NameClaimType = "sub",
        RoleClaimType = "role"
    };
});


builder.Services.AddCors(options =>
{
    options.AddPolicy("AngularPolicy", policy =>
    {
        policy.WithOrigins("http://localhost:4200") // Must be specific!
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials(); // This allows the cookies to travel
    });
});

var app = builder.Build();
app.UseMiddleware<CustomExceptionHandlingMiddleware>();
app.UseRouting();
app.UseCors("AngularPolicy");
//await DatabaseInitializer.InitializeAsync(app.Services, app.Configuration);

app.MapSwagger();
// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "Warehouse Management Api");
            c.RoutePrefix = "";
        });
}

if (!app.Configuration.GetValue("DisableHttpsRedirection", false))
{
    app.UseHttpsRedirection();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();