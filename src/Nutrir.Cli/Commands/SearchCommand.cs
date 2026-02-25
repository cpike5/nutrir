using System.CommandLine;
using System.CommandLine.Invocation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Nutrir.Cli.Infrastructure;
using Nutrir.Core.Interfaces;

namespace Nutrir.Cli.Commands;

public static class SearchCommand
{
    public static Command Create(
        Option<string?> userIdOption,
        Option<string> formatOption,
        Option<string> sourceOption,
        Option<string?> connectionStringOption)
    {
        var queryArg = new Argument<string>("query", "Search query");

        var cmd = new Command("search", "Search across all entities");
        cmd.AddArgument(queryArg);

        cmd.SetHandler(async (context) =>
        {
            var query = context.ParseResult.GetValueForArgument(queryArg);
            var format = context.ParseResult.GetValueForOption(formatOption)!;
            var connStr = context.ParseResult.GetValueForOption(connectionStringOption);

            try
            {
                var userId = ResolveUserId(context, userIdOption);

                using var host = CliHostBuilder.Build(connStr);
                using var scope = host.Services.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<ISearchService>();
                var result = await service.SearchAsync(query, userId);
                OutputFormatter.Write(result, format);
                context.ExitCode = 0;
            }
            catch (InvalidOperationException ex)
            {
                OutputFormatter.WriteError(ex.Message, format);
                context.ExitCode = 1;
            }
            catch (Exception ex)
            {
                OutputFormatter.WriteError(ex.Message, format);
                context.ExitCode = 2;
            }
        });

        return cmd;
    }

    private static string ResolveUserId(InvocationContext context, Option<string?> userIdOption)
    {
        var userId = context.ParseResult.GetValueForOption(userIdOption)
                     ?? Environment.GetEnvironmentVariable("NUTRIR_USER_ID");
        if (string.IsNullOrWhiteSpace(userId))
            throw new InvalidOperationException("--user-id is required for this operation. Set via option or NUTRIR_USER_ID env var.");
        return userId;
    }
}
