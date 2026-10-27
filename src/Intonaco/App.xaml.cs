using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Intonaco.Core;
using Microsoft.Win32;

namespace Intonaco
{
    public partial class App : Application
    {
        private Mutex? _mutex;
        private bool _ownsMutex;
        private ResourceDictionary? _lightThemeDictionary;
        private string _themePreference = "System";
        private const string MutexName = "Intonaco_SingleInstance_Mutex";

        protected override void OnStartup(StartupEventArgs e)
        {
            _mutex = new Mutex(true, MutexName, out bool createdNew);
            _ownsMutex = createdNew;
            if (!createdNew)
            {
                // A login-time launch (--minimized) must never put a dialog in
                // the user's face; it simply yields to the running instance.
                if (!Core.StartupManager.ShouldStartMinimized(e.Args))
                {
                    MessageBox.Show("Intonaco is already running.", "Intonaco",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                Shutdown();
                return;
            }

            // Ensure settings directory exists
            var settingsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Intonaco");
            Directory.CreateDirectory(settingsDir);

            DispatcherUnhandledException += OnDispatcherUnhandledException;

            // Apply the persisted theme (System / Light / Dark) before the
            // main window is created so first paint uses the right palette.
            var settings = AppSettings.Load();
            ApplyTheme(settings.Theme);

            // Follow Windows theme changes while preference is "System".
            try { SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged; }
            catch { /* Non-critical: theme simply will not auto-switch. */ }

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try { SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged; }
            catch { /* Non-critical. */ }

            // Only the instance that created the mutex owns it; releasing a
            // mutex owned by another process throws ApplicationException.
            if (_ownsMutex)
            {
                try { _mutex?.ReleaseMutex(); } catch { /* Already released. */ }
            }
            _mutex?.Dispose();
            base.OnExit(e);
        }

        /// <summary>
        /// Applies a theme preference ("System", "Light" or "Dark").
        /// The dark dictionary (IntonacoTheme.xaml, merged in App.xaml) provides
        /// all styles; LightTheme.xaml only overrides colors/brushes, so applying
        /// the light theme means merging it last so its brushes win lookups.
        /// </summary>
        public void ApplyTheme(string? theme)
        {
            _themePreference = string.IsNullOrWhiteSpace(theme) ? "System" : theme;

            bool useLight = _themePreference switch
            {
                "Light" => true,
                "Dark" => false,
                _ => IsSystemLightTheme()
            };

            var merged = Resources.MergedDictionaries;
            if (_lightThemeDictionary != null)
            {
                merged.Remove(_lightThemeDictionary);
                _lightThemeDictionary = null;
            }

            if (useLight)
            {
                _lightThemeDictionary = new ResourceDictionary
                {
                    Source = new Uri("Themes/LightTheme.xaml", UriKind.Relative)
                };
                merged.Add(_lightThemeDictionary);
            }
        }

        private static bool IsSystemLightTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", false);
                return key?.GetValue("AppsUseLightTheme") is int value && value == 1;
            }
            catch
            {
                return false;
            }
        }

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.General && _themePreference == "System")
            {
                Dispatcher.BeginInvoke(() => ApplyTheme("System"));
            }
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            // Never crash silently: record the failure next to the settings file
            // so it can be attached to a bug report, then keep the app alive.
            try
            {
                var logPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Intonaco", "crash.log");
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.AppendAllText(logPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {e.Exception}\n\n");
            }
            catch { /* Logging must never throw. */ }

            MessageBox.Show(
                "Intonaco hit an unexpected error but will keep running.\n\n" + e.Exception.Message,
                "Intonaco", MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Handled = true;
        }
    }
}
