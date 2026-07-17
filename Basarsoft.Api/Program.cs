using System.Text;
using Basarsoft.Api.Data;
using Basarsoft.Api.Middleware;
using Basarsoft.Api.Security;
using Basarsoft.Api.Serialization;
using Basarsoft.Api.Services;
using Basarsoft.Api.Settings;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
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

// Bind the "GeoServer" config section and share it with the WFS reader below.
var geoServerSettings = builder.Configuration.GetSection("GeoServer").Get<GeoServerSettings>()
    ?? throw new InvalidOperationException("Missing 'GeoServer' configuration section.");
builder.Services.AddSingleton(geoServerSettings);

// Auth services.
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();

// Geometry (drawing) service — still owns writes/query/analysis against PostGIS.
builder.Services.AddScoped<IGeometryService, GeometryService>();

// Reads the drawn geometry back through GeoServer's WFS (the map's one-shot load). A typed HttpClient
// so it gets connection pooling and can be configured/tested independently.
builder.Services.AddHttpClient<IGeoServerReadService, GeoServerReadService>();

// Admin (RBAC) services: user/role/permission management + effective-permission resolution.
builder.Services.AddScoped<IPermissionService, PermissionService>();
builder.Services.AddScoped<IRoleService, RoleService>();
builder.Services.AddScoped<IUserAdminService, UserAdminService>();

// Geographic authorization: per-user/per-role drawing areas + enforcement lookup.
builder.Services.AddScoped<IGeoAuthorizationService, GeoAuthorizationService>();

// POI module: the shared POI catalogue + its parent-child category tree.
builder.Services.AddScoped<IPoiService, PoiService>();
builder.Services.AddScoped<IPoiCategoryService, PoiCategoryService>();

// Location analysis (Konum Analizi): stores weighted-criteria runs that GeoServer's vw_konum view
// renders back as heat maps.
builder.Services.AddScoped<ILocationAnalysisService, LocationAnalysisService>();

// Global exception handling: a final safety net that converts any unhandled exception into a clean
// 500 JSON response (the controllers also try-catch individually). AddProblemDetails() supplies the
// standard error body format that UseExceptionHandler() falls back to.
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// Add controller support so [ApiController] classes work.
// The TimeOnly converter lets the POI working-hours fields accept the "HH:mm" that
// <input type="time"> submits (the built-in converter would reject it as missing seconds).
builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new FlexibleTimeOnlyConverter()));

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
// Authorization with a DB-backed "AdminAccess" policy: AdminAccessHandler checks the caller's effective
// permissions, so only users holding a management permission can reach the admin controllers.
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AdminAccessRequirement.PolicyName, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.AddRequirements(new AdminAccessRequirement());
    });
});
builder.Services.AddScoped<IAuthorizationHandler, AdminAccessHandler>();

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

// `dotnet run -- seed-demo [--yes]` wipes the database and rebuilds the demo dataset, then exits
// without ever starting Kestrel — it is a maintenance command, and we want an exit code out of it.
// A bare argument rather than a config flag on purpose: a switch that wipes the database is one that
// must not be possible to leave turned on in appsettings. (The CommandLine configuration provider
// ignores tokens with no -/--/ prefix, so "seed-demo" cannot collide with a config key.)
var seedDemo = args.Contains("seed-demo", StringComparer.OrdinalIgnoreCase);

// Seed the RBAC baseline (permission catalogue, Admin role, and the bootstrap admin's grant)
// idempotently on startup. The migration must already be applied (tables exist); each step is guarded
// so re-runs no-op. In demo mode this is skipped: DemoSeeder runs it itself, AFTER the wipe.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    if (seedDemo)
    {
        var seeded = await DemoSeeder.RunAsync(
            db, app.Environment, app.Logger, skipConfirmation: args.Contains("--yes"));
        return seeded ? 0 : 1;
    }

    await AdminSeeder.SeedAsync(db);

    // Turkey's 81 provinces for the location-analysis dropdown: loaded once from Data/provinces.geojson,
    // no-ops when the table is already filled.
    await ProvinceSeeder.SeedAsync(db);
}

// Catch unhandled exceptions first so nothing downstream can leak a stack trace. Delegates to
// GlobalExceptionHandler (registered above).
app.UseExceptionHandler();

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

// Only reached when the host shuts down. Explicit because the seed-demo branch above returns an exit
// code, which makes every path in this file have to produce one.
return 0;
