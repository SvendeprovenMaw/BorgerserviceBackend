
using System.Text;
using System.Reflection;
using Backend.api.Configuration;
using Backend.api.Database;
using Backend.api.Services;
using Backend.api.Services.ApplyAIService;
using JwtLibrary.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.IdentityModel.Tokens.Jwt;
using Backend.api.Middleware;

JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "ApplyAI Backend API",
        Version = "v1",
        Description = "Authenticated backend routes for account access, consent-backed file handling, and the ApplyAI pipeline. Route descriptions document what each input field is used for so the frontend can integrate against the backend contract directly from Swagger."
    });

    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
    }

    options.AddSecurityDefinition("AccessTokenCookie", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Cookie,
        Name = "AccessToken",
        Description = "JWT access token cookie issued by /api/User/login. Authorized routes read this cookie automatically."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference
            {
                Type = ReferenceType.SecurityScheme,
                Id = "AccessTokenCookie"
            }
        }] = Array.Empty<string>()
    });
});
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IProfileService, ProfileService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IS3StorageService, S3StorageService>();
builder.Services.AddScoped<IFileService, FileService>();
builder.Services.AddScoped<IConsentService, ConsentService>();
builder.Services.AddScoped<ISentApplicationService, SentApplicationService>();
builder.Services.AddApplyAiServiceModule(builder.Configuration);

var jwtSettings = new JwtSettings
{
    Key = JwtConfigurationReader.GetSigningKey(builder.Configuration),
    Issuer = JwtConfigurationReader.GetIssuer(builder.Configuration),
    Audience = JwtConfigurationReader.GetAudience(builder.Configuration),
    Actor = JwtConfigurationReader.GetActor(builder.Configuration),
    DurationInMinutes = JwtConfigurationReader.GetDurationInMinutes(builder.Configuration)
};

builder.Services.Configure<JwtSettings>(options =>
{
    options.Key = jwtSettings.Key;
    options.Issuer = jwtSettings.Issuer;
    options.Audience = jwtSettings.Audience;
    options.Actor = jwtSettings.Actor;
    options.DurationInMinutes = jwtSettings.DurationInMinutes;
});

builder.Services
    .AddOptions<BackBlazeSettings>()
    .Bind(builder.Configuration.GetSection("BackBlaze"))
    .Validate(settings => !string.IsNullOrWhiteSpace(settings.Keyid), "BackBlaze:Keyid is required.")
    .Validate(settings => !string.IsNullOrWhiteSpace(settings.Bucket), "BackBlaze:Bucket is required.")
    .Validate(settings => !string.IsNullOrWhiteSpace(settings.ApplicationKey), "BackBlaze:ApplicationKey is required.")
    .Validate(settings => !string.IsNullOrWhiteSpace(settings.ServiceUrl), "BackBlaze:ServiceUrl is required.")
    .Validate(settings => Uri.TryCreate(settings.ServiceUrl, UriKind.Absolute, out _), "BackBlaze:ServiceUrl must be an absolute URL.")
    .Validate(settings => !string.IsNullOrWhiteSpace(settings.DownloaderAuthenticationRegion), "BackBlaze:DownloaderAuthenticationRegion is required.")
    .Validate(settings => !string.IsNullOrWhiteSpace(settings.DownloaderRegionSystemName), "BackBlaze:DownloaderRegionSystemName is required.")
    .Validate(settings => !string.IsNullOrWhiteSpace(settings.UploaderAuthenticationRegion), "BackBlaze:UploaderAuthenticationRegion is required.")
    .ValidateOnStart();

var connectionString = builder.Configuration.GetConnectionString("Default");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("Connection string 'Default' failed to bind.");
}

var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>()?
    .Where(origin => !string.IsNullOrWhiteSpace(origin))
    .ToArray()
    ?? ["http://localhost:4200"];

// Add services to the container.
builder.Services.AddDbContext<ApplyAIDbContext>(options =>
    options.UseNpgsql(connectionString));

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

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
        policy.WithOrigins(allowedOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials(); // This allows the cookies to travel
    });
});

var app = builder.Build();
app.UseMiddleware<CustomExceptionHandlingMiddleware>();
app.UseStaticFiles();
app.UseRouting();
app.UseCors("AngularPolicy");
await DatabaseInitializer.InitializeAsync(app.Services, app.Configuration);

app.MapSwagger();
// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "ApplyAI Backend API v1");
            c.DocumentTitle = "ApplyAI Backend Swagger";
            c.InjectStylesheet("/swagger-ui/custom.css");
            c.RoutePrefix = "";
        });
}

if (!app.Configuration.GetValue("DisableHttpsRedirection", false))
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

