using Serilog;

namespace GuideAssistant.Services;

public interface IAudioCaptureService
{
    event Action<byte[]>? AudioDataAvailable;
    Task StartAsync();
    Task StopAsync();
    CaptureMode ActiveMode { get; }
}

public enum CaptureMode { None, WebView2, ProcessLoopback, SystemLoopback }

public class AudioCaptureService : IAudioCaptureService, IDisposable
{
    private CaptureMode _activeMode = CaptureMode.None;
    private bool _isRunning;
    private CancellationTokenSource? _cts;
    private Task? _captureTask;

    public event Action<byte[]>? AudioDataAvailable;
    public CaptureMode ActiveMode => _activeMode;

    public async Task StartAsync()
    {
        _cts = new CancellationTokenSource();
        _isRunning = true;

        var l1Tcs = new TaskCompletionSource<bool>();
        Action<byte[]>? l1Handler = null;
        l1Handler = data =>
        {
            l1Tcs.TrySetResult(true);
        };
        AudioDataAvailable += l1Handler;

        var completed = await Task.WhenAny(l1Tcs.Task, Task.Delay(3000));
        AudioDataAvailable -= l1Handler;

        if (completed == l1Tcs.Task && l1Tcs.Task.Result)
        {
            _activeMode = CaptureMode.WebView2;
            Log.Information("AudioCapture: WebView2 mode active");
            return;
        }

        try
        {
            _activeMode = CaptureMode.ProcessLoopback;
            Log.Information("AudioCapture: ProcessLoopback mode active (L2)");
            _captureTask = Task.Run(() => SimulatedLoopbackCapture(_cts.Token));
            return;
        }
        catch { }

        _activeMode = CaptureMode.SystemLoopback;
        Log.Information("AudioCapture: SystemLoopback mode active (L3)");
        _captureTask = Task.Run(() => SystemLoopbackCapture(_cts.Token));
    }

    public Task StopAsync()
    {
        _isRunning = false;
        _cts?.Cancel();
        _activeMode = CaptureMode.None;
        return Task.CompletedTask;
    }

    public void OnWebView2AudioReceived(byte[] pcmData)
    {
        if (_activeMode == CaptureMode.WebView2 && _isRunning)
        {
            AudioDataAvailable?.Invoke(pcmData);
        }
    }

    private async Task SimulatedLoopbackCapture(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _isRunning)
        {
            await Task.Delay(100, ct);
        }
    }

    private async Task SystemLoopbackCapture(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _isRunning)
        {
            await Task.Delay(100, ct);
        }
    }

    public static bool IsVoiceActive(byte[] pcmBuffer, double threshold = 500.0)
    {
        if (pcmBuffer.Length < 2) return false;
        double sum = 0;
        for (int i = 0; i < pcmBuffer.Length - 1; i += 2)
        {
            short sample = BitConverter.ToInt16(pcmBuffer, i);
            sum += sample * sample;
        }
        double rms = Math.Sqrt(sum / (pcmBuffer.Length / 2));
        return rms > threshold;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _isRunning = false;
        GC.SuppressFinalize(this);
    }
}
