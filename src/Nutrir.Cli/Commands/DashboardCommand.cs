using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Nutrir.Cli.Infrastructure;
using Nutrir.Core.Interfaces;

namespace Nutrir.Cli.Commands;

public static class DashboardCommand
{
    public static Command Create(
        Option<string> formatOption,
        Option<string?> connectionStringOption)
    {
        var cmd = new Command("dashboard", "View dashboard metrics");

        cmd.SetHandler(async (context) =>
        {
            var format = context.ParseResult.GetValueForOption(formatOption)!;
            var connStr = context.ParseResult.GetValueForOption(connectionStringOption);

            try
            {
                using var host = CliHostBuilder.Build(connStr);
                using var scope = host.Services.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IDashboardService>();
                var result = await service.GetMetricsAsync();
                OutputFormatter.Write(result, format);
                context.ExitCode = 0;
            }
            catch (Exception ex)
            {
                OutputFormatter.WriteError(ex.Message, format);
                context.ExitCode = 2;
            }
        });

        return cmd;
    }
}
