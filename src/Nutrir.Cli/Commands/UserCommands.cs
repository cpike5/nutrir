using System.CommandLine;
using System.CommandLine.Invocation;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Nutrir.Cli.Infrastructure;
using Nutrir.Core.DTOs;
using Nutrir.Core.Interfaces;

namespace Nutrir.Cli.Commands;

public static class UserCommands
{
    public static Command Create(
        Option<string?> userIdOption,
        Option<string> formatOption,
        Option<string> sourceOption,
        Option<string?> connectionStringOption)
    {
        var command = new Command("users", "Manage users");

        command.AddCommand(CreateListCommand(formatOption, connectionStringOption));
        command.AddCommand(CreateGetCommand(formatOption, connectionStringOption));
        command.AddCommand(CreateCreateCommand(userIdOption, formatOption, connectionStringOption));
        command.AddCommand(CreateUpdateCommand(userIdOption, formatOption, connectionStringOption));
        command.AddCommand(CreateChangeRoleCommand(userIdOption, formatOption, connectionStringOption));
        command.AddCommand(CreateDeactivateCommand(userIdOption, formatOption, connectionStringOption));
        command.AddCommand(CreateReactivateCommand(userIdOption, formatOption, connectionStringOption));
        command.AddCommand(CreateResetPasswordCommand(userIdOption, formatOption, connectionStringOption));

        return command;
    }

