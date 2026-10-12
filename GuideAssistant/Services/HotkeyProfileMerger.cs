using GuideAssistant.Models;

namespace GuideAssistant.Services;

/// <summary>
/// Pure, testable default+override merger. A missing override inherits the
/// default; VK=0 is an explicit disabled binding, not a missing value.
/// Deliberately does not enforce uniqueness across different action names.
/// </summary>
public static class HotkeyProfileMerger
{
    public static List<HotkeyBinding> Merge(
        IEnumerable<HotkeyBinding> defaults,
        IEnumerable<HotkeyBinding>? overrides)
    {
        var byAction = (overrides ?? Enumerable.Empty<HotkeyBinding>())
            .Where(b => !string.IsNullOrWhiteSpace(b.ActionName))
            .GroupBy(b => b.ActionName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
        return defaults.Select(b =>
        {
            var current = byAction.TryGetValue(b.ActionName, out var overrideBinding)
                ? overrideBinding : b;
            return new HotkeyBinding
            {
                Id = current.Id,
                ProfileId = current.ProfileId,
                ActionName = b.ActionName,
                ActionDisplay = b.ActionDisplay,
                VirtualKey = current.VirtualKey,
                Modifiers = current.Modifiers,
                DisplayText = current.DisplayText
            };
        }).ToList();
    }
}
