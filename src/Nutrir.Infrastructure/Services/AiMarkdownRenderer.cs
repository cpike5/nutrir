using System.Text;
using System.Text.RegularExpressions;
using Nutrir.Core.Interfaces;

namespace Nutrir.Infrastructure.Services;

public class AiMarkdownRenderer : IAiMarkdownRenderer
{
    private static readonly HashSet<string> StatusSuccess = new(StringComparer.OrdinalIgnoreCase)
        { "Confirmed", "Active", "Completed" };

    private static readonly HashSet<string> StatusWarning = new(StringComparer.OrdinalIgnoreCase)
        { "Scheduled", "Pending", "Draft" };

    private static readonly HashSet<string> StatusError = new(StringComparer.OrdinalIgnoreCase)
        { "Cancelled", "No-show", "Expired" };

    public string RenderToHtml(string markdown)
    {
        if (string.IsNullOrEmpty(markdown)) return "";

        var escaped = System.Net.WebUtility.HtmlEncode(markdown);

        // Entity link chips: [[type:id:display]]
        escaped = Regex.Replace(escaped,
            @"\[\[(\w+):(\d+|[\w-]+):(.+?)\]\]",
            match =>
            {
                var entityType = match.Groups[1].Value;
                var id = match.Groups[2].Value;
                var display = match.Groups[3].Value;
                var href = entityType switch
                {
                    "client" => $"/clients/{id}",
                    "appointment" => $"/appointments/{id}",
                    "meal_plan" => $"/meal-plans/{id}",
                    "user" => $"/admin/users/{id}",
                    _ => (string?)null
                };
                return href is not null
                    ? $"<a href=\"{href}\" class=\"cc-ai-entity-chip\">{display}</a>"
                    : display;
            });

        // Code blocks (```)
        escaped = Regex.Replace(escaped,
            @"```(\w*)\r?\n([\s\S]*?)```",
            "<pre><code>$2</code></pre>");

        // Inline code
        escaped = Regex.Replace(escaped,
            @"`([^`]+)`",
            "<code>$1</code>");

        // Bold
        escaped = Regex.Replace(escaped,
            @"\*\*(.+?)\*\*",
            "<strong>$1</strong>");

        // Italic
        escaped = Regex.Replace(escaped,
            @"\*(.+?)\*",
            "<em>$1</em>");

        // Headers
        escaped = Regex.Replace(escaped,
            @"^### (.+)$",
            "<div class=\"cc-ai-h3\">$1</div>",
            RegexOptions.Multiline);
        escaped = Regex.Replace(escaped,
            @"^## (.+)$",
            "<div class=\"cc-ai-h2\">$1</div>",
            RegexOptions.Multiline);
        escaped = Regex.Replace(escaped,
            @"^# (.+)$",
            "<div class=\"cc-ai-h1\">$1</div>",
            RegexOptions.Multiline);

        // Blockquotes
        escaped = Regex.Replace(escaped,
            @"^&gt; (.+)$",
            "<div class=\"cc-ai-blockquote\">$1</div>",
            RegexOptions.Multiline);

        // Horizontal rules
        escaped = Regex.Replace(escaped,
            @"^---+$",
            "<hr/>",
            RegexOptions.Multiline);

        // Tables
        escaped = Regex.Replace(escaped,
            @"(\|.+\|\r?\n\|[-| :]+\|\r?\n(?:\|.+\|\r?\n?)+)",
            match => ConvertTable(match.Value),
            RegexOptions.Multiline);

        // Unordered list items
        escaped = Regex.Replace(escaped,
            @"^[-*] (.+)$",
            "<li data-ul>$1</li>",
            RegexOptions.Multiline);

        // Ordered list items
        escaped = Regex.Replace(escaped,
            @"^\d+\. (.+)$",
            "<li data-ol>$1</li>",
            RegexOptions.Multiline);

        // Wrap consecutive list items
        escaped = Regex.Replace(escaped,
            @"(<li data-ul>[\s\S]*?</li>)(?=\s*(?!<li data-ul>))",
            match => "<ul>" + match.Value + "</ul>");
        escaped = Regex.Replace(escaped,
            @"(<li data-ol>[\s\S]*?</li>)(?=\s*(?!<li data-ol>))",
            match => "<ol>" + match.Value + "</ol>");

        // Clean up data markers
        escaped = escaped.Replace(" data-ul", "").Replace(" data-ol", "");

        // Line breaks
        escaped = escaped.Replace("\r\n", "\n");
        escaped = Regex.Replace(escaped, @"\n\n", "<br/><br/>");
        escaped = Regex.Replace(escaped, @"\n", "<br/>");

        // Clean up excessive <br/> around block elements
        escaped = Regex.Replace(escaped,
            @"(<br/>)+\s*(<(?:div|table|ul|ol|hr|pre)[ >])",
            "$2");
        escaped = Regex.Replace(escaped,
            @"(</(?:div|table|ul|ol|hr|pre)>)\s*(<br/>)+",
            "$1");

        return escaped;
    }