    private static Command CreateListCommand(
        Option<string> formatOption,
        Option<string?> connectionStringOption)
    {
        var searchOption = new Option<string?>("--search", "Search term to filter users");
        var roleOption = new Option<string?>("--role", "Filter by role");
        var activeOption = new Option<bool?>("--active", "Filter by active status");

        var cmd = new Command("list", "List users");
        cmd.AddOption(searchOption);
        cmd.AddOption(roleOption);
        cmd.AddOption(activeOption);

        cmd.SetHandler(async (context) =>
        {
            var format = context.ParseResult.GetValueForOption(formatOption)!;
            var connStr = context.ParseResult.GetValueForOption(connectionStringOption);
            var search = context.ParseResult.GetValueForOption(searchOption);
            var role = context.ParseResult.GetValueForOption(roleOption);
            var active = context.ParseResult.GetValueForOption(activeOption);

            try
            {
                using var host = CliHostBuilder.Build(connStr);
                using var scope = host.Services.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IUserManagementService>();
                var result = await service.GetUsersAsync(search, role, active);
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
        var idArg = new Argument<string>("id", "User ID (GUID)");

        var cmd = new Command("get", "Get a user by ID");
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
                var service = scope.ServiceProvider.GetRequiredService<IUserManagementService>();
                var result = await service.GetUserByIdAsync(id);

                if (result is null)
                {
                    OutputFormatter.WriteError($"User {id} not found", format);
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
        var emailOption = new Option<string>("--email", "Email address") { IsRequired = true };
        var roleOption = new Option<string>("--role", "User role") { IsRequired = true };
        var passwordOption = new Option<string?>("--password", "Password (generated if not provided)");

        var cmd = new Command("create", "Create a new user");
        cmd.AddOption(firstNameOption);
        cmd.AddOption(lastNameOption);
        cmd.AddOption(emailOption);
        cmd.AddOption(roleOption);
        cmd.AddOption(passwordOption);

        cmd.SetHandler(async (context) =>
        {
            var format = context.ParseResult.GetValueForOption(formatOption)!;
            var connStr = context.ParseResult.GetValueForOption(connectionStringOption);

            try
            {
                var userId = ResolveUserId(context, userIdOption);
                var firstName = context.ParseResult.GetValueForOption(firstNameOption)!;
                var lastName = context.ParseResult.GetValueForOption(lastNameOption)!;
                var email = context.ParseResult.GetValueForOption(emailOption)!;
                var role = context.ParseResult.GetValueForOption(roleOption)!;
                var password = context.ParseResult.GetValueForOption(passwordOption);

                var dto = new CreateUserDto(firstName, lastName, email, role, password);

                using var host = CliHostBuilder.Build(connStr);
                using var scope = host.Services.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IUserManagementService>();
                var result = await service.CreateUserAsync(dto, userId);
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
        var idArg = new Argument<string>("id", "User ID (GUID)");
        var firstNameOption = new Option<string?>("--first-name", "First name");
        var lastNameOption = new Option<string?>("--last-name", "Last name");
        var displayNameOption = new Option<string?>("--display-name", "Display name");
        var emailOption = new Option<string?>("--email", "Email address");

        var cmd = new Command("update", "Update a user profile");
        cmd.AddArgument(idArg);
        cmd.AddOption(firstNameOption);
        cmd.AddOption(lastNameOption);
        cmd.AddOption(displayNameOption);
        cmd.AddOption(emailOption);

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
                var service = scope.ServiceProvider.GetRequiredService<IUserManagementService>();

                var existing = await service.GetUserByIdAsync(id);
                if (existing is null)
                {
                    OutputFormatter.WriteError($"User {id} not found", format);
                    context.ExitCode = 1;
                    return;
                }

                var firstName = context.ParseResult.FindResultFor(firstNameOption) is not null
                    ? context.ParseResult.GetValueForOption(firstNameOption)!
                    : existing.FirstName;
                var lastName = context.ParseResult.FindResultFor(lastNameOption) is not null
                    ? context.ParseResult.GetValueForOption(lastNameOption)!
                    : existing.LastName;
                var displayName = context.ParseResult.FindResultFor(displayNameOption) is not null
                    ? context.ParseResult.GetValueForOption(displayNameOption)!
                    : existing.DisplayName;
                var email = context.ParseResult.FindResultFor(emailOption) is not null
                    ? context.ParseResult.GetValueForOption(emailOption)!
                    : existing.Email;

                var success = await service.UpdateProfileAsync(id, firstName, lastName, displayName, email);
                if (!success)
                {
                    OutputFormatter.WriteError($"Failed to update user {id}", format);
                    context.ExitCode = 1;
                    return;
                }

                var result = await service.GetUserByIdAsync(id);
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

    private static Command CreateChangeRoleCommand(
        Option<string?> userIdOption,
        Option<string> formatOption,
        Option<string?> connectionStringOption)
    {
        var idArg = new Argument<string>("id", "User ID (GUID)");
        var roleOption = new Option<string>("--role", "New role") { IsRequired = true };

        var cmd = new Command("change-role", "Change a user's role");
        cmd.AddArgument(idArg);
        cmd.AddOption(roleOption);

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
                var service = scope.ServiceProvider.GetRequiredService<IUserManagementService>();
                var success = await service.ChangeRoleAsync(id, context.ParseResult.GetValueForOption(roleOption)!, userId);

                if (!success)
                {
                    OutputFormatter.WriteError($"Failed to change role for user {id}", format);
                    context.ExitCode = 1;
                    return;
                }

                var result = await service.GetUserByIdAsync(id);
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

    private static Command CreateDeactivateCommand(
        Option<string?> userIdOption,
        Option<string> formatOption,
        Option<string?> connectionStringOption)
    {
        var idArg = new Argument<string>("id", "User ID (GUID)");

        var cmd = new Command("deactivate", "Deactivate a user");
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
                var service = scope.ServiceProvider.GetRequiredService<IUserManagementService>();
                var success = await service.DeactivateAsync(id, userId);

                if (!success)
                {
                    OutputFormatter.WriteError($"Failed to deactivate user {id}", format);
                    context.ExitCode = 1;
                    return;
                }

                OutputFormatter.Write(new { Id = id, Deactivated = true }, format);
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

    private static Command CreateReactivateCommand(
        Option<string?> userIdOption,
        Option<string> formatOption,
        Option<string?> connectionStringOption)
    {
        var idArg = new Argument<string>("id", "User ID (GUID)");

        var cmd = new Command("reactivate", "Reactivate a user");
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
                var service = scope.ServiceProvider.GetRequiredService<IUserManagementService>();
                var success = await service.ReactivateAsync(id, userId);

                if (!success)
                {
                    OutputFormatter.WriteError($"Failed to reactivate user {id}", format);
                    context.ExitCode = 1;
                    return;
                }

                OutputFormatter.Write(new { Id = id, Reactivated = true }, format);
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

    private static Command CreateResetPasswordCommand(
        Option<string?> userIdOption,
        Option<string> formatOption,
        Option<string?> connectionStringOption)
    {
        var idArg = new Argument<string>("id", "User ID (GUID)");
        var passwordOption = new Option<string?>("--password", "New password (generated if not provided)");

        var cmd = new Command("reset-password", "Reset a user's password");
        cmd.AddArgument(idArg);
        cmd.AddOption(passwordOption);

        cmd.SetHandler(async (context) =>
        {
            var id = context.ParseResult.GetValueForArgument(idArg);
            var format = context.ParseResult.GetValueForOption(formatOption)!;
            var connStr = context.ParseResult.GetValueForOption(connectionStringOption);

            try
            {
                var userId = ResolveUserId(context, userIdOption);
                var password = context.ParseResult.GetValueForOption(passwordOption)
                               ?? GeneratePassword(16);
                var wasGenerated = context.ParseResult.FindResultFor(passwordOption) is null;

                using var host = CliHostBuilder.Build(connStr);
                using var scope = host.Services.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IUserManagementService>();
                var success = await service.ResetPasswordAsync(id, password, userId);

                if (!success)
                {
                    OutputFormatter.WriteError($"Failed to reset password for user {id}", format);
                    context.ExitCode = 1;
                    return;
                }

                var output = wasGenerated
                    ? new { Id = id, PasswordReset = true, GeneratedPassword = password }
                    : (object)new { Id = id, PasswordReset = true };

                OutputFormatter.Write(output, format);
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

    private static string GeneratePassword(int length)
    {
        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#$%^&*";
        return RandomNumberGenerator.GetString(chars, length);
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
