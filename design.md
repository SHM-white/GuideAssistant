# GameVideoTool - 小地图 & 语音字幕重构设计文档

## 目标

1. 用 WPF 替换现有 WinUI3 覆盖窗口（`MiniMapOverlay`、`SubtitleOverlay`），实现真正的像素级透明
2. 添加 Windows 原生语音识别接口，当视频没有 CC 字幕时自动切换
3. 音频采集仅限 WebView2 内部视频，避免混入游戏音频

---

## 一、架构总览

```
GameVideoTool.slnx
├── GuideAssistant/                  (现有 WinUI3 项目)
│   ├── 引用 → System.Speech (NuGet)
│   ├── 引用 → GuideAssistant.Overlays (项目引用)
│   │   ※ 通过 <ProjectReference> 引用，非 dll 引用
│   ├── Services/
│   │   ├── SubtitleService.cs       [修改] ISubtitleProvider 接口化
│   │   ├── SpeechRecognitionService.cs [新增] 语音识别服务
│   │   ├── AudioCaptureService.cs   [新增] 音频捕获三层降级
│   │   └── WebView2AudioBridge.cs   [新增] JS↔C# 音频数据桥
│   ├── Guides/
│   │   └── audio-capture.js        [新增] 注入 WebView2 的音频捕获脚本
│   ├── MainWindow.xaml.cs          [修改] 对接 WPF 覆盖窗口
│   ├── App.xaml.cs                 [修改] DI 注册新服务
│   ├── GlobalUsings.cs              [修改] 移除 Shapes 冲突的 global using
│   └── GuideAssistant.csproj       [修改] 加 NuGet + 项目引用
│
└── GuideAssistant.Overlays/         (新建 WPF 类库)
    ├── GuideAssistant.Overlays.csproj
    ├── WpfThreadHost.cs             (STA 线程管理 + Dispatcher)
    ├── Overlays/
    │   ├── WpfOverlayBase.cs        (透明窗口基类)
    │   ├── WpfMiniMapOverlay.xaml   (小地图 XAML)
    │   ├── WpfMiniMapOverlay.xaml.cs(小地图 code-behind)
    │   ├── WpfSubtitleOverlay.xaml  (字幕窗口 XAML)
    │   └── WpfSubtitleOverlay.xaml.cs(字幕窗口 code-behind)
    ├── IOverlayController.cs        (控制接口)
    └── Win32/                       (WPF 侧 P/Invoke)
        └── NativeMethods.cs
```

**跨线程通信模型：**
```
WinUI3 UI 线程                        WPF 后台 STA 线程
──────────────                        ──────────────────
DispatcherQueue                        WpfThreadHost.Dispatcher
    │                                       │
    │  WpfThreadHost.ShowMiniMap()          │
    ├──────────────────────────────────────►│
    │  内部调用 _dispatcher.InvokeAsync()    │
    │                                       ├─ new WpfMiniMapOverlay()
    │                                       └─ overlay.Show()
    │                                       │
    │  WpfThreadHost.HideMiniMap()          │
    ├──────────────────────────────────────►│
    │                                       └─ overlay.Hide()
```

---

## 二、GuideAssistant.Overlays 项目详情

### 2.1 项目文件 (`GuideAssistant.Overlays.csproj`)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
    <UseWPF>true</UseWPF>
    <RootNamespace>GuideAssistant.Overlays</RootNamespace>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <Platforms>x86;x64;ARM64;AnyCPU</Platforms>
  </PropertyGroup>
</Project>
```

**重要：** 必须使用与主项目**完全相同的** `TargetFramework` (`net8.0-windows10.0.19041.0`)，否则 WPF 程序集版本不兼容。

### 2.2 `IOverlayController.cs`

```csharp
namespace GuideAssistant.Overlays;

public interface IOverlayController
{
    void ShowMiniMap();
    void HideMiniMap();
    void ShowDirection(string directionText);
    void ShowSubtitle();
    void HideSubtitle();
    void UpdateSubtitle(string text);
    bool IsMiniMapVisible { get; }
    bool IsSubtitleVisible { get; }
    event Action? MiniMapClosed;
    event Action? SubtitleClosed;
}
```

这个接口放在 `GuideAssistant.Overlays` 项目中（WPF 类库），WinUI3 主项目通过引用 WPF 类库直接使用该接口。

### 2.3 `WpfThreadHost.cs`

**职责：** 在独立 STA 线程中启动 WPF `Application`，暴露同步/异步方法供 WinUI3 调用。

```csharp
public class WpfThreadHost : IDisposable, IOverlayController
{
    private Thread? _wpfThread;
    private Dispatcher? _dispatcher;
    private WpfMiniMapOverlay? _miniMap;
    private WpfSubtitleOverlay? _subtitle;