    public string FormatToolName(string toolName) => toolName switch
    {
        "list_clients" => "clients",
        "get_client" => "client details",
        "create_client" => "creating client",
        "update_client" => "updating client",
        "delete_client" => "deleting client",
        "list_appointments" => "appointments",
        "get_appointment" => "appointment details",
        "create_appointment" => "creating appointment",
        "update_appointment" => "updating appointment",
        "cancel_appointment" => "cancelling appointment",
        "delete_appointment" => "deleting appointment",
        "list_meal_plans" => "meal plans",
        "get_meal_plan" => "meal plan details",
        "create_meal_plan" => "creating meal plan",
        "update_meal_plan" => "updating meal plan",
        "activate_meal_plan" => "activating meal plan",
        "archive_meal_plan" => "archiving meal plan",
        "duplicate_meal_plan" => "duplicating meal plan",
        "delete_meal_plan" => "deleting meal plan",
        "list_goals" => "goals",
        "get_goal" => "goal details",
        "create_goal" => "creating goal",
        "update_goal" => "updating goal",
        "achieve_goal" => "achieving goal",
        "abandon_goal" => "abandoning goal",
        "delete_goal" => "deleting goal",
        "list_progress" => "progress entries",
        "get_progress_entry" => "progress details",
        "create_progress_entry" => "creating progress entry",
        "delete_progress_entry" => "deleting progress entry",
        "list_users" => "users",
        "get_user" => "user details",
        "create_user" => "creating user",
        "change_user_role" => "changing user role",
        "deactivate_user" => "deactivating user",
        "reactivate_user" => "reactivating user",
        "reset_user_password" => "resetting password",
        "search" => "search results",
        "get_dashboard" => "dashboard data",
        _ => toolName.Replace('_', ' ')
    };

    private static string ApplyStatusBadge(string cell)
    {
        var trimmed = cell.Trim();
        if (StatusSuccess.Contains(trimmed))
            return $"<span class=\"cc-ai-status-success\">{cell}</span>";
        if (StatusWarning.Contains(trimmed))
            return $"<span class=\"cc-ai-status-warning\">{cell}</span>";
        if (StatusError.Contains(trimmed))
            return $"<span class=\"cc-ai-status-error\">{cell}</span>";
        return cell;
    }

    private static string ConvertTable(string markdown)
    {
        var lines = markdown.Trim().Split('\n')
            .Select(l => l.Trim().Trim('|'))
            .ToArray();

        if (lines.Length < 2) return markdown;

        var sb = new StringBuilder();
        sb.Append("<div class=\"cc-ai-table-wrap\"><table class=\"cc-ai-table\">");

        // Header
        var headers = lines[0].Split('|').Select(h => h.Trim()).ToArray();
        sb.Append("<thead><tr>");
        foreach (var h in headers)
            sb.Append($"<th>{h}</th>");
        sb.Append("</tr></thead>");

        // Body (skip separator line at index 1)
        sb.Append("<tbody>");
        for (int i = 2; i < lines.Length; i++)
        {
            var cells = lines[i].Split('|').Select(c => c.Trim()).ToArray();
            sb.Append("<tr>");
            foreach (var c in cells)
                sb.Append($"<td>{ApplyStatusBadge(c)}</td>");
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table></div>");

        return sb.ToString();
    }
}
