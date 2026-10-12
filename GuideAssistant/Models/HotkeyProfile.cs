namespace GuideAssistant.Models;

public class HotkeyProfile
{
    public int Id { get; set; }
    public string Name { get; set; } = "默认方案";
    // Legacy GameId refers to the older games table. New per-game hotkeys
    // reference game_configs through a separate column.
    public int? GameId { get; set; }
    public int? GameConfigId { get; set; }
    public bool IsDefault { get; set; }
    public List<HotkeyBinding> Bindings { get; set; } = new();
}

public class HotkeyBinding
{
    public int Id { get; set; }
    public int ProfileId { get; set; }
    public string ActionName { get; set; } = string.Empty;
    public string ActionDisplay { get; set; } = string.Empty;
    public int Modifiers { get; set; }
    public int VirtualKey { get; set; }
    public string DisplayText { get; set; } = string.Empty;
}
