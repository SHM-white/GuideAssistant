using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GuideAssistant.Data;
using GuideAssistant.Models;
using GuideAssistant.Services;
using Serilog;

namespace GuideAssistant.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly HotkeyRepository _hotkeyRepo;
    private readonly HotkeyService _hotkeyService;
    private bool _isSubtitleEnabled;
    private bool _isMiniMapEnabled;
    private double _opacity = 0.9;
    private string _opacityLabel = "当前: 90%";
    private string _selectedPage = "hotkeys";
    private bool _isCapturingKey;
    private string _keyCaptureTitle = "";
    private HotkeyRow? _capturingRow;

    public bool IsSubtitleEnabled { get => _isSubtitleEnabled; set => SetProperty(ref _isSubtitleEnabled, value); }
    public bool IsMiniMapEnabled { get => _isMiniMapEnabled; set => SetProperty(ref _isMiniMapEnabled, value); }
    public double Opacity { get => _opacity; set { if (SetProperty(ref _opacity, value)) OpacityLabel = $"当前: {value * 100:F0}%"; } }
    public string OpacityLabel { get => _opacityLabel; set => SetProperty(ref _opacityLabel, value); }
    public string SelectedPage { get => _selectedPage; set => SetProperty(ref _selectedPage, value); }
    public bool IsCapturingKey { get => _isCapturingKey; set => SetProperty(ref _isCapturingKey, value); }
    public string KeyCaptureTitle { get => _keyCaptureTitle; set => SetProperty(ref _keyCaptureTitle, value); }
    public HotkeyRow? CapturingRow { get => _capturingRow; set => SetProperty(ref _capturingRow, value); }

    public ObservableCollection<HotkeyRow> HotkeyRows { get; } = new();

    private static readonly (string ActionName, string DisplayName)[] ActionDefs =
        HotkeyService.KnownActions.Select(a => (a.ActionName, a.DisplayName)).ToArray();

    public IRelayCommand<string> SelectPageCommand { get; }
    public IRelayCommand<HotkeyRow> StartKeyCaptureCommand { get; }
    public IRelayCommand<HotkeyRow> ClearHotkeyCommand { get; }

    public event Action? HotkeysReloaded;

    public SettingsViewModel(HotkeyRepository hotkeyRepo, HotkeyService hotkeyService, bool isSubtitleEnabled, bool isMiniMapEnabled, double opacity)
    {
        _hotkeyRepo = hotkeyRepo;
        _hotkeyService = hotkeyService;
        _isSubtitleEnabled = isSubtitleEnabled;
        _isMiniMapEnabled = isMiniMapEnabled;
        _opacity = opacity;
        _opacityLabel = $"当前: {opacity * 100:F0}%";

        SelectPageCommand = new RelayCommand<string>(p => SelectedPage = p);
        StartKeyCaptureCommand = new RelayCommand<HotkeyRow>(StartKeyCapture);
        ClearHotkeyCommand = new RelayCommand<HotkeyRow>(ClearHotkey);
    }

    public void LoadHotkeys()
    {
        HotkeyRows.Clear();
        var profile = _hotkeyRepo.GetDefaultProfile();
        var dbBindings = profile?.Bindings
            ?.Where(b => b.VirtualKey != 0)
            .ToDictionary(b => b.ActionName);

        dbBindings ??= new();

        foreach (var def in ActionDefs)
        {
            string displayText;
            if (dbBindings.TryGetValue(def.ActionName, out var db))
                displayText = db.DisplayText;
            else
                displayText = HotkeyService.VirtualKeyToDisplayName(
                    HotkeyService.KnownActions.First(a => a.ActionName == def.ActionName).DefaultVk);

            HotkeyRows.Add(new HotkeyRow
            {
                ActionName = def.ActionName,
                DisplayName = def.DisplayName,
                CurrentBinding = displayText
            });
        }
    }

    public void StartKeyCapture(HotkeyRow? row)
    {
        if (row == null) return;
        CapturingRow = row;
        KeyCaptureTitle = $"绑定快捷键 — {row.DisplayName}";
        IsCapturingKey = true;
        HotkeyService.SuppressAll = true;
        _hotkeyService.SuspendSystemHotkeysForCapture();
    }

    public void OnKeyCaptured(int virtualKey)
    {
        if (CapturingRow == null) return;
        HotkeyService.SuppressAll = false;
        _hotkeyService.ResumeSystemHotkeysAfterCapture();
        SaveHotkeyBinding(CapturingRow.ActionName, virtualKey);
        IsCapturingKey = false;
        CapturingRow = null;
        HotkeysReloaded?.Invoke();
        LoadHotkeys();
    }

    public void CancelKeyCapture()
    {
        HotkeyService.SuppressAll = false;
        _hotkeyService.ResumeSystemHotkeysAfterCapture();
        IsCapturingKey = false;
        CapturingRow = null;
    }

    private void ClearHotkey(HotkeyRow? row)
    {
        if (row == null) return;
        var profile = _hotkeyRepo.GetDefaultProfile();
        if (profile == null) return;

        _hotkeyRepo.ClearBinding(profile.Id, row.ActionName);
        HotkeysReloaded?.Invoke();
        LoadHotkeys();
        Log.Information("Hotkey cleared: {Action}", row.ActionName);
    }

    private void SaveHotkeyBinding(string actionName, int virtualKey)
    {
        var profile = _hotkeyRepo.GetDefaultProfile();
        if (profile == null) return;

        var def = ActionDefs.First(a => a.ActionName == actionName);
        _hotkeyRepo.SaveBinding(profile.Id, actionName, def.DisplayName, virtualKey,
            HotkeyService.VirtualKeyToDisplayName(virtualKey));

        Log.Information("Hotkey saved: {Action} => VK {Key}", actionName, virtualKey);
    }
}

public partial class HotkeyRow : ObservableObject
{
    private string _displayName = "";
    private string _actionName = "";
    private string _currentBinding = "未绑定";

    public string DisplayName { get => _displayName; set => SetProperty(ref _displayName, value); }
    public string ActionName { get => _actionName; set => SetProperty(ref _actionName, value); }
    public string CurrentBinding { get => _currentBinding; set => SetProperty(ref _currentBinding, value); }
}
