using System.CommandLine;
using System.CommandLine.Invocation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Nutrir.Cli.Infrastructure;
using Nutrir.Core.DTOs;
using Nutrir.Core.Interfaces;

namespace Nutrir.Cli.Commands;

public static class ClientCommands
{
    public static Command Create(
        Option<string?> userIdOption,
        Option<string> formatOption,
        Option<string> sourceOption,
        Option<string?> connectionStringOption)
    {
        var command = new Command("clients", "Manage clients");

        command.AddCommand(CreateListCommand(formatOption, connectionStringOption));
        command.AddCommand(CreateGetCommand(formatOption, connectionStringOption));
        command.AddCommand(CreateCreateCommand(userIdOption, formatOption, connectionStringOption));
        command.AddCommand(CreateUpdateCommand(userIdOption, formatOption, connectionStringOption));
        command.AddCommand(CreateDeleteCommand(userIdOption, formatOption, connectionStringOption));

        return command;
    }

    private static Command CreateListCommand(
        Option<string> formatOption,
        Option<string?> connectionStringOption)
    {
        var searchOption = new Option<string?>("--search", "Search term to filter clients");

        var cmd = new Command("list", "List clients");
        cmd.AddOption(searchOption);

        cmd.SetHandler(async (context) =>
        {
            var format = context.ParseResult.GetValueForOption(formatOption)!;
            var connStr = context.ParseResult.GetValueForOption(connectionStringOption);
            var search = context.ParseResult.GetValueForOption(searchOption);

            try
            {
                using var host = CliHostBuilder.Build(connStr);
                using var scope = host.Services.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IClientService>();
                var result = await service.GetListAsync(search);
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

    private static Command CreateGetCommand(
        Option<string> formatOption,
        Option<string?> connectionStringOption)
    {
        var idArg = new Argument<int>("id", "Client ID");

        var cmd = new Command("get", "Get a client by ID");
        cmd.AddArgument(idArg);

        cmd.SetHandler(async (context) =>
        {
            var id = context.ParseResult.GetValueForArgument(idArg);
            var format = context.ParseResult.GetValueForOption(formatOption)!;
            var connStr = context.ParseResult.GetValueForOption(connectionStringOption);

            try
            {
                using var host = CliHostBuilder.Build(connStr);
                using var scope = host.Services.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IClientService>();
                var result = await service.GetByIdAsync(id);

                if (result is null)
                {
                    OutputFormatter.WriteError($"Client {id} not found", format);
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

    private static Command CreateCreateCommand(
        Option<string?> userIdOption,
        Option<string> formatOption,
        Option<string?> connectionStringOption)
    {
        var firstNameOption = new Option<string>("--first-name", "First name") { IsRequired = true };
        var lastNameOption = new Option<string>("--last-name", "Last name") { IsRequired = true };
        var emailOption = new Option<string?>("--email", "Email address");
        var phoneOption = new Option<string?>("--phone", "Phone number");
        var dobOption = new Option<DateOnly?>("--dob", "Date of birth (YYYY-MM-DD)");
        var nutritionistIdOption = new Option<string?>("--nutritionist-id", "Primary nutritionist ID (defaults to --user-id)");
        var consentOption = new Option<bool>("--consent", "Client has given consent");
        var notesOption = new Option<string?>("--notes", "Notes");

        var cmd = new Command("create", "Create a new client");
        cmd.AddOption(firstNameOption);
        cmd.AddOption(lastNameOption);
        cmd.AddOption(emailOption);
        cmd.AddOption(phoneOption);
        cmd.AddOption(dobOption);
        cmd.AddOption(nutritionistIdOption);
        cmd.AddOption(consentOption);
        cmd.AddOption(notesOption);

        cmd.SetHandler(async (context) =>
        {
            var format = context.ParseResult.GetValueForOption(formatOption)!;
            var connStr = context.ParseResult.GetValueForOption(connectionStringOption);

            try
            {
                var userId = ResolveUserId(context, userIdOption);
                var firstName = context.ParseResult.GetValueForOption(firstNameOption)!;
                var lastName = context.ParseResult.GetValueForOption(lastNameOption)!;
                var email = context.ParseResult.GetValueForOption(emailOption);
                var phone = context.ParseResult.GetValueForOption(phoneOption);
                var dob = context.ParseResult.GetValueForOption(dobOption);
                var nutritionistId = context.ParseResult.GetValueForOption(nutritionistIdOption) ?? userId;
                var consent = context.ParseResult.GetValueForOption(consentOption);
                var notes = context.ParseResult.GetValueForOption(notesOption);

                var dto = new ClientDto(
                    Id: 0,
                    FirstName: firstName,
                    LastName: lastName,
                    Email: email,
                    Phone: phone,
                    DateOfBirth: dob,
                    PrimaryNutritionistId: nutritionistId,
                    PrimaryNutritionistName: null,
                    ConsentGiven: consent,
                    ConsentTimestamp: consent ? DateTime.UtcNow : null,
                    ConsentPolicyVersion: null,
                    Notes: notes,
                    IsDeleted: false,
                    CreatedAt: DateTime.UtcNow,
                    UpdatedAt: null,
                    DeletedAt: null);

                using var host = CliHostBuilder.Build(connStr);
                using var scope = host.Services.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IClientService>();
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

    private static Command CreateUpdateCommand(
        Option<string?> userIdOption,
        Option<string> formatOption,
        Option<string?> connectionStringOption)
    {
        var idArg = new Argument<int>("id", "Client ID");
        var firstNameOption = new Option<string?>("--first-name", "First name");
        var lastNameOption = new Option<string?>("--last-name", "Last name");
        var emailOption = new Option<string?>("--email", "Email address");
        var phoneOption = new Option<string?>("--phone", "Phone number");
        var dobOption = new Option<DateOnly?>("--dob", "Date of birth (YYYY-MM-DD)");
        var notesOption = new Option<string?>("--notes", "Notes");

        var cmd = new Command("update", "Update a client");
        cmd.AddArgument(idArg);
        cmd.AddOption(firstNameOption);
        cmd.AddOption(lastNameOption);
        cmd.AddOption(emailOption);
        cmd.AddOption(phoneOption);
        cmd.AddOption(dobOption);
        cmd.AddOption(notesOption);

        cmd.SetHandler(async (context) =>
        {
            var id = context.ParseResult.GetValueForArgument(idArg);
            var format = context.ParseResult.GetValueForOption(formatOption)!;
            var connStr = context.ParseResult.GetValueForOption(connectionStringOption);

            try
            {
                var userId = ResolveUserId(context, userIdOption);

                using var host = CliHostBuilder.Build(connStr);
                using var scope = host.Services.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IClientService>();

                var existing = await service.GetByIdAsync(id);
                if (existing is null)
                {
                    OutputFormatter.WriteError($"Client {id} not found", format);
                    context.ExitCode = 1;
                    return;
                }

                var updated = existing with
                {
                    FirstName = context.ParseResult.FindResultFor(firstNameOption) is not null
                        ? context.ParseResult.GetValueForOption(firstNameOption)!
                        : existing.FirstName,
                    LastName = context.ParseResult.FindResultFor(lastNameOption) is not null
                        ? context.ParseResult.GetValueForOption(lastNameOption)!
                        : existing.LastName,
                    Email = context.ParseResult.FindResultFor(emailOption) is not null
                        ? context.ParseResult.GetValueForOption(emailOption)
                        : existing.Email,
                    Phone = context.ParseResult.FindResultFor(phoneOption) is not null
                        ? context.ParseResult.GetValueForOption(phoneOption)
                        : existing.Phone,
                    DateOfBirth = context.ParseResult.FindResultFor(dobOption) is not null
                        ? context.ParseResult.GetValueForOption(dobOption)
                        : existing.DateOfBirth,
                    Notes = context.ParseResult.FindResultFor(notesOption) is not null
                        ? context.ParseResult.GetValueForOption(notesOption)
                        : existing.Notes,
                };

                var success = await service.UpdateAsync(id, updated, userId);
                if (!success)
                {
                    OutputFormatter.WriteError($"Failed to update client {id}", format);
                    context.ExitCode = 1;
                    return;
                }

                var result = await service.GetByIdAsync(id);
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

    private static Command CreateDeleteCommand(
        Option<string?> userIdOption,
        Option<string> formatOption,
        Option<string?> connectionStringOption)
    {
        var idArg = new Argument<int>("id", "Client ID");

        var cmd = new Command("delete", "Soft-delete a client");
        cmd.AddArgument(idArg);

        cmd.SetHandler(async (context) =>
        {
            var id = context.ParseResult.GetValueForArgument(idArg);
            var format = context.ParseResult.GetValueForOption(formatOption)!;
            var connStr = context.ParseResult.GetValueForOption(connectionStringOption);

            try
            {
                var userId = ResolveUserId(context, userIdOption);

                using var host = CliHostBuilder.Build(connStr);
                using var scope = host.Services.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IClientService>();
                var success = await service.SoftDeleteAsync(id, userId);

                if (!success)
                {
                    OutputFormatter.WriteError($"Client {id} not found", format);
                    context.ExitCode = 1;
                    return;
                }

                OutputFormatter.Write(new { Id = id, Deleted = true }, format);
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
