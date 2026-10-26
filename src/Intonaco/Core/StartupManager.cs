using Microsoft.Win32;
using System;
using System.Diagnostics;

namespace Intonaco.Core
{
    public static class StartupManager
    {
        private const string RegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "Intonaco";

        public static bool IsRegistered
        {
            get
            {
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, false);
                    return key?.GetValue(AppName) != null;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static void Register()
        {
            try
            {
                string exePath = Process.GetCurrentProcess().MainModule?.FileName
                    ?? throw new InvalidOperationException("Cannot determine executable path");

                using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, true);
                key?.SetValue(AppName, $"\"{exePath}\" --minimized");
            }
            catch
            {
                // Non-critical failure
            }
        }

        public static void Unregister()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, true);
                key?.DeleteValue(AppName, false);
            }
            catch
            {
                // Non-critical failure
            }
        }
    }
}
