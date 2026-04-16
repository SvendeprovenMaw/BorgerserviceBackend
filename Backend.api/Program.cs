
using System.Text;
using Backend.api.Configuration;
using Backend.api.Database;
using Backend.api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;

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

builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("JwtSettings"));

var jwtSettings = builder.Configuration.GetSection("JwtSettings").Get<JwtSettings>();

if (jwtSettings == null || string.IsNullOrEmpty(jwtSettings.Key))
{
    throw new Exception("JWT Settings failed to bind! check section name.");
}

Console.WriteLine($"SECRET KEY BOUND: {jwtSettings.Key}");

// Add services to the container.
builder.Services.AddDbContext<WarehouseDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddControllers();
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
        policy.WithOrigins("http://localhost:4200") // Must be specific!
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials(); // This allows the cookies to travel
    });
});

var app = builder.Build();
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

