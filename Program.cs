using System;
using System.IO;
using System.Linq;
using SoundBoard.Models;
using SoundBoard.Services;

namespace SoundBoard;

class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("=================================================================");
        Console.WriteLine("  TTRPG MIDI Audio Mixer & Stem Controller (3D Wheel Wizard)");
        Console.WriteLine("=================================================================");
        Console.WriteLine();

        // 1. Scan audio library
        var libraryService = new AudioLibraryService();
        var categories = libraryService.ScanAudioLibrary("./audio");

        // 2. Initialize Audio Engine, HUD Overlay Service, and MIDI Hardware Service
        using var audioEngine = new AudioEngine();
        using var hudService = new HudService();
        using var midiService = new MidiHardwareService(audioEngine, hudService);

        // Pass scanned audio categories to MIDI service for hardware Stem Assignment Wizard
        midiService.SetCategories(categories);

        // 3. Connect to MIDI hardware and sync LEDs
        midiService.Start("Launch Control");

        // Display Active Mixer Matrix Status
        Console.WriteLine();
        Console.WriteLine("=================================================================");
        Console.WriteLine("  Audio Mixer Matrix & Hardware Control Status:");
        Console.WriteLine("=================================================================");
        for (int i = 0; i < 8; i++)
        {
            var ch = audioEngine.Channels[i];
            if (ch.LoadedStem != null)
            {
                int trackCount = ch.LoadedStem.Tracks.Count;
                Console.WriteLine($"  Channel {i}: Loaded Stem '[{ch.LoadedStem.Name}]' ({ch.LoadedStem.CategoryName})");
                Console.WriteLine($"            - Allocation: {trackCount} tracks (Bottom-to-Top Dials)");
                Console.WriteLine($"            - Dial LEDs: {trackCount} Green / {3 - trackCount} OFF");
                Console.WriteLine($"            - Mute LED: Solid Red (Unmuted) / Slow Flashing Red (Muted)");
                Console.WriteLine($"            - Operation LED: Solid Green -> Short-press: HUD | Long-press (>=600ms): 3D Wheel Wizard!");
                for (int t = 0; t < trackCount; t++)
                {
                    Console.WriteLine($"               └── Knob {3 - t} (Dial {t + 1}) -> {ch.LoadedStem.Tracks[t].FileName}");
                }
            }
            else
            {
                Console.WriteLine($"  Channel {i}: Unassigned (All LEDs OFF)");
            }
        }

        Console.WriteLine();
        Console.WriteLine("-----------------------------------------------------------------");
        Console.WriteLine(" Press 'H' : Test Show HUD Overlay manually");
        Console.WriteLine(" Press 'Q' : Exit application");
        Console.WriteLine("-----------------------------------------------------------------");

        while (true)
        {
            if (Console.IsInputRedirected)
            {
                string? line = Console.ReadLine();
                if (line == null || line.Trim().Equals("q", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("\n[System] Shutting down Audio Mixer and Engine...");
                    break;
                }
            }
            else
            {
                var keyInfo = Console.ReadKey(intercept: true);
                if (keyInfo.Key == ConsoleKey.Q)
                {
                    Console.WriteLine("\n[System] Shutting down Audio Mixer and Engine...");
                    break;
                }
                else if (keyInfo.Key == ConsoleKey.H)
                {
                    Console.WriteLine("\n[Manual Test] Triggering HUD Overlay for Channel 0...");
                    hudService.ShowChannelInfo(0, audioEngine.Channels[0].LoadedStem);
                }
            }
        }
    }
}
