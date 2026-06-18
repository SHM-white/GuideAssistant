namespace GuideAssistant.Models;

public class HotkeyProfile
{
    public int Id { get; set; }
    public string Name { get; set; } = "默认方案";
    public int? GameId { get; set; }
    public bool IsDefault { get; set; }
    public List<HotkeyBinding> Bindings { get; set; } = new();
}

public class HotkeyBinding
{
    public int Id { get; set; }
    public int ProfileId { get; set; }
    public string ActionName { get; set; } = string.Empty;
    public string ActionDisplay { get; set; } = string.Empty;
    public uint Modifiers { get; set; }
    public uint VirtualKey { get; set; }
    public string DisplayText { get; set; } = string.Empty;
}
