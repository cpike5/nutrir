namespace Nutrir.Core.Interfaces;

public interface IAiMarkdownRenderer
{
    /// <summary>
    /// Renders markdown text to HTML with entity links, tables, status badges, and standard formatting.
    /// </summary>
    string RenderToHtml(string markdown);

    /// <summary>
    /// Returns a user-friendly display name for an AI tool invocation.
    /// </summary>
    string FormatToolName(string toolName);
}
