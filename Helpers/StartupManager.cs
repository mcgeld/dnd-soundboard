using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace SoundBoard.Helpers;

/// <summary>
/// Manages Windows Startup auto-launch registration via Registry HKCU CurrentVersion\Run.
/// </summary>
public static class StartupManager
{
    private const string AppName = "DnDSoundBoard";
    private const string RegistryRunPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsStartupEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryRunPath, writable: false);
                string? val = key?.GetValue(AppName) as string;
                return !string.IsNullOrEmpty(val);
            }
            catch
            {
                return false;
            }
        }
    }

    public static void SetStartup(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryRunPath, writable: true);
            if (key == null) return;

            if (enable)
            {
                string exePath = Process.GetCurrentProcess().MainModule?.FileName 
                    ?? Path.GetFullPath("SoundBoard.exe");
                key.SetValue(AppName, $"\"{exePath}\"");
                Console.WriteLine($"[StartupManager] Enabled Windows Startup: \"{exePath}\"");
            }
            else
            {
                key.DeleteValue(AppName, false);
                Console.WriteLine("[StartupManager] Disabled Windows Startup.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[StartupManager Error] Failed to update startup registry: {ex.Message}");
        }
    }
}
