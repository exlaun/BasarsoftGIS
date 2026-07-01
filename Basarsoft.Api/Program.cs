using System.Text;
using Basarsoft.Api.Data;
using Basarsoft.Api.Services;
using Basarsoft.Api.Settings;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Read the PostgreSQL connection string from appsettings.json.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// Register the database context and tell EF Core to use PostgreSQL (Npgsql).
// UseNetTopologySuite() lets EF map C# geometry types (Point/LineString/Polygon) to PostGIS.
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString, o => o.UseNetTopologySuite())
           .UseSnakeCaseNamingConvention());

// Bind the "Jwt" config section once and share it (TokenService reads Key/ExpiresMinutes from it).
var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>()
    ?? throw new InvalidOperationException("Missing 'Jwt' configuration section.");
builder.Services.AddSingleton(jwtSettings);

// Auth services.
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();

// Geometry (drawing) service.
builder.Services.AddScoped<IGeometryService, GeometryService>();

// Add controller support so [ApiController] classes work.
builder.Services.AddControllers();

// Swagger / OpenAPI, with a Bearer button so protected endpoints can be tested from the browser.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter the JWT token (without the 'Bearer ' prefix)."
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// JWT bearer authentication. ClockSkew = zero so the short (5/10-min) token expires exactly on time.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false; // keep "sub"/"unique_name" claim names as-is
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Key))
        };
    });
builder.Services.AddAuthorization();

// CORS policy so the React (Vite) dev server is allowed to call this API.
const string AllowReactApp = "AllowReactApp";
builder.Services.AddCors(options =>
{
    options.AddPolicy(AllowReactApp, policy =>
        policy.WithOrigins("http://localhost:5173") // React dev server URL
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();

// Show Swagger UI only while developing.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Order matters: CORS, then authentication, then authorization, then endpoints.
app.UseCors(AllowReactApp);
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