    public void Start()
    {
        var ready = new ManualResetEventSlim(false);
        _wpfThread = new Thread(() =>
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            System.Windows.Threading.Dispatcher.Run();
        });
        _wpfThread.SetApartmentState(ApartmentState.STA);
        _wpfThread.IsBackground = true;
        _wpfThread.Start();
        ready.Wait();
    }

    public void Stop()
    {
        _dispatcher?.InvokeShutdown();
        _wpfThread?.Join(3000);
    }

    // IOverlayController 实现：
    // 每个方法内部使用 _dispatcher.InvokeAsync(() => { ... })
    // 确保 WPF 窗口创建/操作在 WPF 线程上执行
}
```

**线程安全保证：**
- 所有操作 `_dispatcher.InvokeAsync()` 发送到 WPF 线程
- WinUI3 端调用不会阻塞（使用 `InvokeAsync` 而非 `Invoke`）
- 关闭/Dispose 时 `InvokeShutdown()` 等待线程结束

### 2.4 `WpfOverlayBase.cs`

```csharp
public class WpfOverlayBase : System.Windows.Window
{
    protected WpfOverlayBase()
    {
        // 核心透明设置
        this.AllowsTransparency = true;
        this.WindowStyle = System.Windows.WindowStyle.None;
        this.Background = System.Windows.Media.Brushes.Transparent;
        this.Topmost = true;
        this.ShowInTaskbar = false;
        this.ResizeMode = System.Windows.ResizeMode.NoResize;

        // 加载后设置 Win32 样式
        this.Loaded += (s, e) => ApplyOverlayStyles();
    }

    private void ApplyOverlayStyles()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        var exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        //        点击穿透      分层窗口     工具窗口(不在Alt+Tab中)   不激活
        exStyle |= WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, exStyle);
    }
}
```

**关键区别 vs WinUI3：**
- WPF 的 `AllowsTransparency="True"` + `Background="Transparent"` = 真正的像素级透明
- WinUI3 只能靠 `DesktopAcrylicBackdrop` 模拟（非真正透明）
- 代码中的点击穿透样式与现有 `Win32Helper.SetClickThrough` 逻辑一致

### 2.5 `WpfMiniMapOverlay.xaml` + `.cs`

**XAML (`WpfMiniMapOverlay.xaml`)：**
```xml
<local:WpfOverlayBase x:Class="GuideAssistant.Overlays.WpfMiniMapOverlay"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:local="clr-namespace:GuideAssistant.Overlays"
    Width="200" Height="200">
    <Grid>
        <!-- 圆形罗盘背景 - 半透明深色 -->
        <Ellipse Width="180" Height="180"
                 Fill="#22000000" Stroke="#44888888" StrokeThickness="1" />
        <!-- 十字线 -->
        <Line X1="100" Y1="14" X2="100" Y2="186" Stroke="#44888888" StrokeThickness="1" />
        <Line X1="14" Y1="100" X2="186" Y2="100" Stroke="#44888888" StrokeThickness="1" />
        <!-- 方位标签 -->
        <TextBlock Text="N" FontSize="11" Foreground="#88FFFFFF"
                   HorizontalAlignment="Center" VerticalAlignment="Top" Margin="0,6,0,0" />
        <TextBlock Text="S" FontSize="11" Foreground="#88FFFFFF"
                   HorizontalAlignment="Center" VerticalAlignment="Bottom" Margin="0,0,0,6" />
        <TextBlock Text="W" FontSize="11" Foreground="#88FFFFFF"
                   HorizontalAlignment="Left" VerticalAlignment="Center" Margin="6,0,0,0" />
        <TextBlock Text="E" FontSize="11" Foreground="#88FFFFFF"
                   HorizontalAlignment="Right" VerticalAlignment="Center" Margin="0,0,6,0" />
        <!-- 箭头画布 -->
        <Canvas x:Name="ArrowCanvas" Width="180" Height="180"
                Margin="10,10,10,10" />
    </Grid>
