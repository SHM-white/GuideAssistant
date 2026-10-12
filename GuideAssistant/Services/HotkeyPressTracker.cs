namespace GuideAssistant.Services;

/// <summary>
/// Filters keyboard auto-repeat by physical key lifetime. A new key-down after
/// key-up must fire even when it follows immediately; modifier changes while
/// the key is held do not generate another trigger.
/// </summary>
public sealed class HotkeyPressTracker
{
    private readonly HashSet<int> _down = new();

    public bool OnKeyDown(int virtualKey) => _down.Add(virtualKey);
    public void OnKeyUp(int virtualKey) => _down.Remove(virtualKey);
    public void Reset() => _down.Clear();
}
