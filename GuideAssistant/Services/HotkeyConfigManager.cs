using GuideAssistant.Data;
using GuideAssistant.Models;
using Serilog;

namespace GuideAssistant.Services;

/// <summary>
/// Single source of truth for hotkey configuration.
/// Centralizes the merge of DB-stored overrides with built-in defaults from
/// <see cref="HotkeyService.KnownActions"/>, provides cached lookups for the
/// settings UI, and notifies consumers when bindings change.
/// </summary>
public class HotkeyConfigManager
{
    private readonly HotkeyRepository _repo;
    private volatile List<HotkeyBinding> _mergedBindings = new();
    private int _defaultProfileId;
    private readonly object _lock = new();

    /// <summary>Fired after bindings are reloaded (save, clear, or explicit refresh).</summary>
    public event Action? BindingsChanged;

    public HotkeyConfigManager(HotkeyRepository repo)
    {
        _repo = repo;
    }

    // ═══════════════════════════════════════════════════════════
    //  Public API
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Get the merged binding list (DB overrides + <see cref="HotkeyService.KnownActions"/> fallback).
    /// Returns a defensive copy so callers cannot mutate the cached list.
    /// </summary>
    public List<HotkeyBinding> GetMergedBindings()
    {
        lock (_lock)
        {
            if (_mergedBindings.Count == 0)
                RefreshBindingsLocked();
            return new List<HotkeyBinding>(_mergedBindings);
        }
    }

    /// <summary>Force reload from DB and notify <see cref="BindingsChanged"/>.</summary>
    public void RefreshAndNotify()
    {
        lock (_lock)
        {
            RefreshBindingsLocked();
        }
        BindingsChanged?.Invoke();
    }

    /// <summary>Force reload from DB without firing the event.</summary>
    public void Refresh()
    {
        lock (_lock)
        {
            RefreshBindingsLocked();
        }
    }

    public void SaveBinding(string actionName, int virtualKey)
    {
        if (string.IsNullOrEmpty(actionName))
        {
            Log.Warning("SaveBinding called with null/empty actionName, ignored");
            return;
        }

        if (_defaultProfileId == 0) EnsureDefaultProfile();
        var def = HotkeyService.KnownActions.FirstOrDefault(a => a.ActionName == actionName);
        if (def.ActionName == null)
        {
            Log.Warning("SaveBinding: unknown actionName '{ActionName}', ignored", actionName);
            return;
        }

        _repo.SaveBinding(_defaultProfileId, actionName, def.DisplayName, virtualKey,
            HotkeyService.VirtualKeyToDisplayName(virtualKey));
        Log.Information("Hotkey saved via ConfigManager: {Action} => VK {Key}", actionName, virtualKey);
        RefreshAndNotify();
    }

    public void ClearBinding(string actionName)
    {
        if (_defaultProfileId == 0) EnsureDefaultProfile();
        _repo.ClearBinding(_defaultProfileId, actionName);
        Log.Information("Hotkey cleared via ConfigManager: {Action}", actionName);
        RefreshAndNotify();
    }

    /// <summary>
    /// Ensure a default profile exists in DB, seeded from
    /// <see cref="HotkeyService.KnownActions"/>. Returns the profile id.
    /// </summary>
    public int EnsureDefaultProfile()
    {
        var profile = _repo.GetDefaultProfile();
        if (profile == null)
        {
            profile = new HotkeyProfile { Name = "默认方案", IsDefault = true };
            profile.Id = _repo.AddProfile(profile);
        }

        // Remove stale bindings from in-memory list and DB
        var staleCount = profile.Bindings.RemoveAll(b => string.IsNullOrEmpty(b.ActionName));
        if (staleCount > 0)
        {
            Log.Warning("HotkeyConfigManager: removed {Count} stale bindings with empty ActionName", staleCount);
            // Also clean DB of empty-action rows so SaveBindings won't trip UNIQUE
            _repo.ClearBinding(profile.Id, "");
        }

        bool needsSave = false;
        foreach (var known in HotkeyService.KnownActions)
        {
            var binding = profile.Bindings.FirstOrDefault(b => b.ActionName == known.ActionName);
            if (binding == null)
            {
                profile.Bindings.Add(new HotkeyBinding
                {
                    ActionName = known.ActionName,
                    ActionDisplay = known.DisplayName,
                    VirtualKey = known.DefaultVk,
                    DisplayText = HotkeyService.VirtualKeyToDisplayName(known.DefaultVk),
                });
                needsSave = true;
            }
            else if (binding.VirtualKey == 0)
            {
                binding.VirtualKey = known.DefaultVk;
                binding.DisplayText = HotkeyService.VirtualKeyToDisplayName(known.DefaultVk);
                binding.ActionDisplay = known.DisplayName;
                needsSave = true;
            }
        }

        if (needsSave)
            _repo.SaveBindings(profile.Id, profile.Bindings);

        _defaultProfileId = profile.Id;
        return profile.Id;
    }

    // ═══════════════════════════════════════════════════════════
    //  Internals
    // ═══════════════════════════════════════════════════════════

    private void RefreshBindingsLocked()
    {
        var profile = _repo.GetDefaultProfile();
        var dbBindings = profile?.Bindings
            ?.Where(b => b.VirtualKey != 0)
            .ToDictionary(b => b.ActionName)
            ?? new Dictionary<string, HotkeyBinding>(StringComparer.Ordinal);

        _mergedBindings = HotkeyService.KnownActions.Select(known =>
        {
            if (dbBindings.TryGetValue(known.ActionName, out var db))
                return db;
            return new HotkeyBinding
            {
                ActionName = known.ActionName,
                ActionDisplay = known.DisplayName,
                VirtualKey = known.DefaultVk,
                DisplayText = HotkeyService.VirtualKeyToDisplayName(known.DefaultVk),
            };
        }).ToList();
    }
}
