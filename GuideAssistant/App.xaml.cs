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
            if (repo.GetDefaultProfile() != null) return;

            var profile = new Models.HotkeyProfile
            {
                Name = "默认方案",
                IsDefault = true
            };
            var id = repo.AddProfile(profile);
            repo.SaveBindings(id, new()
            {
                new() { ActionName = "play_pause", ActionDisplay = "播放/暂停" },
                new() { ActionName = "fast_forward", ActionDisplay = "快进" },
                new() { ActionName = "fast_backward", ActionDisplay = "快退" },
                new() { ActionName = "volume_up", ActionDisplay = "音量+" },
                new() { ActionName = "volume_down", ActionDisplay = "音量-" },
                new() { ActionName = "toggle_visibility", ActionDisplay = "隐藏/显示窗口" },
                new() { ActionName = "bookmark_page", ActionDisplay = "收藏页面" },
                new() { ActionName = "toggle_subtitle", ActionDisplay = "字幕切换" },
                new() { ActionName = "toggle_minimap", ActionDisplay = "小地图切换" },
            });
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            _window = Services.GetRequiredService<MainWindow>();
            _window.Activate();
        }
    }
}
