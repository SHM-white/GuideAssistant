// Player control functions - called via WebView2.ExecuteScriptAsync
(function() {
    'use strict';

    window.__gv_player = {
        getVideo: function() {
            return document.querySelector('video') ||
                   document.querySelector('bpx-player video') ||
                   document.querySelector('video[class*="video"]');
        },

        togglePlay: function() {
            const v = this.getVideo();
            if (!v) return 'no video';
            if (v.paused) { v.play(); return 'play'; }
            else { v.pause(); return 'pause'; }
        },

        fastForward: function(sec) {
            const v = this.getVideo();
            if (!v) return;
            v.currentTime = Math.min(v.duration || 0, v.currentTime + (sec || 10));
        },

        fastBackward: function(sec) {
            const v = this.getVideo();
            if (!v) return;
            v.currentTime = Math.max(0, v.currentTime - (sec || 10));
        },

        volumeUp: function() {
            const v = this.getVideo();
            if (!v) return;
            v.volume = Math.min(1, v.volume + 0.1);
        },

        volumeDown: function() {
            const v = this.getVideo();
            if (!v) return;
            v.volume = Math.max(0, v.volume - 0.1);
        },

        getCurrentTime: function() {
            const v = this.getVideo();
            return v ? v.currentTime : 0;
        },

        getDuration: function() {
            const v = this.getVideo();
            return v ? v.duration || 0 : 0;
        },

        // For subtitle sync
        pollTime: function() {
            const v = this.getVideo();
            return v ? { current: v.currentTime, duration: v.duration || 0, paused: v.paused } : null;
        }
    };
})();
