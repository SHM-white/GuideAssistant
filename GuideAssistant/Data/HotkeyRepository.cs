using System.Collections.Concurrent;
using Dapper;
using GuideAssistant.Models;
using Serilog;

namespace GuideAssistant.Data;

public class HotkeyRepository
{
    private readonly Database _db;
    private readonly ConcurrentDictionary<int, HotkeyProfile> _profileCache = new();

    public HotkeyRepository(Database db) => _db = db;

    public List<HotkeyProfile> GetAllProfiles()
    {
        using var conn = _db.CreateConnection();
        var profiles = conn.Query<HotkeyProfile>("SELECT * FROM hotkey_profiles ORDER BY is_default DESC, name").ToList();
        foreach (var p in profiles)
        {
            p.Bindings = conn.Query<HotkeyBinding>("SELECT * FROM hotkey_bindings WHERE profile_id=@id AND action_name != ''", new { id = p.Id }).ToList();
        }
        return profiles;
    }

    public HotkeyProfile? GetDefaultProfile()
    {
        // Try cache first (key = 0 is the sentinel for the default profile)
        if (_profileCache.TryGetValue(0, out var cached))
            return cached;

        using var conn = _db.CreateConnection();
        var profile = conn.QueryFirstOrDefault<HotkeyProfile>("SELECT * FROM hotkey_profiles WHERE is_default=1");
        if (profile != null)
        {
            profile.Bindings = conn.Query<HotkeyBinding>("SELECT * FROM hotkey_bindings WHERE profile_id=@id AND action_name != ''", new { id = profile.Id }).ToList();
            _profileCache[0] = profile;
        }
        return profile;
    }

    public int AddProfile(HotkeyProfile p)
    {
        using var conn = _db.CreateConnection();
        var id = conn.ExecuteScalar<int>(@"
            INSERT INTO hotkey_profiles (name, game_id, is_default) VALUES (@Name, @GameId, @IsDefault);
            SELECT last_insert_rowid();", p);
        InvalidateCache();
        return id;
    }

    public void SaveBindings(int profileId, List<HotkeyBinding> bindings)
    {
        using var conn = _db.CreateConnection();
        using var tx = conn.BeginTransaction();
        conn.Execute("DELETE FROM hotkey_bindings WHERE profile_id=@id", new { id = profileId }, tx);
        var seen = new HashSet<string>();
        int saved = 0, skipped = 0;
        foreach (var b in bindings)
        {
            if (string.IsNullOrEmpty(b.ActionName)) { skipped++; continue; }
            if (!seen.Add(b.ActionName)) { skipped++; continue; }
            b.ProfileId = profileId;
            conn.Execute(@"
                INSERT OR REPLACE INTO hotkey_bindings (profile_id, action_name, action_display, modifiers, virtual_key, display_text)
                VALUES (@ProfileId, @ActionName, @ActionDisplay, @Modifiers, @VirtualKey, @DisplayText)", b, tx);
            saved++;
        }
        tx.Commit();
        InvalidateCache();
        Log.Information("SaveBindings: profile={ProfileId}, total={Total}, saved={Saved}, skipped={Skipped}",
            profileId, bindings.Count, saved, skipped);
    }

    public void SaveBinding(int profileId, string actionName, string actionDisplay, int virtualKey, string displayText)
    {
        using var conn = _db.CreateConnection();
        using var tx = conn.BeginTransaction();
        conn.Execute("DELETE FROM hotkey_bindings WHERE profile_id=@pid AND action_name=@name",
            new { pid = profileId, name = actionName }, tx);
        conn.Execute(@"INSERT OR REPLACE INTO hotkey_bindings (profile_id, action_name, action_display, modifiers, virtual_key, display_text)
            VALUES (@pid, @name, @display, 0, @vk, @text)",
            new { pid = profileId, name = actionName, display = actionDisplay, vk = virtualKey, text = displayText }, tx);
        tx.Commit();
        InvalidateCache();
        Log.Information("SaveBinding: profile={ProfileId}, action={Action}, vk={VK}", profileId, actionName, virtualKey);
    }

    public void ClearBinding(int profileId, string actionName)
    {
        using var conn = _db.CreateConnection();
        conn.Execute("DELETE FROM hotkey_bindings WHERE profile_id=@pid AND action_name=@name",
            new { pid = profileId, name = actionName });
        InvalidateCache();
        Log.Information("ClearBinding: profile={ProfileId}, action={Action}", profileId, actionName);
    }

    public void DeleteProfile(int id)
    {
        using var conn = _db.CreateConnection();
        conn.Execute("DELETE FROM hotkey_bindings WHERE profile_id=@id", new { id });
        conn.Execute("DELETE FROM hotkey_profiles WHERE id=@id", new { id });
        InvalidateCache();
    }

    private void InvalidateCache()
    {
        _profileCache.Clear();
    }
}

public class WindowStateRepository
{
    private readonly Database _db;
    public WindowStateRepository(Database db) => _db = db;

    public WindowState? Get(string windowName)
    {
        using var conn = _db.CreateConnection();
        return conn.QueryFirstOrDefault<WindowState>("SELECT * FROM window_states WHERE window_name=@name", new { name = windowName });
    }

    public void Save(WindowState state)
    {
        using var conn = _db.CreateConnection();
        conn.Execute(@"
            UPDATE window_states SET x=@X, y=@Y, width=@Width, height=@Height, opacity=@Opacity, is_always_on_top=@IsAlwaysOnTop
            WHERE window_name=@WindowName", state);
    }
}
