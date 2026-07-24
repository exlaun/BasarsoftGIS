using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Basarsoft.Api.Data;

// Gives EF tooling a metadata context even when Program's startup seeders cannot reach a database.
// It mirrors Program's configuration sources, so `dotnet ef database update` still targets the
// configured database while model-only commands avoid constructing the full application.
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var basePath = Directory.GetCurrentDirectory();
        if (!File.Exists(Path.Combine(basePath, "appsettings.json")))
            basePath = Path.Combine(basePath, "Basarsoft.Api");

        var environment =
            Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? Environments.Production;
        var configurationBuilder = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: false);
        if (environment.Equals(Environments.Development, StringComparison.OrdinalIgnoreCase))
            configurationBuilder.AddUserSecrets<AppDbContextFactory>(optional: true);
        configurationBuilder.AddEnvironmentVariables();
        if (args.Length > 0)
            configurationBuilder.AddCommandLine(args);
        var configuration = configurationBuilder.Build();
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Missing 'DefaultConnection' connection string.");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(
                connectionString,
                postgres => postgres.UseNetTopologySuite())
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AppDbContext(options);
    }
}
