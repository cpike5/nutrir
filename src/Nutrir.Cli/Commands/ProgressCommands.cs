using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Nutrir.Cli.Infrastructure;
using Nutrir.Core.DTOs;
using Nutrir.Core.Enums;
using Nutrir.Core.Interfaces;

namespace Nutrir.Cli.Commands;

public static class ProgressCommands
{
    private static readonly JsonSerializerOptions JsonReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static Command Create(
        Option<string?> userIdOption,
        Option<string> formatOption,
        Option<string> sourceOption,
        Option<string?> connectionStringOption)
    {
        var command = new Command("progress", "Manage progress entries");

        command.AddCommand(BuildListCommand(formatOption, connectionStringOption));
        command.AddCommand(BuildGetCommand(formatOption, connectionStringOption));
        command.AddCommand(BuildCreateCommand(userIdOption, formatOption, connectionStringOption));
        command.AddCommand(BuildDeleteCommand(userIdOption, formatOption, connectionStringOption));

        return command;
    }

    private static Command BuildListCommand(
        Option<string> formatOption,
        Option<string?> connectionStringOption)
    {
        var clientIdOption = new Option<int>("--client-id", "Client ID") { IsRequired = true };
        var cmd = new Command("list", "List progress entries for a client") { clientIdOption };

        cmd.SetHandler(async (InvocationContext context) =>
        {
            var clientId = context.ParseResult.GetValueForOption(clientIdOption);
            var format = context.ParseResult.GetValueForOption(formatOption)!;
            var connStr = context.ParseResult.GetValueForOption(connectionStringOption);

            try
            {
                using var host = CliHostBuilder.Build(connStr);
                await host.StartAsync();
                using var scope = host.Services.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IProgressService>();

                var result = await service.GetEntriesByClientAsync(clientId);
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

    private static Command BuildGetCommand(
        Option<string> formatOption,
        Option<string?> connectionStringOption)
    {
        var idArg = new Argument<int>("id", "Progress entry ID");
        var cmd = new Command("get", "Get progress entry details") { idArg };

        cmd.SetHandler(async (InvocationContext context) =>
        {
            var id = context.ParseResult.GetValueForArgument(idArg);
            var format = context.ParseResult.GetValueForOption(formatOption)!;
            var connStr = context.ParseResult.GetValueForOption(connectionStringOption);

            try
            {
                using var host = CliHostBuilder.Build(connStr);
                await host.StartAsync();
                using var scope = host.Services.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IProgressService>();

                var result = await service.GetEntryByIdAsync(id);
                if (result is null)
                {
                    OutputFormatter.WriteError($"Progress entry {id} not found", format);
                    context.ExitCode = 1;
                    return;
                }

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

    private static Command BuildCreateCommand(
        Option<string?> userIdOption,
        Option<string> formatOption,
        Option<string?> connectionStringOption)
    {
        var clientIdOption = new Option<int>("--client-id", "Client ID") { IsRequired = true };
        var dateOption = new Option<DateOnly>("--date", "Entry date (yyyy-MM-dd)") { IsRequired = true };
        var metricsOption = new Option<string>("--metrics", "JSON array of measurements, e.g. '[{\"type\":\"Weight\",\"value\":80,\"unit\":\"kg\"}]'") { IsRequired = true };
        var notesOption = new Option<string?>("--notes", "Entry notes");

        var cmd = new Command("create", "Create a progress entry")
        {
            clientIdOption, dateOption, metricsOption, notesOption
        };

        cmd.SetHandler(async (InvocationContext context) =>
        {
            var format = context.ParseResult.GetValueForOption(formatOption)!;
            var connStr = context.ParseResult.GetValueForOption(connectionStringOption);

            try
            {
                var userId = ResolveUserId(context, userIdOption);
                var clientId = context.ParseResult.GetValueForOption(clientIdOption);
                var date = context.ParseResult.GetValueForOption(dateOption);
                var metricsJson = context.ParseResult.GetValueForOption(metricsOption)!;
                var notes = context.ParseResult.GetValueForOption(notesOption);

                var metricItems = JsonSerializer.Deserialize<List<MetricInput>>(metricsJson, JsonReadOptions);
                if (metricItems is null || metricItems.Count == 0)
                {
                    OutputFormatter.WriteError("--metrics must be a non-empty JSON array", format);
                    context.ExitCode = 1;
                    return;
                }

                var measurements = metricItems.Select(m => new CreateProgressMeasurementDto(
                    MetricType: m.Type,
                    CustomMetricName: m.CustomName,
                    Value: m.Value,
                    Unit: m.Unit)).ToList();

                var dto = new CreateProgressEntryDto(
                    ClientId: clientId,
                    EntryDate: date,
                    Notes: notes,
                    Measurements: measurements);

                using var host = CliHostBuilder.Build(connStr);
                await host.StartAsync();
                using var scope = host.Services.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IProgressService>();

                var result = await service.CreateEntryAsync(dto, userId);
                OutputFormatter.Write(result, format);
                context.ExitCode = 0;
            }
            catch (JsonException ex)
            {
                OutputFormatter.WriteError($"Invalid --metrics JSON: {ex.Message}", format);
                context.ExitCode = 1;
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

    private static Command BuildDeleteCommand(
        Option<string?> userIdOption,
        Option<string> formatOption,
        Option<string?> connectionStringOption)
    {
        var idArg = new Argument<int>("id", "Progress entry ID");
        var cmd = new Command("delete", "Soft-delete a progress entry") { idArg };

        cmd.SetHandler(async (InvocationContext context) =>
        {
            var format = context.ParseResult.GetValueForOption(formatOption)!;
            var connStr = context.ParseResult.GetValueForOption(connectionStringOption);

            try
            {
                var userId = ResolveUserId(context, userIdOption);
                var id = context.ParseResult.GetValueForArgument(idArg);

                using var host = CliHostBuilder.Build(connStr);
                await host.StartAsync();
                using var scope = host.Services.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IProgressService>();

                var success = await service.SoftDeleteEntryAsync(id, userId);
                if (!success)
                {
                    OutputFormatter.WriteError($"Progress entry {id} not found", format);
                    context.ExitCode = 1;
                    return;
                }

                OutputFormatter.Write(new { Id = id, Message = "Progress entry deleted" }, format);
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

    /// <summary>
    /// Internal model for deserializing the --metrics JSON input.
    /// </summary>
    private record MetricInput(
        MetricType Type,
        string? CustomName,
        decimal Value,
        string? Unit);
}
