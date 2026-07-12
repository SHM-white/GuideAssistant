using Microsoft.Web.WebView2.Core;
using Serilog;

namespace GuideAssistant.Services;

public class DomCcProvider : ISubtitleProvider, IDisposable
{
    private readonly CoreWebView2 _coreWebView;
    private string? _lastText;

    public event Action<string>? SubtitleChanged;
    public event Action<string>? DirectionWordDetected;

    private static readonly string[] s_directionWords = DirectionWords.All;

    public DomCcProvider(CoreWebView2 coreWebView)
    {
        _coreWebView = coreWebView;
    }

    public Task<bool> StartAsync(string url, SubtitleData? data = null)
    {
        _coreWebView.WebMessageReceived += OnCcMessage;
        Log.Information("DomCcProvider started (listening for __gv_cc messages)");
        return Task.FromResult(true);
    }

    public Task StopAsync()
    {
        _coreWebView.WebMessageReceived -= OnCcMessage;
        return Task.CompletedTask;
    }

    public void UpdateTime(double currentTime) { }

    private void OnCcMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var msg = e.TryGetWebMessageAsString();
            if (string.IsNullOrEmpty(msg) || !msg.StartsWith("__gv_cc:")) return;

            var text = msg["__gv_cc:".Length..];
            if (string.IsNullOrWhiteSpace(text)) return;

            if (text != _lastText)
            {
                _lastText = text;
                SubtitleChanged?.Invoke(text);
                CheckDirectionWords(text);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "DomCcProvider: failed to process CC message");
        }
    }

    private void CheckDirectionWords(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        foreach (var word in s_directionWords)
        {
            if (text.Contains(word))
            {
                DirectionWordDetected?.Invoke(word);
                return;
            }
        }
    }

    public void Dispose()
    {
        try { _coreWebView.WebMessageReceived -= OnCcMessage; }
        catch (ObjectDisposedException) { }
    }
}
