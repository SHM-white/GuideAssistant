namespace GuideAssistant.Helpers;

public static class ScriptInjector
{
    public static string DefaultFullscreenHijack = @"
(function() {
    'use strict';

    function hijackFullscreen() {
        // Redirect Bilibili player native fullscreen → web fullscreen
        // Strategy: intercept every click on the native fullscreen button
        // and redirect it to the web fullscreen button before it fires.
        document.addEventListener('click', function(e) {
            const target = e.target.closest('.bpx-player-ctrl-full');
            if (!target) return;

            e.preventDefault();
            e.stopPropagation();
            e.stopImmediatePropagation();

            const webBtn = document.querySelector('.bpx-player-ctrl-web');
            if (webBtn) webBtn.click();
        }, true);

        // Safety net: if native fullscreen fires anyway (e.g. via keyboard shortcut),
        // exit it immediately and trigger web fullscreen.
        document.addEventListener('fullscreenchange', function() {
            if (!document.fullscreenElement) return;
            document.exitFullscreen();
            const webBtn = document.querySelector('.bpx-player-ctrl-web');
            if (webBtn) webBtn.click();
        }, true);
    }

    if (document.readyState === 'loading')
        document.addEventListener('DOMContentLoaded', hijackFullscreen);
    else
        hijackFullscreen();
})();
";
}
