using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using SoundBoard.Models;

namespace SoundBoard.Services;

/// <summary>
/// Service for saving, loading, and listing JSON Preset files.
/// </summary>
public class PresetStorageService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public bool SavePreset(Preset preset, string presetsDirectory = "./presets")
    {
        try
        {
            if (string.IsNullOrWhiteSpace(preset.Name))
            {
                Console.WriteLine("[PresetStorageService Warning] Cannot save preset with an empty name.");
                return false;
            }

            Directory.CreateDirectory(presetsDirectory);
            string safeFileName = SanitizeFileName(preset.Name) + ".json";
            string filePath = Path.Combine(presetsDirectory, safeFileName);

            string json = JsonSerializer.Serialize(preset, JsonOptions);
            File.WriteAllText(filePath, json);

            Console.WriteLine($"[PresetStorageService] Saved preset '{preset.Name}' to '{filePath}'.");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PresetStorageService Error] Failed to save preset '{preset.Name}': {ex.Message}");
            return false;
        }
    }

    public Preset? LoadPreset(string presetName, string presetsDirectory = "./presets")
    {
        try
        {
            string fileName = presetName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? presetName
                : SanitizeFileName(presetName) + ".json";

            string filePath = Path.Combine(presetsDirectory, fileName);

            if (!File.Exists(filePath))
            {
                Console.WriteLine($"[PresetStorageService Warning] Preset file not found: '{filePath}'.");
                return null;
            }

            string json = File.ReadAllText(filePath);
            var preset = JsonSerializer.Deserialize<Preset>(json, JsonOptions);
            if (preset != null)
            {
                Console.WriteLine($"[PresetStorageService] Loaded preset '{preset.Name}' ({preset.SavedChannels.Count} channels).");
            }
            return preset;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PresetStorageService Error] Failed to load preset '{presetName}': {ex.Message}");
            return null;
        }
    }

    public List<Preset> GetAllPresets(string presetsDirectory = "./presets")
    {
        var presets = new List<Preset>();
        try
        {
            if (!Directory.Exists(presetsDirectory))
            {
                Directory.CreateDirectory(presetsDirectory);
                return presets;
            }

            var files = Directory.GetFiles(presetsDirectory, "*.json");
            foreach (var file in files)
            {
                try
                {
                    string json = File.ReadAllText(file);
                    var preset = JsonSerializer.Deserialize<Preset>(json, JsonOptions);
                    if (preset != null)
                    {
                        presets.Add(preset);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[PresetStorageService Warning] Error reading preset file '{file}': {ex.Message}");
                }
            }

            Console.WriteLine($"[PresetStorageService] Found {presets.Count} presets in '{presetsDirectory}'.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PresetStorageService Error] Failed to list presets in '{presetsDirectory}': {ex.Message}");
        }

        return presets;
    }

    private static string SanitizeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return name;
    }
}