</local:WpfOverlayBase>
```

**Code-behind (`WpfMiniMapOverlay.xaml.cs`)：**
- 构造：设置窗口位置（`screenWidth/100, screenWidth/100`，与现有一致）
- `ShowDirection(string directionText)` → `DirectionService.ParseDirection()` → 绘制箭头
- 箭头：`System.Windows.Shapes.Polygon`，`RotateTransform` 旋转
- 自动消失：4秒后 `DispatcherTimer` 移除
- **关键区别 vs WinUI3 版**：WPF 使用 `System.Windows.Threading.DispatcherTimer`（非 `System.Timers.Timer`），WPF 的 `Shapes` / `Brushes` 而非 WinUI3 的 `Microsoft.UI.Xaml.Shapes`

### 2.6 `WpfSubtitleOverlay.xaml` + `.cs`

**XAML (`WpfSubtitleOverlay.xaml`)：**
```xml
<local:WpfOverlayBase x:Class="GuideAssistant.Overlays.WpfSubtitleOverlay"
    Width="800" Height="100">
    <Border CornerRadius="12" Padding="20,12"
            Background="Transparent" MaxWidth="800"
            HorizontalAlignment="Center" VerticalAlignment="Center">
        <TextBlock x:Name="SubtitleText" Text="等待字幕..."
                   FontSize="22" Foreground="White"
                   TextWrapping="Wrap" TextAlignment="Center"
                   LineHeight="36" />
    </Border>
</local:WpfOverlayBase>
```

**Code-behind (`WpfSubtitleOverlay.xaml.cs`)：**
- 构造：窗口定位 `screenWidth/2 - 400, screenHeight * 0.9`（与现有一致）
- `ShowText(string text)` → 解析方向词 → 黄色高亮 `Run` 元素
- 方向词列表与现有 `SubtitleOverlay.xaml.cs` 的 `HighlightWords` 完全一致
- WPF 使用 `System.Windows.Documents.Run`、`System.Windows.Media.Brushes.Yellow`

### 2.7 `Win32/NativeMethods.cs`

与现有 `Win32Helper.cs` 中 P/Invoke 定义重复但独立，提供：
```csharp
internal static class NativeMethods
{
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_LAYERED = 0x00080000;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_NOACTIVATE = 0x08000000;
    // GetWindowLong, SetWindowLong ...
}
```

---

## 三、语音识别 + 音频捕获

### 3.1 NuGet 依赖新增

在 `GuideAssistant.csproj` 中添加：
```xml
<PackageReference Include="System.Speech" Version="10.0.0" />
```

### 3.2 `Services/AudioCaptureService.cs`

**设计：** 三层降级架构

```csharp
public interface IAudioCaptureService
{
    event Action<byte[]> AudioDataAvailable;  // PCM 16kHz 16bit mono
    Task StartAsync();
    Task StopAsync();
    CaptureMode ActiveMode { get; }
}

public enum CaptureMode { None, WebView2, ProcessLoopback, SystemLoopback }
```

**L1 - WebView2 注入 (`CaptureMode.WebView2`)：**
- 不直接捕获；由 `WebView2AudioBridge` 接收 JS 推送的数据
- `AudioCaptureService` 作为中间层，收到数据后通过 `AudioDataAvailable` 事件转发给 `SpeechRecognitionService`

**L2 - 进程级 Loopback (`CaptureMode.ProcessLoopback`)：**
- 使用 `Windows.Media.Audio.AudioGraph` API
- 设置 `AudioGraphSettings` 的 `AudioRenderCategory = AudioRenderCategory.Media`
- 通过 `IAudioSessionManager2` 枚举音频会话，过滤 `msedgewebview2.exe`
- 仅当 `CoreWebView2.IsDocumentPlayingAudio == true` 时启动

**L3 - 全系统 Loopback (`CaptureMode.SystemLoopback`)：**
- 使用 WASAPI `AudioClient.Initialize(AudioClientShareMode.Shared, AudioClientStreamFlags.Loopback, ...)`
- 仅当 `CoreWebView2.IsDocumentPlayingAudio == true` 且检测到语音时输出
- VAD（短时能量检测）：计算 RMS，低于阈值的静音段丢弃

**降级触发链：**

```
StartAsync()
→ L1 尝试：等待 JS bridge 发送数据 (3秒超时)
  → 收到 L1 数据? → Mode = WebView2 ✓
  → 3秒无数据? → L2 尝试
      → AudioGraph 可用且找到 WebView2 会话? → Mode = ProcessLoopback ✓
      → 失败? → L3
          → Mode = SystemLoopback (全系统 + VAD 过滤)
