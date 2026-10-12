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

static void DirectionTest(string text, bool filter, double? expectedAngle, DirectionReference? expectedReference, string description)
{
    var match = DirectionWords.TryResolve(text, filter);
    Expect(match.HasValue, expectedAngle.HasValue, description + " exists");
    if (match is { } value)
    {
        Expect(Math.Abs(value.Angle - expectedAngle!.Value) < 0.001, true, description + " angle");
        Expect(value.Reference == expectedReference, true, description + " reference");
    }
}

DirectionTest("敌人在东北方向", false, 45, DirectionReference.Compass, "compass northeast");
DirectionTest("往西边走", false, 270, DirectionReference.Compass, "compass west");
DirectionTest("小地图左上角有入口", true, 315, DirectionReference.Map, "small map corner");
DirectionTest("地图的右侧有标记", true, 90, DirectionReference.Map, "explicit map right");
DirectionTest("在地图上方", true, 0, DirectionReference.Map, "map top");
DirectionTest("去北方", true, null, null, "filter requires map mention");
DirectionTest("大地图西北方向", true, null, null, "big map suppressed by filter");
DirectionTest("大地图西北方向", false, 315, DirectionReference.Compass, "big map override");
DirectionTest("敌人在我的正前方", false, null, null, "forward is not north");
DirectionTest("小地图上敌人在我的左前方", true, null, null, "camera-relative left forward not northwest");
DirectionTest("地图在面前", false, null, null, "facing is not north");
DirectionTest("往前走，随后向右", false, null, null, "movement is not a map bearing");
DirectionTest("地图右上角", false, 45, DirectionReference.Map, "disabled filter still resolves map");
DirectionTest("地图右上角", true, 45, DirectionReference.Map, "map corner prioritized over shorter right");
DirectionTest("东西都可以", false, null, null, "single cardinal characters do not match ordinary words");
DirectionTest("北", false, 0, DirectionReference.Compass, "single explicit compass word");
DirectionTest("", false, null, null, "empty caption");
Expect(DirectionService.ParseDirection("正前方") == null, true, "overlay refuses camera-relative headings");
Expect(DirectionService.ParseDirection("地图左下角")?.angle == 225, true, "overlay uses shared map resolver");
Console.WriteLine("Direction reference and minimap filtering tests passed.");
