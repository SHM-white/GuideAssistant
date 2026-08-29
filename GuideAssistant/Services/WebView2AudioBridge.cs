using Microsoft.Web.WebView2.Core;
using Serilog;

namespace GuideAssistant.Services;

public class WebView2AudioBridge : IDisposable
{
    private readonly CoreWebView2 _coreWebView;
    private readonly AudioCaptureService _audioCapture;
    private bool _initialized;

    public WebView2AudioBridge(CoreWebView2 coreWebView, AudioCaptureService audioCapture)
    {
        _coreWebView = coreWebView;
        _audioCapture = audioCapture;
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;

        var scriptPath = System.IO.Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Guides", "audio-capture.js");

        string script;
        if (System.IO.File.Exists(scriptPath))
        {
            script = await System.IO.File.ReadAllTextAsync(scriptPath);
        }
        else
        {
            script = GetEmbeddedAudioCaptureScript();
        }

        _coreWebView.WebMessageReceived += OnWebMessageReceived;
        try
        {
            await _coreWebView.AddScriptToExecuteOnDocumentCreatedAsync(script);
            await _coreWebView.ExecuteScriptAsync(script);
            _initialized = true;
            Log.Information("WebView2AudioBridge initialized");
        }
        catch
        {
            _coreWebView.WebMessageReceived -= OnWebMessageReceived;
            throw;
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var message = e.TryGetWebMessageAsString();
            if (string.IsNullOrEmpty(message) || !message.StartsWith("__gv_audio:"))
                return;

            var base64 = message["__gv_audio:".Length..];
            var pcmData = Convert.FromBase64String(base64);
            _audioCapture.OnWebView2AudioReceived(pcmData);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "WebView2AudioBridge: failed to process audio message");
        }
    }

    private static string GetEmbeddedAudioCaptureScript()
    {
        return @"
(function() {
  if (window.__gv_audioCapture) return;
  window.__gv_audioCapture = true;

  function initCapture(video) {
    if (!video || !video.captureStream) return;
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
        var bytes = new Uint8Array(int16.buffer);
        var binary = '';
        for (var i = 0; i < bytes.byteLength; i++)
          binary += String.fromCharCode(bytes[i]);
        var base64 = btoa(binary);
        try { chrome.webview.postMessage('__gv_audio:' + base64); } catch(e) {}
      };
    } catch(ex) {
      console.warn('[GuideAssistant] Audio capture not available:', ex.message);
    }
  }

  var observer = new MutationObserver(function() {
    var v = document.querySelector('video');
    if (v && !v.__gv_captured) {
      v.__gv_captured = true;
      initCapture(v);
    }
  });
  observer.observe(document.body||document.documentElement, {childList: true, subtree: true});

  var existing = document.querySelector('video');
  if (existing) { existing.__gv_captured = true; initCapture(existing); }
})();
".Trim();
    }

    public void Dispose()
    {
        _coreWebView.WebMessageReceived -= OnWebMessageReceived;
        GC.SuppressFinalize(this);
    }
}
