using System.Speech.AudioFormat;
using System.Speech.Recognition;
using Serilog;

namespace GuideAssistant.Services;

public class SpeechRecognitionService : IDisposable
{
    private readonly AudioCaptureService _audioCapture;
    private SpeechRecognitionEngine? _engine;
    private BlockingPcmStream? _audioStream;
    private bool _isRunning;
    private bool _disposed;
    private readonly object _lock = new();

    public event Action<string>? SpeechRecognized;

    public SpeechRecognitionService(AudioCaptureService audioCapture)
    {
        _audioCapture = audioCapture;
    }

    public async Task<bool> StartAsync()
    {
        lock (_lock)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SpeechRecognitionService));
            if (_isRunning) return true;
            _isRunning = true;
        }

        var recognizerInfo = SpeechRecognitionEngine.InstalledRecognizers()
            .FirstOrDefault(r => r.Culture.Name.StartsWith("zh"));

        if (recognizerInfo == null)
        {
            Log.Warning("No Chinese speech recognizer installed. Speech recognition disabled.");
            lock (_lock) { _isRunning = false; }
            return false;
        }

        _engine = new SpeechRecognitionEngine(recognizerInfo);
        _engine.SpeechRecognized += OnSpeechRecognized;
        _engine.RecognizeCompleted += OnRecognizeCompleted;

        _engine.LoadGrammar(new DictationGrammar());

        _audioStream = new BlockingPcmStream();
        _engine.SetInputToAudioStream(_audioStream,
            new SpeechAudioFormatInfo(16000, AudioBitsPerSample.Sixteen, AudioChannel.Mono));

        _audioCapture.AudioDataAvailable += OnAudioData;
        await _audioCapture.StartAsync();

        if (_audioCapture.ActiveMode == CaptureMode.None)
        {
            Log.Warning("Audio capture did not activate. Speech recognition disabled.");
            await StopAsync();
            return false;
        }

        _engine.InitialSilenceTimeout = TimeSpan.FromSeconds(2);
        _engine.BabbleTimeout = TimeSpan.FromSeconds(3);
        _engine.EndSilenceTimeout = TimeSpan.FromSeconds(1);
        _engine.EndSilenceTimeoutAmbiguous = TimeSpan.FromSeconds(1.5);

        try
        {
            _engine.RecognizeAsync(RecognizeMode.Multiple);
        }
        catch (InvalidOperationException ex)
        {
            Log.Warning(ex, "Speech recognition could not start");
            await StopAsync();
            return false;
        }

        Log.Information("SpeechRecognitionService started (culture={Culture})", recognizerInfo.Culture.Name);
        return true;
    }

    private void OnAudioData(byte[] pcmData)
    {
        _audioStream?.Append(pcmData);
    }

    private void OnSpeechRecognized(object? sender, SpeechRecognizedEventArgs e)
    {
        if (e.Result.Confidence > 0.4 && !string.IsNullOrWhiteSpace(e.Result.Text))
        {
            SpeechRecognized?.Invoke(e.Result.Text);
            Log.Debug("Speech recognized: {Text} (confidence={Conf})", e.Result.Text, e.Result.Confidence);
        }
    }

    private void OnRecognizeCompleted(object? sender, RecognizeCompletedEventArgs e)
    {
        if (e.Error != null)
        {
            Log.Error(e.Error, "Speech recognition completed with an error");
            return;
        }

        Log.Debug("Speech recognition completed (cancelled={Cancelled}, inputEnded={InputEnded})", e.Cancelled, e.InputStreamEnded);
    }

    public Task StopAsync()
    {
        SpeechRecognitionEngine? engine;
        BlockingPcmStream? stream;

        lock (_lock)
        {
            _isRunning = false;
            engine = _engine;
            _engine = null;
            stream = _audioStream;
            _audioStream = null;
        }

        _audioCapture.AudioDataAvailable -= OnAudioData;
        if (engine != null)
        {
            engine.SpeechRecognized -= OnSpeechRecognized;
            engine.RecognizeCompleted -= OnRecognizeCompleted;
        }

        try
        {
            engine?.RecognizeAsyncCancel();
        }
        catch (InvalidOperationException ex)
        {
            Log.Debug(ex, "Speech recognition cancel requested after completion");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Speech recognition cancel failed");
        }

        if (stream != null)
        {
            stream.Complete();
            stream.Dispose();
        }

        engine?.Dispose();

        return _audioCapture.StopAsync();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
        }

        StopAsync().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }
}