```

### 3.3 `Services/WebView2AudioBridge.cs`

**职责：** 将 `audio-capture.js` 捕获的 PCM 数据从 JS 传递到 C#

```csharp
public class WebView2AudioBridge
{
    private readonly CoreWebView2 _coreWebView;
    private readonly AudioCaptureService _audioCapture;

    public WebView2AudioBridge(CoreWebView2 coreWebView, AudioCaptureService audioCapture)
    {
        _coreWebView = coreWebView;
        _audioCapture = audioCapture;
    }

    public async Task InitializeAsync()
    {
        // 注入音频捕获脚本
        string script = await File.ReadAllTextAsync("Guides/audio-capture.js");
        await _coreWebView.AddScriptToExecuteOnDocumentCreatedAsync(script);

        // 注册 WebMessage 接收
        _coreWebView.WebMessageReceived += OnWebMessageReceived;
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var message = e.TryGetWebMessageAsString();
        if (string.IsNullOrEmpty(message) || !message.StartsWith("__gv_audio:"))
            return;

        // 格式: "__gv_audio:base64PcmData"
        var base64 = message["__gv_audio:".Length..];
        var pcmData = Convert.FromBase64String(base64);
        _audioCapture.OnWebView2AudioReceived(pcmData);
    }
}
```

### 3.4 `Guides/audio-capture.js`

```javascript
(function() {
  if (window.__gv_audioCapture) return;
  window.__gv_audioCapture = true;

  function initCapture(video) {
    if (!video) return;
    try {
      var ctx = new AudioContext({sampleRate: 16000});
      var source = ctx.createMediaStreamSource(video.captureStream());
      var processor = ctx.createScriptProcessor(4096, 1, 1);

      source.connect(processor);
      processor.connect(ctx.destination);

      processor.onaudioprocess = function(e) {
        var input = e.inputBuffer.getChannelData(0);
        var int16 = new Int16Array(input.length);
        for (var i = 0; i < input.length; i++) {
          var s = Math.max(-1, Math.min(1, input[i]));
          int16[i] = s < 0 ? s * 0x8000 : s * 0x7FFF;
        }
        // 转为 base64 发送
        var bytes = new Uint8Array(int16.buffer);
        var binary = '';
        for (var i = 0; i < bytes.byteLength; i++)
          binary += String.fromCharCode(bytes[i]);
        var base64 = btoa(binary);
        chrome.webview.postMessage('__gv_audio:' + base64);
      };

      console.log('[GuideAssistant] Audio capture started');
    } catch(ex) {
      console.warn('[GuideAssistant] Audio capture not available:', ex.message);
    }
  }

  // 监听视频元素
  var observer = new MutationObserver(function() {
    var v = document.querySelector('video');
    if (v && !v.__gv_captured) {
      v.__gv_captured = true;
      initCapture(v);
    }
  });
  observer.observe(document.body, {childList: true, subtree: true});

  // 检查已有视频
  var existing = document.querySelector('video');
  if (existing) { existing.__gv_captured = true; initCapture(existing); }
})();
```

**重要：** 此脚本通过 `AddScriptToExecuteOnDocumentCreatedAsync` 在页面创建时注入，确保在任何内容加载前就绪。

### 3.5 `Services/SpeechRecognitionService.cs`

```csharp
public class SpeechRecognitionService : IDisposable
{
    private readonly AudioCaptureService _audioCapture;
    private readonly SubtitleService _subtitleService;
    private SpeechRecognitionEngine? _engine;
    private bool _isRunning;

    public SpeechRecognitionService(AudioCaptureService audioCapture, SubtitleService subtitleService)
    {
        _audioCapture = audioCapture;
        _subtitleService = subtitleService;
    }

