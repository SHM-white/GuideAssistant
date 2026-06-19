using GuideAssistant.Data;
using GuideAssistant.Services;
using GuideAssistant.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Serilog;
using System;

namespace GuideAssistant
{
    public partial class App : Application
    {
        private Window? _window;
        public static IServiceProvider Services { get; private set; } = null!;

        public App()
        {
            InitializeComponent();

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(
                    System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "GuideAssistant", "logs", "app-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7)
                .CreateLogger();

            Log.Information("Application starting");

            var services = new ServiceCollection();
            ConfigureServices(services);
            Services = services.BuildServiceProvider();

            // Initialize database
            var db = Services.GetRequiredService<Database>();
            db.Initialize();

            // Ensure default hotkey profile exists
            var hotkeyRepo = Services.GetRequiredService<HotkeyRepository>();
            EnsureDefaultHotkeys(hotkeyRepo);
        }

        private static void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<Database>();
            services.AddSingleton<BookmarkRepository>();
            services.AddSingleton<GameRepository>();
            services.AddSingleton<HotkeyRepository>();
            services.AddSingleton<WindowStateRepository>();

            services.AddSingleton<TabManager>();
            services.AddSingleton<HotkeyService>();
            services.AddSingleton<WindowManager>();
            services.AddSingleton<BookmarkService>();
            services.AddSingleton<BilibiliApi>();
            services.AddSingleton<SubtitleService>();
            services.AddSingleton<DirectionService>();
            services.AddSingleton<GameDetector>();
            services.AddSingleton<ProcessLauncher>();

            services.AddSingleton<MainWindow>();
            services.AddSingleton<ToolbarWindow>();
        }

        private static void EnsureDefaultHotkeys(HotkeyRepository repo)
        {
            var profile = repo.GetDefaultProfile();
            if (profile == null)
            {
                profile = new Models.HotkeyProfile
                {
                    Name = "默认方案",
                    IsDefault = true
                };
                profile.Id = repo.AddProfile(profile);
            }

            // Only seed defaults for actions that have no key assigned
            var defaults = new Dictionary<string, (uint vk, string display, string actionDisplay)>
            {
                ["play_pause"]        = (0xC0, "`", "播放/暂停"),
                ["fast_forward"]      = (0x36, "6", "快进"),
                ["fast_backward"]     = (0x35, "5", "快退"),
                ["volume_up"]         = (0x39, "9", "音量+"),
                ["volume_down"]       = (0x38, "8", "音量-"),
                ["toggle_visibility"] = (0x48, "H", "显示/隐藏"),
                ["bookmark_page"]     = (0x42, "B", "收藏页面"),
                ["toggle_subtitle"]   = (0x53, "S", "字幕切换"),
                ["toggle_minimap"]    = (0x4D, "M", "小地图切换"),
            };

            bool needsSave = false;
            foreach (var kv in defaults)
            {
                var binding = profile.Bindings.FirstOrDefault(b => b.ActionName == kv.Key);
                if (binding == null)
                {
                    profile.Bindings.Add(new Models.HotkeyBinding
                    {
                        ActionName = kv.Key,
                        ActionDisplay = kv.Value.actionDisplay,
                        VirtualKey = kv.Value.vk,
                        DisplayText = kv.Value.display,
                    });
                    needsSave = true;
                }
                else if (binding.VirtualKey == 0)
                {
                    binding.VirtualKey = kv.Value.vk;
                    binding.DisplayText = kv.Value.display;
                    binding.ActionDisplay = kv.Value.actionDisplay;
                    needsSave = true;
                }
            }

            if (needsSave)
                repo.SaveBindings(profile.Id, profile.Bindings);
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            _window = Services.GetRequiredService<MainWindow>();
            _window.Activate();
        }
    }
}
