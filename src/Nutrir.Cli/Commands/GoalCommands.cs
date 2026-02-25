using System.CommandLine;
using System.CommandLine.Invocation;
using Microsoft.Extensions.DependencyInjection;
using Nutrir.Cli.Infrastructure;
using Nutrir.Core.DTOs;
using Nutrir.Core.Enums;
using Nutrir.Core.Interfaces;

namespace Nutrir.Cli.Commands;

public static class GoalCommands
{
    public static Command Create(
        Option<string?> userIdOption,
        Option<string> formatOption,
        Option<string> sourceOption,
        Option<string?> connectionStringOption)
    {
        var command = new Command("goals", "Manage progress goals");

        command.AddCommand(BuildListCommand(formatOption, connectionStringOption));
        command.AddCommand(BuildGetCommand(formatOption, connectionStringOption));
        command.AddCommand(BuildCreateCommand(userIdOption, formatOption, connectionStringOption));
        command.AddCommand(BuildUpdateCommand(userIdOption, formatOption, connectionStringOption));
        command.AddCommand(BuildAchieveCommand(userIdOption, formatOption, connectionStringOption));
        command.AddCommand(BuildAbandonCommand(userIdOption, formatOption, connectionStringOption));
        command.AddCommand(BuildDeleteCommand(userIdOption, formatOption, connectionStringOption));

        return command;
    }

