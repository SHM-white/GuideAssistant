using System.Text;

namespace GuideAssistant.Helpers;

public static class ScriptInjector
{
    public static string WrapInScript(string script)
    {
        return $@"
        (function() {{
            {script}
        }})();";
    }

    public static string GetFullscreenHijackScript()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory, "Assets", "Scripts", "fullscreen-hijack.js");
        if (File.Exists(path))
            return File.ReadAllText(path);
        return DefaultFullscreenHijack;
    }

    public static string DefaultFullscreenHijack = @"
(function() {
    'use strict';

    function hijackFullscreen() {
        const origRequestFullscreen = Element.prototype.requestFullscreen;
        const origWebkitRequestFullscreen = Element.prototype.webkitRequestFullscreen;

        Element.prototype.requestFullscreen = function() {
            return inContainerFullscreen(this);
        };
        Element.prototype.webkitRequestFullscreen = function() {
            return inContainerFullscreen(this);
        };

        function inContainerFullscreen(element) {
            const video = element.querySelector('video') || element;
            const container = video.closest('[data-webview-container]') || document.body;

            let fsDiv = document.getElementById('__gv_fullscreen');
            if (!fsDiv) {
                fsDiv = document.createElement('div');
                fsDiv.id = '__gv_fullscreen';
                fsDiv.style.cssText = 'position:fixed;left:0;top:0;width:100%;height:100%;z-index:9999;background:#000;display:flex;align-items:center;justify-content:center;';
                container.appendChild(fsDiv);

                const exitBtn = document.createElement('button');
                exitBtn.textContent = '✕ 退出全屏';
                exitBtn.style.cssText = 'position:absolute;top:10px;right:10px;z-index:10000;padding:6px 16px;background:rgba(255,255,255,0.2);color:#fff;border:1px solid rgba(255,255,255,0.3);border-radius:4px;cursor:pointer;font-size:13px;';
                exitBtn.onclick = exitFullscreen;
                fsDiv.appendChild(exitBtn);

                document.addEventListener('keydown', function(e) {
                    if (e.key === 'Escape' && document.getElementById('__gv_fullscreen')) {
                        exitFullscreen();
                    }
                });
            }

            fsDiv.appendChild(video);
            video.style.width = '100%';
            video.style.height = '100%';
            video.style.objectFit = 'contain';
            return Promise.resolve();
        }

        function exitFullscreen() {
            const fsDiv = document.getElementById('__gv_fullscreen');
            if (fsDiv) {
                const video = fsDiv.querySelector('video');
                if (video) {
                    video.style.cssText = '';
                    const origParent = document.querySelector('.bpx-player-video-wrap') || document.body;
                    origParent.appendChild(video);
                }
                fsDiv.remove();
            }
        }

        document.addEventListener('fullscreenchange', function(e) {
            e.preventDefault();
            e.stopPropagation();
        }, true);
        document.addEventListener('webkitfullscreenchange', function(e) {
            e.preventDefault();
            e.stopPropagation();
        }, true);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', hijackFullscreen);
    } else {
        hijackFullscreen();
    }
})();
";
}
