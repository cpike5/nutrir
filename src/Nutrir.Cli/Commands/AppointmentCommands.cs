using System.CommandLine;
using System.CommandLine.Invocation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Nutrir.Cli.Infrastructure;
using Nutrir.Core.DTOs;
using Nutrir.Core.Enums;
using Nutrir.Core.Interfaces;

namespace Nutrir.Cli.Commands;

public static class AppointmentCommands
{
    public static Command Create(
        Option<string?> userIdOption,
        Option<string> formatOption,
        Option<string> sourceOption,
        Option<string?> connectionStringOption)
    {
        var command = new Command("appointments", "Manage appointments");

        command.AddCommand(CreateListCommand(formatOption, connectionStringOption));
        command.AddCommand(CreateGetCommand(formatOption, connectionStringOption));
        command.AddCommand(CreateCreateCommand(userIdOption, formatOption, connectionStringOption));
        command.AddCommand(CreateCancelCommand(userIdOption, formatOption, connectionStringOption));
        command.AddCommand(CreateDeleteCommand(userIdOption, formatOption, connectionStringOption));

        return command;
    }

    private static Command CreateListCommand(
        Option<string> formatOption,
        Option<string?> connectionStringOption)
    {
        var clientIdOption = new Option<int?>("--client-id", "Filter by client ID");
        var fromOption = new Option<DateTime?>("--from", "Filter from date (ISO 8601)");
        var toOption = new Option<DateTime?>("--to", "Filter to date (ISO 8601)");
        var statusOption = new Option<string?>("--status", "Filter by status (Scheduled, Confirmed, Completed, NoShow, LateCancellation, Cancelled)");

        var cmd = new Command("list", "List appointments");
        cmd.AddOption(clientIdOption);
        cmd.AddOption(fromOption);
        cmd.AddOption(toOption);
        cmd.AddOption(statusOption);

        cmd.SetHandler(async (context) =>
        {
            var format = context.ParseResult.GetValueForOption(formatOption)!;
            var connStr = context.ParseResult.GetValueForOption(connectionStringOption);
            var clientId = context.ParseResult.GetValueForOption(clientIdOption);
            var from = context.ParseResult.GetValueForOption(fromOption);
            var to = context.ParseResult.GetValueForOption(toOption);
            var statusStr = context.ParseResult.GetValueForOption(statusOption);

            try
            {
                AppointmentStatus? status = null;
                if (!string.IsNullOrWhiteSpace(statusStr))
                    status = Enum.Parse<AppointmentStatus>(statusStr, ignoreCase: true);

                using var host = CliHostBuilder.Build(connStr);
                using var scope = host.Services.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IAppointmentService>();
                var result = await service.GetListAsync(from, to, clientId, status);
                OutputFormatter.Write(result, format);
                context.ExitCode = 0;
            }
            catch (ArgumentException ex)
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

    private static Command CreateGetCommand(
        Option<string> formatOption,
        Option<string?> connectionStringOption)
    {
        var idArg = new Argument<int>("id", "Appointment ID");

        var cmd = new Command("get", "Get an appointment by ID");
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
                var service = scope.ServiceProvider.GetRequiredService<IAppointmentService>();
                var result = await service.GetByIdAsync(id);

                if (result is null)
                {
                    OutputFormatter.WriteError($"Appointment {id} not found", format);
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
        var clientIdOption = new Option<int>("--client-id", "Client ID") { IsRequired = true };
        var typeOption = new Option<string>("--type", "Appointment type (InitialConsultation, FollowUp, CheckIn)") { IsRequired = true };
        var startOption = new Option<DateTime>("--start", "Start time (ISO 8601)") { IsRequired = true };
        var durationOption = new Option<int>("--duration", "Duration in minutes") { IsRequired = true };
        var locationOption = new Option<string>("--location", "Location (InPerson, Virtual, Phone)") { IsRequired = true };
        var urlOption = new Option<string?>("--url", "Virtual meeting URL");
        var locationNotesOption = new Option<string?>("--location-notes", "Location notes");
        var notesOption = new Option<string?>("--notes", "Notes");

        var cmd = new Command("create", "Create a new appointment");
        cmd.AddOption(clientIdOption);
        cmd.AddOption(typeOption);
        cmd.AddOption(startOption);
        cmd.AddOption(durationOption);
        cmd.AddOption(locationOption);
        cmd.AddOption(urlOption);
        cmd.AddOption(locationNotesOption);
        cmd.AddOption(notesOption);

        cmd.SetHandler(async (context) =>
        {
            var format = context.ParseResult.GetValueForOption(formatOption)!;
            var connStr = context.ParseResult.GetValueForOption(connectionStringOption);

            try
            {
                var userId = ResolveUserId(context, userIdOption);
                var clientId = context.ParseResult.GetValueForOption(clientIdOption);
                var type = Enum.Parse<AppointmentType>(context.ParseResult.GetValueForOption(typeOption)!, ignoreCase: true);
                var start = context.ParseResult.GetValueForOption(startOption);
                var duration = context.ParseResult.GetValueForOption(durationOption);
                var location = Enum.Parse<AppointmentLocation>(context.ParseResult.GetValueForOption(locationOption)!, ignoreCase: true);
                var url = context.ParseResult.GetValueForOption(urlOption);
                var locationNotes = context.ParseResult.GetValueForOption(locationNotesOption);
                var notes = context.ParseResult.GetValueForOption(notesOption);

                var dto = new CreateAppointmentDto(
                    ClientId: clientId,
                    Type: type,
                    StartTime: start,
                    DurationMinutes: duration,
                    Location: location,
                    VirtualMeetingUrl: url,
                    LocationNotes: locationNotes,
                    Notes: notes);

                using var host = CliHostBuilder.Build(connStr);
                using var scope = host.Services.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IAppointmentService>();
                var result = await service.CreateAsync(dto, userId);
                OutputFormatter.Write(result, format);
                context.ExitCode = 0;
            }
            catch (InvalidOperationException ex)
            {
                OutputFormatter.WriteError(ex.Message, format);
                context.ExitCode = 1;
            }
            catch (ArgumentException ex)
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

    private static Command CreateCancelCommand(
        Option<string?> userIdOption,
        Option<string> formatOption,
        Option<string?> connectionStringOption)
    {
        var idArg = new Argument<int>("id", "Appointment ID");
        var reasonOption = new Option<string?>("--reason", "Cancellation reason");

        var cmd = new Command("cancel", "Cancel an appointment");
        cmd.AddArgument(idArg);
        cmd.AddOption(reasonOption);

        cmd.SetHandler(async (context) =>
        {
            var id = context.ParseResult.GetValueForArgument(idArg);
            var format = context.ParseResult.GetValueForOption(formatOption)!;
            var connStr = context.ParseResult.GetValueForOption(connectionStringOption);

            try
            {
                var userId = ResolveUserId(context, userIdOption);
                var reason = context.ParseResult.GetValueForOption(reasonOption);

                using var host = CliHostBuilder.Build(connStr);
                using var scope = host.Services.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IAppointmentService>();
                var success = await service.UpdateStatusAsync(id, AppointmentStatus.Cancelled, userId, reason);

                if (!success)
                {
                    OutputFormatter.WriteError($"Appointment {id} not found", format);
                    context.ExitCode = 1;
                    return;
                }

                OutputFormatter.Write(new { Id = id, Status = "Cancelled" }, format);
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
        var idArg = new Argument<int>("id", "Appointment ID");

        var cmd = new Command("delete", "Soft-delete an appointment");
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
                var service = scope.ServiceProvider.GetRequiredService<IAppointmentService>();
                var success = await service.SoftDeleteAsync(id, userId);

                if (!success)
                {
                    OutputFormatter.WriteError($"Appointment {id} not found", format);
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