    private static Command BuildListCommand(
        Option<string> formatOption,
        Option<string?> connectionStringOption)
    {
        var clientIdOption = new Option<int>("--client-id", "Client ID") { IsRequired = true };
        var cmd = new Command("list", "List goals for a client") { clientIdOption };

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

                var result = await service.GetGoalsByClientAsync(clientId);
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
        var idArg = new Argument<int>("id", "Goal ID");
        var cmd = new Command("get", "Get goal details") { idArg };

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

                var result = await service.GetGoalByIdAsync(id);
                if (result is null)
                {
                    OutputFormatter.WriteError($"Goal {id} not found", format);
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
        var titleOption = new Option<string>("--title", "Goal title") { IsRequired = true };
        var typeOption = new Option<GoalType>("--type", "Goal type (Weight, BodyComposition, Dietary, Custom)") { IsRequired = true };
        var targetValueOption = new Option<decimal?>("--target-value", "Target value");
        var targetUnitOption = new Option<string?>("--target-unit", "Target unit");
        var targetDateOption = new Option<DateOnly?>("--target-date", "Target date (yyyy-MM-dd)");
        var descriptionOption = new Option<string?>("--description", "Goal description");

        var cmd = new Command("create", "Create a new goal")
        {
            clientIdOption, titleOption, typeOption,
            targetValueOption, targetUnitOption, targetDateOption, descriptionOption
        };

        cmd.SetHandler(async (InvocationContext context) =>
        {
            var format = context.ParseResult.GetValueForOption(formatOption)!;
            var connStr = context.ParseResult.GetValueForOption(connectionStringOption);

            try
            {
                var userId = ResolveUserId(context, userIdOption);
                var dto = new CreateProgressGoalDto(
                    ClientId: context.ParseResult.GetValueForOption(clientIdOption),
                    Title: context.ParseResult.GetValueForOption(titleOption)!,
                    Description: context.ParseResult.GetValueForOption(descriptionOption),
                    GoalType: context.ParseResult.GetValueForOption(typeOption),
                    TargetValue: context.ParseResult.GetValueForOption(targetValueOption),
                    TargetUnit: context.ParseResult.GetValueForOption(targetUnitOption),
                    TargetDate: context.ParseResult.GetValueForOption(targetDateOption));

                using var host = CliHostBuilder.Build(connStr);
                await host.StartAsync();
                using var scope = host.Services.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IProgressService>();

                var result = await service.CreateGoalAsync(dto, userId);
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

    private static Command BuildUpdateCommand(
        Option<string?> userIdOption,
        Option<string> formatOption,
        Option<string?> connectionStringOption)
    {
        var idArg = new Argument<int>("id", "Goal ID");
        var titleOption = new Option<string?>("--title", "Goal title");
        var descriptionOption = new Option<string?>("--description", "Goal description");
        var targetValueOption = new Option<decimal?>("--target-value", "Target value");
        var targetUnitOption = new Option<string?>("--target-unit", "Target unit");
        var targetDateOption = new Option<DateOnly?>("--target-date", "Target date (yyyy-MM-dd)");
        var typeOption = new Option<GoalType?>("--type", "Goal type (Weight, BodyComposition, Dietary, Custom)");

        var cmd = new Command("update", "Update an existing goal")
        {
            idArg, titleOption, descriptionOption,
            targetValueOption, targetUnitOption, targetDateOption, typeOption
        };

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

                // Fetch existing goal to merge with provided options
                var existing = await service.GetGoalByIdAsync(id);
                if (existing is null)
                {
                    OutputFormatter.WriteError($"Goal {id} not found", format);
                    context.ExitCode = 1;
                    return;
                }

                // Only override fields that were explicitly provided
                var title = context.ParseResult.FindResultFor(titleOption) is not null
                    ? context.ParseResult.GetValueForOption(titleOption) ?? existing.Title
                    : existing.Title;
                var description = context.ParseResult.FindResultFor(descriptionOption) is not null
                    ? context.ParseResult.GetValueForOption(descriptionOption)
                    : existing.Description;
                var goalType = context.ParseResult.FindResultFor(typeOption) is not null
                    ? context.ParseResult.GetValueForOption(typeOption) ?? existing.GoalType
                    : existing.GoalType;
                var targetValue = context.ParseResult.FindResultFor(targetValueOption) is not null
                    ? context.ParseResult.GetValueForOption(targetValueOption)
                    : existing.TargetValue;
                var targetUnit = context.ParseResult.FindResultFor(targetUnitOption) is not null
                    ? context.ParseResult.GetValueForOption(targetUnitOption)
                    : existing.TargetUnit;
                var targetDate = context.ParseResult.FindResultFor(targetDateOption) is not null
                    ? context.ParseResult.GetValueForOption(targetDateOption)
                    : existing.TargetDate;

                var dto = new UpdateProgressGoalDto(
                    Title: title,
                    Description: description,
                    GoalType: goalType,
                    TargetValue: targetValue,
                    TargetUnit: targetUnit,
                    TargetDate: targetDate);

                var success = await service.UpdateGoalAsync(id, dto, userId);
                if (!success)
                {
                    OutputFormatter.WriteError($"Failed to update goal {id}", format);
                    context.ExitCode = 1;
                    return;
                }

                OutputFormatter.Write(new { Id = id, Message = "Goal updated" }, format);
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

    private static Command BuildAchieveCommand(
        Option<string?> userIdOption,
        Option<string> formatOption,
        Option<string?> connectionStringOption)
    {
        var idArg = new Argument<int>("id", "Goal ID");
        var cmd = new Command("achieve", "Mark a goal as achieved") { idArg };

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

                var success = await service.UpdateGoalStatusAsync(id, GoalStatus.Achieved, userId);
                if (!success)
                {
                    OutputFormatter.WriteError($"Goal {id} not found", format);
                    context.ExitCode = 1;
                    return;
                }

                OutputFormatter.Write(new { Id = id, Status = "Achieved" }, format);
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

    private static Command BuildAbandonCommand(
        Option<string?> userIdOption,
        Option<string> formatOption,
        Option<string?> connectionStringOption)
    {
        var idArg = new Argument<int>("id", "Goal ID");
        var cmd = new Command("abandon", "Mark a goal as abandoned") { idArg };

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

                var success = await service.UpdateGoalStatusAsync(id, GoalStatus.Abandoned, userId);
                if (!success)
                {
                    OutputFormatter.WriteError($"Goal {id} not found", format);
                    context.ExitCode = 1;
                    return;
                }

                OutputFormatter.Write(new { Id = id, Status = "Abandoned" }, format);
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

    private static Command BuildDeleteCommand(
        Option<string?> userIdOption,
        Option<string> formatOption,
        Option<string?> connectionStringOption)
    {
        var idArg = new Argument<int>("id", "Goal ID");
        var cmd = new Command("delete", "Soft-delete a goal") { idArg };

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

                var success = await service.SoftDeleteGoalAsync(id, userId);
                if (!success)
                {
                    OutputFormatter.WriteError($"Goal {id} not found", format);
                    context.ExitCode = 1;
                    return;
                }

                OutputFormatter.Write(new { Id = id, Message = "Goal deleted" }, format);
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
