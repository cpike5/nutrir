namespace Nutrir.Web.Components.Layout;

/// <summary>
/// Scoped state service for the AI assistant panel. Allows TopBar toggle button
/// and AiAssistantPanel to communicate open/close state within the same circuit.
/// </summary>
public class AiPanelState
{
    public bool IsOpen { get; private set; }

    public event Action? OnToggle;

    public void Toggle()
    {
        IsOpen = !IsOpen;
        OnToggle?.Invoke();
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        OnToggle?.Invoke();
    }

    public void SetOpen(bool isOpen)
    {
        if (IsOpen == isOpen) return;
        IsOpen = isOpen;
        OnToggle?.Invoke();
    }
}
