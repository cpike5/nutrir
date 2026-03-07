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

    /// <summary>
    /// Starts a root-level Activity that Elastic APM bridges as a standalone transaction
    /// rather than a span nested under the current HTTP/SignalR transaction.
    /// Useful for business-important operations (AI conversations, reports) that need
    /// their own entry in APM Transactions, independent of the transport layer.
    /// </summary>
    public static TransactionScope StartTransaction(ActivitySource source, string name)
    {
        return new TransactionScope(source, name);
    }

    /// <summary>
    /// Wraps a root Activity and restores the previous Activity.Current on dispose.
    /// </summary>
    public sealed class TransactionScope : IDisposable
    {
        private readonly Activity? _previous;

        public Activity? Activity { get; }

        internal TransactionScope(ActivitySource source, string name)
        {
            _previous = System.Diagnostics.Activity.Current;
            System.Diagnostics.Activity.Current = null;
            Activity = source.StartActivity(name, ActivityKind.Server);
            if (Activity == null)
                System.Diagnostics.Activity.Current = _previous;
        }

        public void Dispose()
        {
            Activity?.Dispose();
            System.Diagnostics.Activity.Current = _previous;
        }
    }
}
