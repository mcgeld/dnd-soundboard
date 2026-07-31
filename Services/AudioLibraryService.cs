using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SoundBoard.Models;

namespace SoundBoard.Services;

/// <summary>
/// Scans the audio library folder hierarchy to populate in-memory Category, Stem, and Track structures.
/// </summary>
public class AudioLibraryService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".ogg", ".flac", ".m4a"
    };

    public List<Category> ScanAudioLibrary(string rootPath = "./audio")
    {
        var categories = new List<Category>();

        try
        {
            if (!Directory.Exists(rootPath))
            {
                Console.WriteLine($"[AudioLibraryService] Creating root audio directory: {Path.GetFullPath(rootPath)}");
                Directory.CreateDirectory(rootPath);
                return categories;
            }

            var categoryDirectories = Directory.GetDirectories(rootPath)
                .Where(d => !Path.GetFileName(d).StartsWith(".") && !Path.GetFileName(d).Equals("presets", StringComparison.OrdinalIgnoreCase))
                .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase);

            foreach (var catDir in categoryDirectories)
            {
                var category = new Category
                {
                    Name = Path.GetFileName(catDir)
                };

                var stemDirectories = Directory.GetDirectories(catDir)
                    .Where(d => !Path.GetFileName(d).StartsWith("."))
                    .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase);

                foreach (var stemDir in stemDirectories)
                {
                    var stem = new Stem
                    {
                        Name = Path.GetFileName(stemDir),
                        CategoryName = category.Name
                    };

                    var audioFiles = Directory.GetFiles(stemDir)
                        .Where(f => SupportedExtensions.Contains(Path.GetExtension(f)))
                        .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                        .Take(3);

                    foreach (var file in audioFiles)
                    {
                        stem.Tracks.Add(new Track
                        {
                            FilePath = Path.GetFullPath(file),
                            FileName = Path.GetFileName(file)
                        });
                    }

                    category.Stems.Add(stem);
                }

                categories.Add(category);
            }

            Console.WriteLine($"[AudioLibraryService] Scanned {categories.Count} categories from '{rootPath}'.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AudioLibraryService Error] Failed to scan audio directory '{rootPath}': {ex.Message}");
        }

        return categories;
    }
}
