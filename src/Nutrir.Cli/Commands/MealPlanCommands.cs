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

public static class MealPlanCommands
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
        var command = new Command("meal-plans", "Manage meal plans");

        command.AddCommand(BuildListCommand(formatOption, connectionStringOption));
        command.AddCommand(BuildGetCommand(formatOption, connectionStringOption));
        command.AddCommand(BuildCreateCommand(userIdOption, formatOption, connectionStringOption));
        command.AddCommand(BuildAddContentCommand(userIdOption, formatOption, connectionStringOption));
        command.AddCommand(BuildActivateCommand(userIdOption, formatOption, connectionStringOption));
        command.AddCommand(BuildArchiveCommand(userIdOption, formatOption, connectionStringOption));
        command.AddCommand(BuildDuplicateCommand(userIdOption, formatOption, connectionStringOption));
        command.AddCommand(BuildDeleteCommand(userIdOption, formatOption, connectionStringOption));

        return command;
    }

    private static Command BuildListCommand(
        Option<string> formatOption,
        Option<string?> connectionStringOption)
    {
        var clientIdOption = new Option<int?>("--client-id", "Filter by client ID");
        var statusOption = new Option<MealPlanStatus?>("--status", "Filter by status (Draft, Active, Archived)");

        var cmd = new Command("list", "List meal plans") { clientIdOption, statusOption };

        cmd.SetHandler(async (InvocationContext context) =>
        {
            var clientId = context.ParseResult.GetValueForOption(clientIdOption);
            var status = context.ParseResult.GetValueForOption(statusOption);
            var format = context.ParseResult.GetValueForOption(formatOption)!;
            var connStr = context.ParseResult.GetValueForOption(connectionStringOption);

            try
            {
                using var host = CliHostBuilder.Build(connStr);
                await host.StartAsync();
                using var scope = host.Services.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IMealPlanService>();

                var result = await service.GetListAsync(clientId, status);
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
        var idArg = new Argument<int>("id", "Meal plan ID");
        var cmd = new Command("get", "Get meal plan details") { idArg };

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
                var service = scope.ServiceProvider.GetRequiredService<IMealPlanService>();

                var result = await service.GetByIdAsync(id);
                if (result is null)
                {
                    OutputFormatter.WriteError($"Meal plan {id} not found", format);
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
        var titleOption = new Option<string>("--title", "Meal plan title") { IsRequired = true };
        var daysOption = new Option<int>("--days", "Number of days") { IsRequired = true };
        var descriptionOption = new Option<string?>("--description", "Description");
        var caloriesOption = new Option<decimal?>("--calories", "Calorie target");
        var proteinOption = new Option<decimal?>("--protein", "Protein target (g)");
        var carbsOption = new Option<decimal?>("--carbs", "Carbs target (g)");
        var fatOption = new Option<decimal?>("--fat", "Fat target (g)");
        var notesOption = new Option<string?>("--notes", "Notes");
        var instructionsOption = new Option<string?>("--instructions", "Instructions");

        var cmd = new Command("create", "Create a new meal plan")
        {
            clientIdOption, titleOption, daysOption, descriptionOption,
            caloriesOption, proteinOption, carbsOption, fatOption,
            notesOption, instructionsOption
        };

        cmd.SetHandler(async (InvocationContext context) =>
        {
            var format = context.ParseResult.GetValueForOption(formatOption)!;
            var connStr = context.ParseResult.GetValueForOption(connectionStringOption);

            try
            {
                var userId = ResolveUserId(context, userIdOption);
                var dto = new CreateMealPlanDto(
                    ClientId: context.ParseResult.GetValueForOption(clientIdOption),
                    Title: context.ParseResult.GetValueForOption(titleOption)!,
                    Description: context.ParseResult.GetValueForOption(descriptionOption),
                    StartDate: null,
                    EndDate: null,
                    CalorieTarget: context.ParseResult.GetValueForOption(caloriesOption),
                    ProteinTargetG: context.ParseResult.GetValueForOption(proteinOption),
                    CarbsTargetG: context.ParseResult.GetValueForOption(carbsOption),
                    FatTargetG: context.ParseResult.GetValueForOption(fatOption),
                    Notes: context.ParseResult.GetValueForOption(notesOption),
                    Instructions: context.ParseResult.GetValueForOption(instructionsOption),
                    NumberOfDays: context.ParseResult.GetValueForOption(daysOption));

                using var host = CliHostBuilder.Build(connStr);
                await host.StartAsync();
                using var scope = host.Services.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IMealPlanService>();

                var result = await service.CreateAsync(dto, userId);
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

    private static Command BuildAddContentCommand(
        Option<string?> userIdOption,
        Option<string> formatOption,
        Option<string?> connectionStringOption)
    {
        var idArg = new Argument<int>("id", "Meal plan ID");
        var fromJsonOption = new Option<string>("--from-json", "Path to JSON file with meal plan content") { IsRequired = true };

        var cmd = new Command("add-content", "Add content to a meal plan from a JSON file") { idArg, fromJsonOption };

        cmd.SetHandler(async (InvocationContext context) =>
        {
            var format = context.ParseResult.GetValueForOption(formatOption)!;
            var connStr = context.ParseResult.GetValueForOption(connectionStringOption);

            try
            {
                var userId = ResolveUserId(context, userIdOption);
                var id = context.ParseResult.GetValueForArgument(idArg);
                var filePath = context.ParseResult.GetValueForOption(fromJsonOption)!;

                if (!File.Exists(filePath))
                {
                    OutputFormatter.WriteError($"File not found: {filePath}", format);
                    context.ExitCode = 1;
                    return;
                }

                var json = await File.ReadAllTextAsync(filePath);
                var dto = JsonSerializer.Deserialize<SaveMealPlanContentDto>(json, JsonReadOptions);

                if (dto is null)
                {
                    OutputFormatter.WriteError("Failed to deserialize JSON content", format);
                    context.ExitCode = 1;
                    return;
                }

                // Override MealPlanId with the argument
                dto = dto with { MealPlanId = id };

                using var host = CliHostBuilder.Build(connStr);
                await host.StartAsync();
                using var scope = host.Services.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IMealPlanService>();

                var success = await service.SaveContentAsync(dto, userId);
                if (!success)
                {
                    OutputFormatter.WriteError($"Failed to save content for meal plan {id}", format);
                    context.ExitCode = 1;
                    return;
                }

                OutputFormatter.Write(new { MealPlanId = id, Message = "Content saved successfully" }, format);
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

    private static Command BuildActivateCommand(
        Option<string?> userIdOption,
        Option<string> formatOption,
        Option<string?> connectionStringOption)
    {
        var idArg = new Argument<int>("id", "Meal plan ID");
        var cmd = new Command("activate", "Activate a meal plan") { idArg };

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
                var service = scope.ServiceProvider.GetRequiredService<IMealPlanService>();

                var success = await service.UpdateStatusAsync(id, MealPlanStatus.Active, userId);
                if (!success)
                {
                    OutputFormatter.WriteError($"Meal plan {id} not found", format);
                    context.ExitCode = 1;
                    return;
                }

                OutputFormatter.Write(new { Id = id, Status = "Active" }, format);
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

    private static Command BuildArchiveCommand(
        Option<string?> userIdOption,
        Option<string> formatOption,
        Option<string?> connectionStringOption)
    {
        var idArg = new Argument<int>("id", "Meal plan ID");
        var cmd = new Command("archive", "Archive a meal plan") { idArg };

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
                var service = scope.ServiceProvider.GetRequiredService<IMealPlanService>();

                var success = await service.UpdateStatusAsync(id, MealPlanStatus.Archived, userId);
                if (!success)
                {
                    OutputFormatter.WriteError($"Meal plan {id} not found", format);
                    context.ExitCode = 1;
                    return;
                }

                OutputFormatter.Write(new { Id = id, Status = "Archived" }, format);
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

    private static Command BuildDuplicateCommand(
        Option<string?> userIdOption,
        Option<string> formatOption,
        Option<string?> connectionStringOption)
    {
        var idArg = new Argument<int>("id", "Meal plan ID");
        var cmd = new Command("duplicate", "Duplicate a meal plan") { idArg };

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
                var service = scope.ServiceProvider.GetRequiredService<IMealPlanService>();

                var success = await service.DuplicateAsync(id, userId);
                if (!success)
                {
                    OutputFormatter.WriteError($"Meal plan {id} not found", format);
                    context.ExitCode = 1;
                    return;
                }

                OutputFormatter.Write(new { Id = id, Message = "Meal plan duplicated" }, format);
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
        var idArg = new Argument<int>("id", "Meal plan ID");
        var cmd = new Command("delete", "Soft-delete a meal plan") { idArg };

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
                var service = scope.ServiceProvider.GetRequiredService<IMealPlanService>();

                var success = await service.SoftDeleteAsync(id, userId);
                if (!success)
                {
                    OutputFormatter.WriteError($"Meal plan {id} not found", format);
                    context.ExitCode = 1;
                    return;
                }

                OutputFormatter.Write(new { Id = id, Message = "Meal plan deleted" }, format);
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
