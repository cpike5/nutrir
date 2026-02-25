using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nutrir.Core.Entities;
using Nutrir.Infrastructure;
using Nutrir.Infrastructure.Data;

namespace Nutrir.Cli.Infrastructure;

public static class CliHostBuilder
{
    public static IHost Build(string? connectionStringOverride = null)
    {
        var builder = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((_, config) =>
            {
                config.SetBasePath(AppContext.BaseDirectory);
                config.AddJsonFile("appsettings.json", optional: false);
                config.AddEnvironmentVariables("NUTRIR_");

                if (!string.IsNullOrWhiteSpace(connectionStringOverride))
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:DefaultConnection"] = connectionStringOverride
                    });
                }
            })
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.SetMinimumLevel(LogLevel.Warning);
            })
            .ConfigureServices((context, services) =>
            {
                services.AddInfrastructure(context.Configuration);

                services.AddIdentityCore<ApplicationUser>()
                    .AddRoles<IdentityRole>()
                    .AddEntityFrameworkStores<AppDbContext>();
            });

        return builder.Build();
    }
}
