using CommunityToolkit.Mvvm.Messaging;
using Dapper;
using GuideAssistant.Data;
using GuideAssistant.Services;
using GuideAssistant.ViewModels;
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

            // Enable Dapper underscore → PascalCase column mapping
            // (Microsoft.Data.Sqlite GetOrdinal is case-sensitive in some versions,
            //  so action_name won't map to ActionName without this)
            DefaultTypeMap.MatchNamesWithUnderscores = true;

            Log.Information("Application starting");

            var services = new ServiceCollection();
            ConfigureServices(services);
            Services = services.BuildServiceProvider();

#if DEBUG
            // Clear database on each debug launch to avoid stale data interference
            var dbPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GuideAssistant", "data.db");
            try { System.IO.File.Delete(dbPath); Log.Information("DEBUG: Database cleared at {Path}", dbPath); }
            catch (System.IO.IOException) { /* file may be locked by a previous run */ }
#endif

            // Initialize database
            var db = Services.GetRequiredService<Database>();
            db.Initialize();

            // Ensure default hotkey profile exists (seeded from KnownActions)
            var hotkeyConfig = Services.GetRequiredService<HotkeyConfigManager>();
            hotkeyConfig.EnsureDefaultProfile();
        }

        private static void ConfigureServices(IServiceCollection services)
        {
            // Data
            services.AddSingleton<Database>();
            services.AddSingleton<BookmarkRepository>();
            services.AddSingleton<FolderRepository>();
            services.AddSingleton<GameRepository>();
            services.AddSingleton<HotkeyRepository>();
            services.AddSingleton<WindowStateRepository>();

            // Services
            services.AddSingleton<TabManager>();
            services.AddSingleton<HotkeyService>();
            services.AddSingleton<HotkeyConfigManager>();
            services.AddSingleton<WindowManager>();
            services.AddSingleton<BookmarkService>();
            services.AddSingleton<BilibiliApi>();
            services.AddSingleton<SubtitleService>();
            services.AddSingleton<DirectionService>();
            services.AddSingleton<GameDetector>();
            services.AddSingleton<ProcessLauncher>();

            // ViewModels
            services.AddSingleton<ViewModels.MainViewModel>();
            services.AddSingleton<ViewModels.ToolbarViewModel>();
            services.AddTransient<ViewModels.SettingsViewModel>(sp =>
            {
                var mainVm = sp.GetRequiredService<ViewModels.MainViewModel>();
                return new ViewModels.SettingsViewModel(
                    sp.GetRequiredService<HotkeyConfigManager>(),
                    mainVm.IsSubtitleEnabled,
                    mainVm.IsMiniMapEnabled,
                    mainVm.Opacity);
            });

            // Windows
            services.AddSingleton<MainWindow>();
            services.AddSingleton<ToolbarWindow>();
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            _window = Services.GetRequiredService<MainWindow>();
            _window.Activate();
        }
    }
}