    public async Task StartAsync()
    {
        var recognizerInfo = SpeechRecognitionEngine.InstalledRecognizers()
            .FirstOrDefault(r => r.Culture.Name.StartsWith("zh"));
        if (recognizerInfo == null)
        {
            Log.Warning("No Chinese speech recognizer installed");
            return;
        }

        _engine = new SpeechRecognitionEngine(recognizerInfo);

        var dictation = new DictationGrammar();
        _engine.LoadGrammar(dictation);

        _engine.SpeechRecognized += (s, e) =>
        {
            if (e.Result.Confidence > 0.5)
            {
                _subtitleService.OnSpeechRecognized(e.Result.Text);
            }
        };

        _engine.SetInputToDefaultAudioDevice(); // 初始化默认输入
        _engine.RecognizeAsync(RecognizeMode.Single);
        // 注意：System.Speech 不能直接喂自定义音频流时的连续识别
        // 使用 RecognizeAsync(RecognizeMode.Single) + 每段结束后重新启动

        _audioCapture.AudioDataAvailable += OnAudioData;
        await _audioCapture.StartAsync();
        _isRunning = true;
    }

    // 每收到一段音频数据，重新启动识别
    private void OnAudioData(byte[] pcmData)
    {
        // System.Speech 的 SetInputToAudioStream 方案：
        // 需要 MemoryStream + 定期 RecognizeAsync
        // 实现细节见下方注释
    }

    public void Stop()
    {
        _audioCapture.AudioDataAvailable -= OnAudioData;
        _engine?.RecognizeAsyncCancel();
        _isRunning = false;
    }
}
```

**System.Speech 音频流喂入关键实现：**

`System.Speech.Recognition.SpeechRecognitionEngine` 支持 `SetInputToAudioStream()` 方法，可以传入自定义 `Stream`。流程如下：

1. 创建 `MemoryStream` 作为缓冲区
2. 每次 `AudioDataAvailable` 回调时 `stream.Write(pcmData, 0, pcmData.Length)`
3. 定期调用 `_engine.RecognizeAsync(RecognizeMode.Single)` 识别流中已有的数据
4. 识别完成后收到 `SpeechRecognized` 事件，结果文本通过 `SubtitleService.OnSpeechRecognized()` 发送
5. 立即重新调用 `_engine.RecognizeAsync(RecognizeMode.Single)` 继续下一段

**注意：** `System.Speech` 的 `SetInputToAudioStream` 需要一个可seek的流，且音频格式必须是 16kHz / 16bit / mono PCM。`MemoryStream` 满足此要求。

### 3.6 `SubtitleService` 修改

**目标：** 引入 `ISubtitleProvider` 接口，实现 CC 字幕与语音识别自动切换

**修改后的 `SubtitleService.cs`：**

```csharp
public interface ISubtitleProvider
{
    event Action<string>? SubtitleAvailable;
    Task StartAsync(string? url);
    Task StopAsync();
}

public class SubtitleService
{
    private ISubtitleProvider? _activeProvider;
    private readonly BilibiliApi _bilibiliApi;
    private readonly SpeechRecognitionService _speechRecognition;

    public event Action<string>? SubtitleChanged;
    public event Action<string>? DirectionWordDetected;

    public async Task StartAsync(string url)
    {
        // 先尝试加载 CC 字幕
        var ccData = await _bilibiliApi.GetSubtitle(url);
        if (ccData != null && ccData.Items.Count > 0)
        {
            // CC 字幕存在 → 使用 CC
            _activeProvider = new BilibiliCcProvider(ccData);
        }
        else
        {
            // CC 字幕不存在 → 使用语音识别
            await _speechRecognition.StartAsync();
        }
    }

    // 语音识别回调入口
    public void OnSpeechRecognized(string text)
    {
        SubtitleChanged?.Invoke(text);
        CheckDirectionWords(text);
    }

    // ... 其余现有方法保留
}
```

**`BilibiliCcProvider`** 将现有 `SubtitleService` 中的 CC 字幕同步逻辑提取为独立类。

---

## 四、现有项目修改详情

### 4.1 `GuideAssistant.csproj` 修改

```xml
<!-- 添加 NuGet 依赖 -->
<PackageReference Include="System.Speech" Version="10.0.0" />

