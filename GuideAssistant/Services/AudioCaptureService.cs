using NAudio.Wave;
using Serilog;

namespace GuideAssistant.Services;

public interface IAudioCaptureService
{
    event Action<byte[]>? AudioDataAvailable;
    Task StartAsync();
    Task StopAsync();
    CaptureMode ActiveMode { get; }
}

public enum CaptureMode { None, WebView2, SystemLoopback }

public sealed class AudioCaptureService : IAudioCaptureService, IDisposable
{
    private const int ProbeTimeoutMilliseconds = 3000;
    private readonly object _gate = new();
    private CaptureMode _activeMode = CaptureMode.None;
    private bool _isRunning;
    private bool _disposed;
    private TaskCompletionSource<bool>? _probeTcs;
    private WasapiLoopbackCapture? _loopback;

    public event Action<byte[]>? AudioDataAvailable;
    public CaptureMode ActiveMode { get { lock (_gate) return _activeMode; } }

    public async Task StartAsync()
    {
        TaskCompletionSource<bool> probe;
        lock (_gate)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AudioCaptureService));
            if (_isRunning) return;
            _isRunning = true;
            _activeMode = CaptureMode.None;
            _probeTcs = probe = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        try
        {
            var completed = await Task.WhenAny(probe.Task, Task.Delay(ProbeTimeoutMilliseconds)).ConfigureAwait(false);
            if (completed == probe.Task && probe.Task.Result)
            {
                Log.Information("AudioCapture: WebView2 mode active");
                return;
            }

            WasapiLoopbackCapture? capture = null;
            lock (_gate)
            {
                if (_isRunning && !_disposed && _activeMode == CaptureMode.None)
                {
                    capture = new WasapiLoopbackCapture { WaveFormat = new WaveFormat(16000, 16, 1) };
                    capture.DataAvailable += OnLoopbackDataAvailable;
                    capture.RecordingStopped += OnLoopbackRecordingStopped;
                    _loopback = capture;
                    _activeMode = CaptureMode.SystemLoopback;
                }
            }

            if (capture is not null)
            {
                try
                {
                    capture.StartRecording();
                    Log.Information("AudioCapture: SystemLoopback mode active");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "AudioCapture: failed to initialize or start system loopback recording");
                    lock (_gate)
                    {
                        if (ReferenceEquals(_loopback, capture)) _loopback = null;
                        if (_activeMode == CaptureMode.SystemLoopback) _activeMode = CaptureMode.None;
                    }

                    StopAndDisposeLoopback(capture);
                }
            }
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_probeTcs, probe)) _probeTcs = null;
            }
        }
    }

    public Task StopAsync() { StopCore(); return Task.CompletedTask; }

    public void OnWebView2AudioReceived(byte[] pcmData)
    {
        if (pcmData.Length == 0) return;

        byte[]? payload = null;
        TaskCompletionSource<bool>? probe = null;
        WasapiLoopbackCapture? loopbackToStop = null;

        lock (_gate)
        {
            if (!_isRunning || _disposed) return;

            switch (_activeMode)
            {
                case CaptureMode.None:
                    _activeMode = CaptureMode.WebView2;
                    probe = _probeTcs;
                    break;
                case CaptureMode.SystemLoopback:
                    _activeMode = CaptureMode.WebView2;
                    loopbackToStop = _loopback;
                    _loopback = null;
                    break;
                case CaptureMode.WebView2:
                    break;
            }

            payload = new byte[pcmData.Length];
            Buffer.BlockCopy(pcmData, 0, payload, 0, pcmData.Length);
        }

        probe?.TrySetResult(true);
        if (loopbackToStop is not null)
        {
            StopAndDisposeLoopback(loopbackToStop);
            Log.Information("AudioCapture: switched from system loopback to WebView2");
        }

        if (payload is not null)
        {
            AudioDataAvailable?.Invoke(payload);
        }
    }

    private void OnLoopbackDataAvailable(object? sender, WaveInEventArgs e)
    {
        byte[]? payload = null;
        lock (_gate)
        {
            if (!_isRunning || _disposed || _activeMode != CaptureMode.SystemLoopback || !ReferenceEquals(sender, _loopback) || e.BytesRecorded == 0)
            {
                return;
            }

            payload = new byte[e.BytesRecorded];
            Buffer.BlockCopy(e.Buffer, 0, payload, 0, e.BytesRecorded);
        }

        if (payload is not null)
        {
            AudioDataAvailable?.Invoke(payload);
        }
    }

    private void OnLoopbackRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null) Log.Error(e.Exception, "AudioCapture: system loopback recording stopped with an error");
        lock (_gate)
        {
            if (ReferenceEquals(sender, _loopback)) _loopback = null;
            if (_activeMode == CaptureMode.SystemLoopback) _activeMode = CaptureMode.None;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }

        StopCore();
        GC.SuppressFinalize(this);
    }

    private void StopCore()
    {
        WasapiLoopbackCapture? loopback;
        TaskCompletionSource<bool>? probe;

        lock (_gate)
        {
            _isRunning = false;
            _activeMode = CaptureMode.None;
            loopback = _loopback;
            _loopback = null;
            probe = _probeTcs;
            _probeTcs = null;
        }

        probe?.TrySetResult(false);
        if (loopback is not null) StopAndDisposeLoopback(loopback);
    }

    private static void StopAndDisposeLoopback(WasapiLoopbackCapture capture)
    {
        try { capture.StopRecording(); }
        catch (Exception ex) { Log.Debug(ex, "AudioCapture: system loopback stop failed"); }

        try { capture.Dispose(); }
        catch (Exception ex) { Log.Debug(ex, "AudioCapture: system loopback dispose failed"); }
    }
}
