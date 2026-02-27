namespace Nutrir.Web.Components.Layout;

/// <summary>
/// Scoped state service for the AI assistant panel. Allows TopBar toggle button
/// and AiAssistantPanel to communicate open/close state within the same circuit.
/// </summary>
public class AiPanelState
{
    public bool IsOpen { get; private set; }
    public bool IsWide { get; private set; }

    public event Action? OnChange;

    public void Toggle()
    {
        IsOpen = !IsOpen;
        OnChange?.Invoke();
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        OnChange?.Invoke();
    }

    public void SetOpen(bool isOpen)
    {
        if (IsOpen == isOpen) return;
        IsOpen = isOpen;
        OnChange?.Invoke();
    }

    public void SetWide(bool isWide)
    {
        if (IsWide == isWide) return;
        IsWide = isWide;
        OnChange?.Invoke();
    }
}
