using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GuideAssistant.Data;
using GuideAssistant.Services;
using Serilog;

namespace GuideAssistant.ViewModels;

public partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly HotkeyConfigManager _hotkeyConfigManager;
    private readonly GameRepository _games;
    private bool _isSubtitleEnabled;
    private bool _isMiniMapEnabled;
    private bool _isMinimapFilterEnabled = true;
    private double _opacity = 0.9;
    private string _opacityLabel = "当前: 90%";
    private string _selectedPage = "hotkeys";
    private bool _isCapturingKey;
    private string _keyCaptureTitle = "";
    private HotkeyRow? _capturingRow;
    private int? _editingGameConfigId;
    private string _scopeDescription = "";

    public bool IsSubtitleEnabled { get => _isSubtitleEnabled; set => SetProperty(ref _isSubtitleEnabled, value); }
    public bool IsMiniMapEnabled { get => _isMiniMapEnabled; set => SetProperty(ref _isMiniMapEnabled, value); }
    public bool IsMinimapFilterEnabled { get => _isMinimapFilterEnabled; set => SetProperty(ref _isMinimapFilterEnabled, value); }
    public double Opacity { get => _opacity; set { if (SetProperty(ref _opacity, value)) OpacityLabel = $"当前: {value * 100:F0}%"; } }
    public string OpacityLabel { get => _opacityLabel; set => SetProperty(ref _opacityLabel, value); }
    public string SelectedPage { get => _selectedPage; set => SetProperty(ref _selectedPage, value); }
    public bool IsCapturingKey { get => _isCapturingKey; set => SetProperty(ref _isCapturingKey, value); }
    public string KeyCaptureTitle { get => _keyCaptureTitle; set => SetProperty(ref _keyCaptureTitle, value); }
    public HotkeyRow? CapturingRow { get => _capturingRow; set => SetProperty(ref _capturingRow, value); }
    public int? EditingGameConfigId { get => _editingGameConfigId; private set => SetProperty(ref _editingGameConfigId, value); }
    public string ScopeDescription { get => _scopeDescription; private set => SetProperty(ref _scopeDescription, value); }

    public ObservableCollection<HotkeyRow> HotkeyRows { get; } = new();
    public ObservableCollection<HotkeyScopeOption> HotkeyScopes { get; } = new();

    private static readonly (string ActionName, string DisplayName)[] ActionDefs =
        HotkeyService.KnownActions.Select(a => (a.ActionName, a.DisplayName)).ToArray();

    public IRelayCommand<string> SelectPageCommand { get; }
    public IRelayCommand<HotkeyRow> StartKeyCaptureCommand { get; }
    public IRelayCommand<HotkeyRow> ClearHotkeyCommand { get; }

    public SettingsViewModel(
        HotkeyConfigManager hotkeyConfigManager, GameRepository games,
        bool isSubtitleEnabled, bool isMiniMapEnabled,
        bool isMinimapFilterEnabled, double opacity)
    {
        _hotkeyConfigManager = hotkeyConfigManager;
        _games = games;
        _isSubtitleEnabled = isSubtitleEnabled;
        _isMiniMapEnabled = isMiniMapEnabled;
        _isMinimapFilterEnabled = isMinimapFilterEnabled;
        _opacity = opacity;
        _opacityLabel = $"当前: {opacity * 100:F0}%";

        SelectPageCommand = new RelayCommand<string>(p => { if (p != null) SelectedPage = p; });
        StartKeyCaptureCommand = new RelayCommand<HotkeyRow>(StartKeyCapture);
        ClearHotkeyCommand = new RelayCommand<HotkeyRow>(ClearHotkey);
        _hotkeyConfigManager.BindingsChanged += LoadHotkeys;
        RefreshScopes();
    }

    public void RefreshScopes()
    {
        var current = EditingGameConfigId;
        HotkeyScopes.Clear();
        HotkeyScopes.Add(new HotkeyScopeOption(null, "全局默认方案"));
        foreach (var game in _games.GetAll())
            HotkeyScopes.Add(new HotkeyScopeOption(game.Id, game.GameName));
        if (current is int id && !HotkeyScopes.Any(x => x.GameConfigId == id))
            EditingGameConfigId = null;
        LoadHotkeys();
    }

    public void SelectScope(int? gameConfigId)
    {
        if (gameConfigId is not null && !HotkeyScopes.Any(x => x.GameConfigId == gameConfigId))
            gameConfigId = null;
        EditingGameConfigId = gameConfigId;
        LoadHotkeys();
    }

    public void ResetSelectedGameProfile()
    {
        if (EditingGameConfigId is int id)
            _hotkeyConfigManager.ResetGameProfile(id);
    }

    public void LoadHotkeys()
    {
        var merged = _hotkeyConfigManager.GetBindingsForScope(EditingGameConfigId);
        var lookup = merged.ToDictionary(b => b.ActionName);
        for (int i = 0; i < ActionDefs.Length; i++)
        {
            var def = ActionDefs[i];
            var displayText = lookup.TryGetValue(def.ActionName, out var b)
                ? HotkeyService.FormatBinding(b.VirtualKey, b.Modifiers)
                : "未绑定";
            if (i < HotkeyRows.Count && HotkeyRows[i].ActionName == def.ActionName)
                HotkeyRows[i].CurrentBinding = displayText;
            else
            {
                var row = new HotkeyRow
                {
                    ActionName = def.ActionName,
                    DisplayName = def.DisplayName,
                    CurrentBinding = displayText
                };
                if (i < HotkeyRows.Count) HotkeyRows[i] = row;
                else HotkeyRows.Add(row);
            }
        }
        while (HotkeyRows.Count > ActionDefs.Length)
            HotkeyRows.RemoveAt(HotkeyRows.Count - 1);

        int shared = merged.Where(b => b.VirtualKey != 0)
            .GroupBy(b => (b.VirtualKey, b.Modifiers))
            .Count(g => g.Count() > 1);
        var baseHint = EditingGameConfigId is null
            ? "编辑全局默认方案；没有专属配置的游戏继承此方案。"
            : "编辑当前游戏专属覆盖项；未修改的动作自动继承全局默认。";
        ScopeDescription = shared == 0
            ? baseHint + " 允许同键绑定多个动作。"
            : baseHint + $" 目前有 {shared} 组快捷键对应多个动作（允许）。";
    }

    public void StartKeyCapture(HotkeyRow? row)
    {
        if (row == null) return;
        CapturingRow = row;
        KeyCaptureTitle = $"绑定快捷键 — {row.DisplayName}";
        IsCapturingKey = true;
        HotkeyService.SuppressAll = true;
    }

    public void OnKeyCaptured(int virtualKey, int modifiers = 0)
    {
        if (CapturingRow == null) return;
        if (string.IsNullOrEmpty(CapturingRow.ActionName))
        {
            Log.Warning("OnKeyCaptured: missing ActionName");
            CancelKeyCapture();
            return;
        }
        var row = CapturingRow;
        CancelKeyCapture();
        _hotkeyConfigManager.SaveBindingForScope(EditingGameConfigId, row.ActionName, virtualKey, modifiers);
    }

    public void CancelKeyCapture()
    {
        HotkeyService.SuppressAll = false;
        IsCapturingKey = false;
        CapturingRow = null;
    }

    private void ClearHotkey(HotkeyRow? row)
    {
        if (row != null)
            _hotkeyConfigManager.ClearBindingForScope(EditingGameConfigId, row.ActionName);
    }

    public void Dispose()
    {
        if (IsCapturingKey) CancelKeyCapture();
        _hotkeyConfigManager.BindingsChanged -= LoadHotkeys;
    }
}

public sealed record HotkeyScopeOption(int? GameConfigId, string Name);

public partial class HotkeyRow : ObservableObject
{
    private string _displayName = "";
    private string _actionName = "";
    private string _currentBinding = "未绑定";

    public string DisplayName { get => _displayName; set => SetProperty(ref _displayName, value); }
    public string ActionName { get => _actionName; set => SetProperty(ref _actionName, value); }
    public string CurrentBinding { get => _currentBinding; set => SetProperty(ref _currentBinding, value); }
}
