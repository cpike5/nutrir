using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nutrir.Cli.Infrastructure;

public static class OutputFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public static void Write(object? data, string format = "json")
    {
        if (format.Equals("table", StringComparison.OrdinalIgnoreCase))
        {
            WriteTable(data);
            return;
        }

        var result = new CliResult(true, data);
        Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
    }

    public static void WriteError(string error, string format = "json")
    {
        if (format.Equals("table", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"Error: {error}");
            return;
        }

        var result = new CliResult(false, Error: error);
        Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
    }

    private static void WriteTable(object? data)
    {
        if (data is null)
        {
            Console.WriteLine("(no data)");
            return;
        }

        // For lists, format as simple table
        if (data is System.Collections.IEnumerable enumerable and not string)
        {
            var items = enumerable.Cast<object>().ToList();
            if (items.Count == 0)
            {
                Console.WriteLine("(no results)");
                return;
            }

            var props = items[0].GetType().GetProperties();
            var headers = props.Select(p => p.Name).ToArray();
            var rows = items.Select(item =>
                props.Select(p => p.GetValue(item)?.ToString() ?? "").ToArray()
            ).ToList();

            // Calculate column widths
            var widths = new int[headers.Length];
            for (var i = 0; i < headers.Length; i++)
            {
                widths[i] = Math.Max(headers[i].Length,
                    rows.Max(r => r[i].Length));
                widths[i] = Math.Min(widths[i], 40); // cap width
            }

            // Print header
            Console.WriteLine(string.Join("  ",
                headers.Select((h, i) => h.PadRight(widths[i]))));
            Console.WriteLine(string.Join("  ",
                widths.Select(w => new string('-', w))));

            // Print rows
            foreach (var row in rows)
            {
                Console.WriteLine(string.Join("  ",
                    row.Select((v, i) => v.Length > widths[i]
                        ? v[..(widths[i] - 3)] + "..."
                        : v.PadRight(widths[i]))));
            }
        }
        else
        {
            // Single object — key/value pairs
            var props = data.GetType().GetProperties();
            var maxKey = props.Max(p => p.Name.Length);
            foreach (var prop in props)
            {
                var value = prop.GetValue(data)?.ToString() ?? "(null)";
                Console.WriteLine($"{prop.Name.PadRight(maxKey)}  {value}");
            }
        }
    }
}
