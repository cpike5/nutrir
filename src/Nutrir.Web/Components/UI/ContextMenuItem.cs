namespace Nutrir.Web.Components.UI;

public enum ContextMenuItemType
{
    Action,
    Submenu,
    Separator,
    SectionLabel
}

public record ContextMenuItem
{
    public ContextMenuItemType Type { get; init; }
    public string Id { get; init; } = "";
    public string? Icon { get; init; }
    public string Label { get; init; } = "";
    public string? ShortcutText { get; init; }
    public bool IsDanger { get; init; }
    public bool IsDisabled { get; init; }
    public List<ContextMenuItem> Children { get; init; } = [];

    public static ContextMenuItem Action(string label, string id, string? icon = null, string? shortcut = null, bool isDanger = false, bool isDisabled = false)
        => new()
        {
            Type = ContextMenuItemType.Action,
            Id = id,
            Icon = icon,
            Label = label,
            ShortcutText = shortcut,
            IsDanger = isDanger,
            IsDisabled = isDisabled
        };

    public static ContextMenuItem Submenu(string label, List<ContextMenuItem> children, string? icon = null)
        => new()
        {
            Type = ContextMenuItemType.Submenu,
            Label = label,
            Icon = icon,
            Children = children
        };

    public static ContextMenuItem Separator()
        => new() { Type = ContextMenuItemType.Separator };

    public static ContextMenuItem SectionLabel(string label)
        => new() { Type = ContextMenuItemType.SectionLabel, Label = label };
}
