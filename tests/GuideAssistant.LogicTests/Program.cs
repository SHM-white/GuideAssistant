using GuideAssistant.Services;

static void Expect(bool actual, bool expected, string description)
{
    if (actual != expected)
        throw new Exception($"{description}: expected {expected}, got {actual}");
}
var keys = new HotkeyPressTracker();
Expect(keys.OnKeyDown(0x4B), true, "first press");
Expect(keys.OnKeyDown(0x4B), false, "auto-repeat does not retrigger");
keys.OnKeyUp(0x4B);
Expect(keys.OnKeyDown(0x4B), true, "immediate second press triggers");
keys.OnKeyUp(0x4B);
Expect(keys.OnKeyDown(0x11), true, "modifier down");
Expect(keys.OnKeyDown(0x4B), true, "chord down");
Expect(keys.OnKeyDown(0x4B), false, "held chord repeat suppressed");
keys.OnKeyUp(0x11);
Expect(keys.OnKeyDown(0x4B), false, "changing modifier cannot retrigger held key");
keys.OnKeyUp(0x4B);
Expect(keys.OnKeyDown(0x4B), true, "new physical press triggers");
keys.Reset();
Expect(keys.OnKeyDown(0x4B), true, "restart clears pressed state");
Console.WriteLine("Hotkey press tracking tests passed.");

var defaults = new[]
{
    new GuideAssistant.Models.HotkeyBinding { ActionName = "play", ActionDisplay = "播放", VirtualKey = 0x4B },
    new GuideAssistant.Models.HotkeyBinding { ActionName = "pause", ActionDisplay = "暂停", VirtualKey = 0x50 },
    new GuideAssistant.Models.HotkeyBinding { ActionName = "volume", ActionDisplay = "音量", VirtualKey = 0x56 }
};
var overrides = new[]
{
    new GuideAssistant.Models.HotkeyBinding { ActionName = "play", VirtualKey = 0x50, Modifiers = 1 },
    new GuideAssistant.Models.HotkeyBinding { ActionName = "volume", VirtualKey = 0 }
};
var effective = HotkeyProfileMerger.Merge(defaults, overrides);
Expect(effective[0].VirtualKey == 0x50 && effective[0].Modifiers == 1, true,
    "game override takes precedence");
Expect(effective[1].VirtualKey == 0x50, true, "missing override inherits global");
Expect(effective[2].VirtualKey == 0, true, "explicit unbound remains disabled");
Expect(defaults[0].VirtualKey == 0x4B, true, "default bindings are never modified");
Expect(HotkeyProfileMerger.Merge(defaults, null)[0].VirtualKey == 0x4B, true,
    "switching away from game restores original key");
var duplicateOverrides = new[]
{
    new GuideAssistant.Models.HotkeyBinding { ActionName = "play", VirtualKey = 0x31 },
    new GuideAssistant.Models.HotkeyBinding { ActionName = "pause", VirtualKey = 0x31 }
};
var duplicates = HotkeyProfileMerger.Merge(defaults, duplicateOverrides);
Expect(duplicates[0].VirtualKey == duplicates[1].VirtualKey, true,
    "multiple actions may deliberately share a hotkey");
Console.WriteLine("Game-specific hotkey profile tests passed.");
