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
