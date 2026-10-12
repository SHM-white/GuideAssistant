using GuideAssistant.Data;
using GuideAssistant.Models;
using Serilog;

namespace GuideAssistant.Services;

/// <summary>
/// Global hotkeys are the fallback. Per-game profiles store only overrides,
/// including explicit unbound keys. Switching the active foreground game
/// never mutates the configuration currently being edited in Settings.
/// </summary>
public sealed class HotkeyConfigManager
{
    private readonly HotkeyRepository _repo;
    private readonly GameRepository _games;
    private readonly object _gate = new();
    private List<HotkeyBinding> _activeBindings = new();
    private int _defaultProfileId;
    private int? _activeGameConfigId;

    public event Action? BindingsChanged;

    public HotkeyConfigManager(HotkeyRepository repo, GameRepository games)
    {
        _repo = repo;
        _games = games;
    }

    public int? ActiveGameConfigId
    {
        get { lock (_gate) return _activeGameConfigId; }
    }

    public List<HotkeyBinding> GetMergedBindings()
    {
        lock (_gate)
        {
            if (_activeBindings.Count == 0) RebuildLocked();
            return HotkeyProfileMerger.Merge(_activeBindings, null);
        }
    }

    public List<HotkeyBinding> GetBindingsForScope(int? gameConfigId)
    {
        lock (_gate)
            return BuildScopeBindingsLocked(gameConfigId);
    }

    /// <summary>Only the foreground game gets its overrides; null restores global defaults.</summary>
    public void SetActiveGame(int? gameConfigId)
    {
        bool changed;
        lock (_gate)
        {
            changed = _activeGameConfigId != gameConfigId;
            if (changed)
            {
                _activeGameConfigId = gameConfigId;
                RebuildLocked();
            }
        }
        if (changed)
        {
            Log.Information("Switched active hotkey profile to game config {GameId}", gameConfigId);
            BindingsChanged?.Invoke();
        }
    }

    public void RefreshAndNotify()
    {
        lock (_gate) RebuildLocked();
        BindingsChanged?.Invoke();
    }

    public void Refresh()
    {
        lock (_gate) RebuildLocked();
    }

    /// <summary>Legacy API: edits the global profile, never the foreground game.</summary>
    public void SaveBinding(string actionName, int virtualKey, int modifiers = 0)
        => SaveBindingForScope(null, actionName, virtualKey, modifiers);

    public void ClearBinding(string actionName) => ClearBindingForScope(null, actionName);

    public void SaveBindingForScope(int? gameConfigId, string actionName, int virtualKey, int modifiers = 0)
    {
        var def = HotkeyService.KnownActions.FirstOrDefault(x => x.ActionName == actionName);
        if (def.ActionName == null)
        {
            Log.Warning("Unknown hotkey action ignored: {Action}", actionName);
            return;
        }

        int profileId;
        if (gameConfigId is int id)
        {
            var game = _games.GetAll().FirstOrDefault(g => g.Id == id);
            if (game == null)
                throw new InvalidOperationException("The selected game no longer exists.");
            profileId = _repo.GetOrCreateGameProfile(id, game.GameName).Id;
        }
        else
        {
            profileId = EnsureDefaultProfile();
        }

        modifiers &= HotkeyService.ModifierMask;
        _repo.SaveBinding(profileId, actionName, def.DisplayName, virtualKey,
            HotkeyService.FormatBinding(virtualKey, modifiers), modifiers);
        RefreshAndNotify();
    }

    public void ClearBindingForScope(int? gameConfigId, string actionName)
        => SaveBindingForScope(gameConfigId, actionName, 0);

    public void ResetGameProfile(int gameConfigId)
    {
        _repo.DeleteGameProfile(gameConfigId);
        RefreshAndNotify();
    }

    public int EnsureDefaultProfile()
    {
        lock (_gate)
        {
            var profile = _repo.GetDefaultProfile();
            if (profile == null)
            {
                profile = new HotkeyProfile { Name = "默认方案", IsDefault = true };
                profile.Id = _repo.AddProfile(profile);
            }
            // Default bindings are seeded once; never replace an explicit VK=0.
            bool needsSave = false;
            foreach (var known in HotkeyService.KnownActions)
            {
                if (profile.Bindings.Any(x => x.ActionName == known.ActionName)) continue;
                profile.Bindings.Add(new HotkeyBinding
                {
                    ActionName = known.ActionName,
                    ActionDisplay = known.DisplayName,
                    VirtualKey = known.DefaultVk,
                    DisplayText = HotkeyService.VirtualKeyToDisplayName(known.DefaultVk)
                });
                needsSave = true;
            }
            if (needsSave) _repo.SaveBindings(profile.Id, profile.Bindings);
            _defaultProfileId = profile.Id;
            return profile.Id;
        }
    }

    private void RebuildLocked() => _activeBindings = BuildScopeBindingsLocked(_activeGameConfigId);

    private List<HotkeyBinding> BuildScopeBindingsLocked(int? gameConfigId)
    {
        var profile = _repo.GetDefaultProfile();
        var dbBindings = profile?.Bindings
            .Where(x => !string.IsNullOrWhiteSpace(x.ActionName))
            .ToDictionary(x => x.ActionName, StringComparer.Ordinal)
            ?? new Dictionary<string, HotkeyBinding>(StringComparer.Ordinal);
        var global = HotkeyService.KnownActions.Select(known =>
            dbBindings.TryGetValue(known.ActionName, out var b)
                ? b
                : new HotkeyBinding
                {
                    ActionName = known.ActionName,
                    ActionDisplay = known.DisplayName,
                    VirtualKey = known.DefaultVk,
                    DisplayText = HotkeyService.VirtualKeyToDisplayName(known.DefaultVk)
                }).ToList();

        var overrides = gameConfigId is int id ? _repo.GetGameProfile(id)?.Bindings : null;
        var merged = HotkeyProfileMerger.Merge(global, overrides);
        foreach (var b in merged)
            b.DisplayText = HotkeyService.FormatBinding(b.VirtualKey, b.Modifiers);
        return merged;
    }
}