<!-- 添加项目引用 -->
<ProjectReference Include="..\GuideAssistant.Overlays\GuideAssistant.Overlays.csproj" />
```

**注意：** 不要添加任何 WPF SDK 属性（`<UseWPF>`）到主项目，只在 `GuideAssistant.Overlays` 项目中使用 WPF。

### 4.2 `GlobalUsings.cs` 修改

```csharp
global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Threading.Tasks;
global using Microsoft.UI.Xaml;
global using Microsoft.UI.Xaml.Controls;
// 移除 global using Microsoft.UI.Xaml.Media;  // 与 WPF System.Windows.Media 冲突
// 移除 global using Microsoft.UI.Xaml.Shapes;  // 与 WPF System.Windows.Shapes 冲突
// 移除 global using Windows.UI;               // Colors 冲突
global using Microsoft.UI;
global using Microsoft.UI.Windowing;
global using Windows.Graphics;
global using WinRT.Interop;
```

**原因：** 当 WinUI3 项目引用 WPF 类库时，命名空间冲突会出现：
- `Microsoft.UI.Xaml.Shapes.Polygon` vs `System.Windows.Shapes.Polygon`
- `Microsoft.UI.Xaml.Media.SolidColorBrush` vs `System.Windows.Media.SolidColorBrush`
- `Microsoft.UI.Colors` vs `System.Windows.Media.Colors`

解决方案：移除主项目中会冲突的 global using，在需要的地方显式 using。

**方案简化：** 推荐不在 GlobalUsings.cs 中移除，而是在少数有冲突的文件中显式使用完整命名空间。大多数现有文件不会直接 import WPF 命名空间，WPF 类只在 `GuideAssistant.Overlays` 项目内部使用。

**实际修改：** 不需要改 `GlobalUsings.cs`。WinUI3 主项目通过 `IOverlayController` 接口与 WPF 交互，不直接引用 WPF 类型，所以不会产生命名空间冲突。

### 4.3 `App.xaml.cs` 修改

在 `ConfigureServices` 方法中添加：

```csharp
// WPF 覆盖窗口宿主
var wpfHost = new GuideAssistant.Overlays.WpfThreadHost();
wpfHost.Start();
services.AddSingleton<GuideAssistant.Overlays.IOverlayController>(wpfHost);

// 音频捕获 + 语音识别
services.AddSingleton<AudioCaptureService>();
services.AddSingleton<SpeechRecognitionService>();
// WebView2AudioBridge 在 MainWindow 中手动创建（需要 CoreWebView2 引用）
```

### 4.4 `MainWindow.xaml.cs` 修改

**核心变更：**

1. **新增字段：**
```csharp
private readonly IOverlayController _overlayCtrl;
private readonly AudioCaptureService _audioCapture;
private readonly SpeechRecognitionService _speechRecognition;
private WebView2AudioBridge? _audioBridge;
```

2. **构造函数新增参数：** 通过 DI 注入上述新服务

3. **`OverlayToggleMessage` 处理器修改：**
```csharp
// 字幕切换 - 改为控制 WPF 覆盖窗口
if (m.Type == "subtitle")
{
    if (m.Enabled)
    {
        _overlayCtrl.ShowSubtitle();
        _subtitleService.StartAsync(_viewModel.CurrentUrl);
    }
    else
    {
        _overlayCtrl.HideSubtitle();
        _subtitleService.StopAsync();
    }
}

// 小地图切换 - 改为控制 WPF 覆盖窗口
if (m.Type == "minimap")
{
    if (m.Enabled)
        _overlayCtrl.ShowMiniMap();
    else
        _overlayCtrl.HideMiniMap();
}
```

4. **`WebViewControl.UrlChanged` 处理器：** 在导航到新 B站 URL 时初始化音频桥：
```csharp
// 在 InitializeWebView() 的 UrlChanged 中
if (url.Contains("bilibili.com/video/"))
{
    // 原有字幕加载
    _ = _subtitleService.LoadSubtitle(url);

    // 新增：初始化 WebView2 音频桥
    if (_audioBridge == null && WebViewControl.CoreWebView2 != null)
    {
        _audioBridge = new WebView2AudioBridge(WebViewControl.CoreWebView2, _audioCapture);
        _ = _audioBridge.InitializeAsync();
    }
}
```

5. **`DirectionWordDetected` → WPF 小地图：**
```csharp
_subtitleService.DirectionWordDetected += word =>
{
    _overlayCtrl.ShowDirection(word);
};
```

6. **`RestoreToolsBtn_Click` 修改：** 不再创建 WinUI3 `SubtitleOverlay` / `MiniMapOverlay`，改用 WPF。

7. **`MainWindow_Closed`：** 添加 WPF 宿主清理：
```csharp
_overlayCtrl.Dispose();
// ... 或在 App.xaml.cs 的退出逻辑中处理
```

### 4.5 `SubtitleService.cs` 修改

**修改内容：**
1. 提取 `ISubtitleProvider` 接口（在 `Services/` 目录下新建文件或直接在 SubtitleService.cs 中定义）
2. 添加 `BilibiliCcProvider` 类（现有 CC 逻辑封装）
3. `SubtitleService` 改为组合模式：
   - `_activeProvider` 字段持有当前 provider
   - `StartAsync(url)` → CC 优先，失败时切语音
   - `StopAsync()` → 停止当前 provider
   - `SubtitleChanged` 事件由 provider 转发

### 4.6 `GameVideoTool.slnx` 修改

添加第二个项目：
```xml
<Project Path="GuideAssistant.Overlays/GuideAssistant.Overlays.csproj">
    <Platform Solution="*|ARM64" Project="ARM64" />
    <Platform Solution="*|x64" Project="x64" />
    <Platform Solution="*|x86" Project="x86" />
