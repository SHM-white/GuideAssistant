using System.Speech.AudioFormat;
using System.Speech.Recognition;
using Serilog;

namespace GuideAssistant.Services;

public class SpeechRecognitionService : IDisposable
{
    private readonly AudioCaptureService _audioCapture;
    private SpeechRecognitionEngine? _engine;
    private MemoryStream? _audioStream;
    private bool _isRunning;
    private bool _isRecognizing;
    private readonly object _lock = new();

    public event Action<string>? SpeechRecognized;

    public SpeechRecognitionService(AudioCaptureService audioCapture)
    {
        _audioCapture = audioCapture;
    }

    public async Task StartAsync()
    {
        var recognizerInfo = SpeechRecognitionEngine.InstalledRecognizers()
            .FirstOrDefault(r => r.Culture.Name.StartsWith("zh"));

        if (recognizerInfo == null)
        {
            Log.Warning("No Chinese speech recognizer installed. Speech recognition disabled.");
            return;
        }

        _engine = new SpeechRecognitionEngine(recognizerInfo);
        _engine.SpeechRecognized += OnSpeechRecognized;
        _engine.RecognizeCompleted += OnRecognizeCompleted;

        var dictation = new DictationGrammar();
        _engine.LoadGrammar(dictation);

        if (_audioCapture.ActiveMode == CaptureMode.WebView2)
        {
            _audioStream = new MemoryStream();
            _engine.SetInputToAudioStream(_audioStream,
                new SpeechAudioFormatInfo(16000, AudioBitsPerSample.Sixteen, AudioChannel.Mono));
        }
        else
        {
            _engine.SetInputToDefaultAudioDevice();
        }

        _engine.InitialSilenceTimeout = TimeSpan.FromSeconds(2);
        _engine.BabbleTimeout = TimeSpan.FromSeconds(3);
        _engine.EndSilenceTimeout = TimeSpan.FromSeconds(1);
        _engine.EndSilenceTimeoutAmbiguous = TimeSpan.FromSeconds(1.5);

        _audioCapture.AudioDataAvailable += OnAudioData;
        await _audioCapture.StartAsync();

        StartSingleRecognition();
        _isRunning = true;
        Log.Information("SpeechRecognitionService started (culture={Culture})", recognizerInfo.Culture.Name);
    }

    private void OnAudioData(byte[] pcmData)
    {
        if (_audioCapture.ActiveMode != CaptureMode.WebView2) return;

        lock (_lock)
        {
            if (_audioStream == null) return;
            try
            {
                long pos = _audioStream.Position;
                _audioStream.Write(pcmData, 0, pcmData.Length);
                _audioStream.Position = pos;
            }
            catch (ObjectDisposedException) { }
        }
    }

    private void StartSingleRecognition()
    {
        if (_engine == null || _isRecognizing) return;
        _isRecognizing = true;
        try
        {
            _engine.RecognizeAsync(RecognizeMode.Single);
        }
        catch (InvalidOperationException)
        {
            _isRecognizing = false;
        }
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
        _isRecognizing = false;
        if (_isRunning && _engine != null)
        {
            StartSingleRecognition();
        }
    }

    public Task StopAsync()
    {
        _isRunning = false;
        _audioCapture.AudioDataAvailable -= OnAudioData;
        try { _engine?.RecognizeAsyncCancel(); } catch { }

        lock (_lock)
        {
            _audioStream?.Dispose();
            _audioStream = null;
        }

        return _audioCapture.StopAsync();
    }

    public void Dispose()
    {
        _isRunning = false;
        _audioCapture.AudioDataAvailable -= OnAudioData;
        try { _engine?.RecognizeAsyncCancel(); } catch { }
        _engine?.Dispose();
        _engine = null;

        lock (_lock)
        {
            _audioStream?.Dispose();
            _audioStream = null;
        }

        GC.SuppressFinalize(this);
    }
}
