using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Nutrir.Cli.Infrastructure;
using Nutrir.Core.Interfaces;

namespace Nutrir.Cli.Commands;

public static class AuditCommand
{
    public static Command Create(
        Option<string> formatOption,
        Option<string?> connectionStringOption)
    {
        var command = new Command("audit", "View audit logs");

        command.AddCommand(CreateListCommand(formatOption, connectionStringOption));

        return command;
    }

    private static Command CreateListCommand(
        Option<string> formatOption,
        Option<string?> connectionStringOption)
    {
        var countOption = new Option<int>("--count", getDefaultValue: () => 50, description: "Number of entries to retrieve");

        var cmd = new Command("list", "List recent audit log entries");
        cmd.AddOption(countOption);

        cmd.SetHandler(async (context) =>
        {
            var format = context.ParseResult.GetValueForOption(formatOption)!;
            var connStr = context.ParseResult.GetValueForOption(connectionStringOption);
            var count = context.ParseResult.GetValueForOption(countOption);

            try
            {
                using var host = CliHostBuilder.Build(connStr);
                using var scope = host.Services.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IAuditLogService>();
                var result = await service.GetRecentAsync(count);
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
