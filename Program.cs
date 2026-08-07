using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using SoundBoard.Models;
using SoundBoard.Services;
using SoundBoard.UI;

namespace SoundBoard;

class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        try
        {
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

            // 1. Initialize WPF Application with explicit shutdown mode
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

            // 2. Scan audio library
            var libraryService = new AudioLibraryService();
            var presetStorage = new PresetStorageService();
            var categories = libraryService.ScanAudioLibrary("./audio");

        // 3. Initialize Core Audio, HUD, and MIDI Services
        using var audioEngine = new AudioEngine();
        using var hudService = new HudService();
        using var midiService = new MidiHardwareService(audioEngine, hudService, presetStorage);

        midiService.SetCategories(categories);

        // 4. Initialize Windows System Tray Service
        using var trayService = new SystemTrayService();

        ManagerWindow? managerWindow = null;

        // Tray Service Action Handlers
        trayService.OnOpenManagerRequested += () =>
        {
            app.Dispatcher.Invoke(() =>
            {
                if (managerWindow == null || !managerWindow.IsLoaded)
                {
                    managerWindow = new ManagerWindow(audioEngine, libraryService, presetStorage, hudService, categories, () =>
                    {
                        var freshCategories = libraryService.ScanAudioLibrary("./audio");
                        midiService.SetCategories(freshCategories);
                    });
                    managerWindow.Show();
                }
                else
                {
                    managerWindow.Activate();
                }
            });
        };

        trayService.OnRescanRequested += () =>
        {
            var freshCategories = libraryService.ScanAudioLibrary("./audio");
            midiService.SetCategories(freshCategories);
            int count = freshCategories.Sum(c => c.Stems.Count);
            trayService.ShowNotification("Audio Library Rescanned", $"Found {count} Music & Stem folders in ./audio.");
        };

        trayService.OnExitRequested += () =>
        {
            System.Windows.Forms.Application.Exit();
        };

        // 5. Connect MIDI controller (runs background hot-plug timer)
        midiService.Start("Launch Control");

        trayService.ShowNotification(
            "TTRPG SoundBoard Active",
            "Running in System Tray. Plug in your Launch Control XL at any time!"
        );

        // 6. Start Windows Application Event Loop for System Tray Service
        System.Windows.Forms.Application.Run();
        }
        catch (Exception ex)
        {
            File.WriteAllText("./crash.log", ex.ToString());
        }
    }
}
