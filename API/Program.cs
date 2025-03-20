using API.Data;
using API.Endpoints;
using API.Hubs;
using API.Models;
using API.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Define CORS origins once to avoid duplication
var corsOrigins = new[] { "http://localhost:4200", "https://localhost:4200" };

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(builder =>
    {
        builder.WithOrigins(corsOrigins)
               .AllowAnyHeader()
               .AllowAnyMethod()
               .AllowCredentials();
    });
});

// Configure JWT Settings
var jwtSettingsSection = builder.Configuration.GetSection("JWTSettings");
builder.Services.Configure<JwtSettings>(jwtSettingsSection);
var securityKey = jwtSettingsSection["SecurityKey"];

if (string.IsNullOrEmpty(securityKey))
{
    throw new InvalidOperationException("JWT Security Key is missing in configuration");
}

// Ensure key is at least 32 bytes for security
if (Encoding.UTF8.GetBytes(securityKey).Length < 32)
{
    securityKey = securityKey.PadRight(32, '!');
}

// Configure Database
builder.Services.AddDbContext<AppDbContext>(x => x.UseSqlite("Data Source=chat.db"));

// Configure Identity
builder.Services.AddIdentityCore<AppUser>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

// Register Services
builder.Services.AddScoped<TokensService>();

// Configure Controllers
builder.Services.AddControllers(options =>
{
    options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
});

// Configure Authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(
                securityKey.Length < 32
                    ? securityKey.PadRight(32, '!')
                    : securityKey
            )
        ),
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };

    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        },
        OnTokenValidated = context =>
        {
            Console.WriteLine("Token validated successfully");
            return Task.CompletedTask;
        },
        OnAuthenticationFailed = context =>
        {
            Console.WriteLine($"Authentication failed: {context.Exception}");
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();

// Configure OpenAPI (Swagger)
builder.Services.AddOpenApi();
builder.Services.AddSignalR();

var app = builder.Build();

// Enable OpenAPI only in Development
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Apply middleware in the correct order
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseCors(); 
app.UseAuthentication();
app.UseAuthorization();

// Map controllers and endpoints
app.MapControllers();
app.MapHub<ChatHub>("hubs/chat");
app.MapAccountEndpoint();

app.Run();