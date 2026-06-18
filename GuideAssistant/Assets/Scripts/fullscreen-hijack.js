(function() {
    'use strict';

    function hijackFullscreen() {
        Element.prototype.requestFullscreen = function() {
            return inContainerFullscreen(this);
        };
        Element.prototype.webkitRequestFullscreen = function() {
            return inContainerFullscreen(this);
        };

        function inContainerFullscreen(element) {
            const video = element.querySelector('video') || element;
            const bpxContainer = document.querySelector('.bpx-player-video-wrap');
            const container = bpxContainer || document.body;

            let fsDiv = document.getElementById('__gv_fullscreen');
            if (!fsDiv) {
                fsDiv = document.createElement('div');
                fsDiv.id = '__gv_fullscreen';
                fsDiv.style.cssText = 'position:fixed;left:0;top:0;width:100%;height:100%;z-index:9999;background:#000;display:flex;align-items:center;justify-content:center;';
                document.body.appendChild(fsDiv);

                const exitBtn = document.createElement('button');
                exitBtn.textContent = '✕ 退出全屏 (Esc)';
                exitBtn.style.cssText = 'position:absolute;top:12px;right:16px;z-index:10000;padding:6px 18px;background:rgba(255,255,255,0.15);color:#fff;border:1px solid rgba(255,255,255,0.25);border-radius:6px;cursor:pointer;font-size:13px;backdrop-filter:blur(8px);transition:background .2s;';
                exitBtn.onmouseover = function() { this.style.background = 'rgba(255,255,255,0.3)'; };
                exitBtn.onmouseout = function() { this.style.background = 'rgba(255,255,255,0.15)'; };
                exitBtn.onclick = exitFullscreen;
                fsDiv.appendChild(exitBtn);

                document.addEventListener('keydown', function(e) {
                    if (e.key === 'Escape' && document.getElementById('__gv_fullscreen')) {
                        exitFullscreen();
                    }
                });
            }

            // Move video into fullscreen container
            video.style.width = '100%';
            video.style.height = '100%';
            video.style.objectFit = 'contain';
            fsDiv.appendChild(video);
            return Promise.resolve();
        }

        function exitFullscreen() {
            const fsDiv = document.getElementById('__gv_fullscreen');
            if (!fsDiv) return;
            const video = fsDiv.querySelector('video');
            if (video) {
                video.style.cssText = '';
                const wrap = document.querySelector('.bpx-player-video-wrap');
                if (wrap) wrap.appendChild(video);
                else document.body.appendChild(video);
            }
            fsDiv.remove();
        }

        // Block native fullscreen events
        document.addEventListener('fullscreenchange', function(e) {
            if (!document.getElementById('__gv_fullscreen')) {
                if (document.fullscreenElement) document.exitFullscreen();
            }
            e.stopPropagation();
        }, true);
        document.addEventListener('webkitfullscreenchange', function(e) {
            e.stopPropagation();
        }, true);
    }

    if (document.readyState === 'loading')
        document.addEventListener('DOMContentLoaded', hijackFullscreen);
    else
        hijackFullscreen();
})();
