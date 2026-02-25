using System.CommandLine;
using Nutrir.Cli.Commands;

var rootCommand = new RootCommand("Nutrir CLI — programmatic CRUD for the Nutrir nutrition practice platform");

// Global options
var userIdOption = new Option<string?>(
    "--user-id",
    description: "User ID for audit trail (required for mutations). Can also set NUTRIR_USER_ID env var.");

var formatOption = new Option<string>(
    "--format",
    getDefaultValue: () => "json",
    description: "Output format: json or table");

var sourceOption = new Option<string>(
    "--source",
    getDefaultValue: () => "cli",
    description: "Action source for audit trail (cli, ai-assistant, or custom)");

var connectionStringOption = new Option<string?>(
    "--connection-string",
    description: "Override database connection string");

rootCommand.AddGlobalOption(userIdOption);
rootCommand.AddGlobalOption(formatOption);
rootCommand.AddGlobalOption(sourceOption);
rootCommand.AddGlobalOption(connectionStringOption);

// Register domain commands
rootCommand.AddCommand(ClientCommands.Create(userIdOption, formatOption, sourceOption, connectionStringOption));
rootCommand.AddCommand(AppointmentCommands.Create(userIdOption, formatOption, sourceOption, connectionStringOption));
rootCommand.AddCommand(MealPlanCommands.Create(userIdOption, formatOption, sourceOption, connectionStringOption));
rootCommand.AddCommand(GoalCommands.Create(userIdOption, formatOption, sourceOption, connectionStringOption));
rootCommand.AddCommand(ProgressCommands.Create(userIdOption, formatOption, sourceOption, connectionStringOption));
rootCommand.AddCommand(UserCommands.Create(userIdOption, formatOption, sourceOption, connectionStringOption));
rootCommand.AddCommand(SearchCommand.Create(userIdOption, formatOption, sourceOption, connectionStringOption));
rootCommand.AddCommand(DashboardCommand.Create(formatOption, connectionStringOption));
rootCommand.AddCommand(AuditCommand.Create(formatOption, connectionStringOption));

return await rootCommand.InvokeAsync(args);
