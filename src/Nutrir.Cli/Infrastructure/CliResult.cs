namespace Nutrir.Cli.Infrastructure;

public record CliResult<T>(bool Success, T? Data = default, string? Error = null)
{
    public static CliResult<T> Ok(T data) => new(true, data);
    public static CliResult<T> Fail(string error) => new(false, Error: error);
}

public record CliResult(bool Success, object? Data = null, string? Error = null)
{
    public static CliResult Ok(object? data = null) => new(true, data);
    public static CliResult Fail(string error) => new(false, Error: error);
}
