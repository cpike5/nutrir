using System.Diagnostics;

namespace Nutrir.Infrastructure.Diagnostics;

/// <summary>
/// Centralized ActivitySource definitions for custom APM spans.
/// Elastic APM (1.26+) automatically bridges System.Diagnostics.Activity to APM spans.
/// </summary>
public static class NutrirTelemetry
{
    public const string ServiceName = "Nutrir";

    /// <summary>AI assistant operations: conversations, API calls, tool execution.</summary>
    public static readonly ActivitySource AiSource = new($"{ServiceName}.AI");

    /// <summary>Domain service operations: CRUD, search, dashboard, reports.</summary>
    public static readonly ActivitySource AppSource = new($"{ServiceName}.App");

    /// <summary>Document generation: PDF, DOCX rendering.</summary>
    public static readonly ActivitySource DocSource = new($"{ServiceName}.Documents");
}