</Project>
```

**注意：** `.slnx` 格式中不需要 `<Deploy />` 元素（WPF 类库不需要部署为 MSIX）。

---

## 五、文件操作清单汇总

### 新增文件

| 文件 | 职责 |
|------|------|
| `GuideAssistant.Overlays/GuideAssistant.Overlays.csproj` | WPF 类库项目文件 |
| `GuideAssistant.Overlays/IOverlayController.cs` | 覆盖窗口控制接口 |
| `GuideAssistant.Overlays/WpfThreadHost.cs` | STA 线程 + Dispatcher 管理 |
| `GuideAssistant.Overlays/Overlays/WpfOverlayBase.cs` | 透明窗口基类 |
| `GuideAssistant.Overlays/Overlays/WpfMiniMapOverlay.xaml` | 小地图 XAML |
| `GuideAssistant.Overlays/Overlays/WpfMiniMapOverlay.xaml.cs` | 小地图代码 |
| `GuideAssistant.Overlays/Overlays/WpfSubtitleOverlay.xaml` | 字幕 XAML |
| `GuideAssistant.Overlays/Overlays/WpfSubtitleOverlay.xaml.cs` | 字幕代码 |
| `GuideAssistant.Overlays/Win32/NativeMethods.cs` | WPF 侧 Win32 P/Invoke |
| `GuideAssistant/Services/AudioCaptureService.cs` | 音频捕获三层降级 |
| `GuideAssistant/Services/WebView2AudioBridge.cs` | JS ↔ C# 音频传输桥 |
| `GuideAssistant/Services/SpeechRecognitionService.cs` | 语音识别服务 |
| `GuideAssistant/Services/BilibiliCcProvider.cs` | CC 字幕 Provider 实现 |
| `GuideAssistant/Guides/audio-capture.js` | 注入 WebView2 的音频捕获 JS |

### 修改文件

| 文件 | 修改内容 |
|------|----------|
| `GuideAssistant.csproj` | + System.Speech NuGet + ProjectReference |
| `App.xaml.cs` | DI 注册 WpfThreadHost, AudioCaptureService, SpeechRecognitionService |
| `MainWindow.xaml.cs` | 对接 IOverlayController, 初始化音频桥, 方向词→小地图 |
| `Services/SubtitleService.cs` | 引入 ISubtitleProvider, CC/语音自动切换 |
| `GameVideoTool.slnx` | 添加 GuideAssistant.Overlays 项目 |

### 待删除（WPF 版稳定后）

| 文件 | 原因 |
|------|------|
| `Views/MiniMapOverlay.xaml` | 替换为 WPF 版 |
| `Views/MiniMapOverlay.xaml.cs` | 替换为 WPF 版 |
| `Views/SubtitleOverlay.xaml` | 替换为 WPF 版 |
| `Views/SubtitleOverlay.xaml.cs` | 替换为 WPF 版 |

---

## 六、实现顺序

1. **Agent 1** — 创建 `GuideAssistant.Overlays` WPF 类库项目（8个文件）
2. **Agent 2** — 实现音频捕获 + 语音识别服务 + CC Provider（4个C# + 1个JS）
3. **Agent 3** — 修改现有项目：csproj, slnx, App.xaml.cs, MainWindow.xaml.cs（4个修改）
4. **Agent 4** — 修改 SubtitleService.cs，引入 ISubtitleProvider（1个修改 + 迁移）
