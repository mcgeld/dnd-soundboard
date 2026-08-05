using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SoundBoard.Models;

namespace SoundBoard.Services;

/// <summary>
/// Scans the audio library folder hierarchy to populate in-memory Music and Stem folders.
/// </summary>
public class AudioLibraryService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".ogg", ".flac", ".m4a"
    };

    public List<Category> ScanAudioLibrary(string rootPath = "./audio")
    {
        var musicCategory = new Category { Name = "Music" };
        var stemsCategory = new Category { Name = "Stems" };

        try
        {
            if (!Directory.Exists(rootPath))
            {
                Console.WriteLine($"[AudioLibraryService] Creating root audio directory: {Path.GetFullPath(rootPath)}");
                Directory.CreateDirectory(rootPath);
            }

            string musicPath = Path.Combine(rootPath, "Music");
            string stemsPath = Path.Combine(rootPath, "Stems");

            if (!Directory.Exists(musicPath)) Directory.CreateDirectory(musicPath);
            if (!Directory.Exists(stemsPath)) Directory.CreateDirectory(stemsPath);

            // Migrate legacy root folders (e.g. ./audio/Locale or ./audio/Weather) to ./audio/Stems/
            var legacyDirs = Directory.GetDirectories(rootPath)
                .Where(d => !Path.GetFileName(d).Equals("Music", StringComparison.OrdinalIgnoreCase)
                         && !Path.GetFileName(d).Equals("Stems", StringComparison.OrdinalIgnoreCase)
                         && !Path.GetFileName(d).Equals("Stem", StringComparison.OrdinalIgnoreCase)
                         && !Path.GetFileName(d).Equals("presets", StringComparison.OrdinalIgnoreCase)
                         && !Path.GetFileName(d).StartsWith("."))
                .ToList();

            foreach (var legDir in legacyDirs)
            {
                string targetDir = Path.Combine(stemsPath, Path.GetFileName(legDir));
                if (!Directory.Exists(targetDir))
                {
                    try
                    {
                        Directory.Move(legDir, targetDir);
                        Console.WriteLine($"[AudioLibraryService] Migrated legacy folder '{Path.GetFileName(legDir)}' -> '{targetDir}'.");
                    }
                    catch { }
                }
            }

            // Populate sample Music & Stems if empty and TabletopAudio exists
            PopulateSampleMusicIfEmpty(musicPath);
            PopulateSampleStemsIfEmpty(stemsPath);

            // Scan Music folders
            ScanFolderToStems(musicPath, "Music", musicCategory.Stems);

            // Scan Stems folders
            ScanFolderToStems(stemsPath, "Stems", stemsCategory.Stems);
            string alternateStemPath = Path.Combine(rootPath, "Stem");
            if (Directory.Exists(alternateStemPath))
            {
                ScanFolderToStems(alternateStemPath, "Stems", stemsCategory.Stems);
            }

            Console.WriteLine($"[AudioLibraryService] Scanned {musicCategory.Stems.Count} Music folder(s) and {stemsCategory.Stems.Count} Stem folder(s).");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AudioLibraryService Error] Failed to scan audio directory '{rootPath}': {ex.Message}");
        }

        return new List<Category> { musicCategory, stemsCategory };
    }

    private void ScanFolderToStems(string parentPath, string categoryName, List<Stem> targetList)
    {
        if (!Directory.Exists(parentPath)) return;

        var subDirs = Directory.GetDirectories(parentPath)
            .Where(d => !Path.GetFileName(d).StartsWith("."))
            .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase);

        foreach (var subDir in subDirs)
        {
            string folderName = Path.GetFileName(subDir);

            // Check direct audio files
            var directAudioFiles = Directory.GetFiles(subDir)
                .Where(f => SupportedExtensions.Contains(Path.GetExtension(f)))
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList();

            if (directAudioFiles.Count > 0)
            {
                var stem = new Stem
                {
                    Name = folderName,
                    CategoryName = categoryName
                };
                foreach (var file in directAudioFiles)
                {
                    stem.Tracks.Add(new Track
                    {
                        FilePath = Path.GetFullPath(file),
                        FileName = Path.GetFileName(file)
                    });
                }
                targetList.Add(stem);
            }

            // Check nested subdirectories (e.g. Stems/Locale/Campfire)
            var nestedDirs = Directory.GetDirectories(subDir)
                .Where(d => !Path.GetFileName(d).StartsWith("."))
                .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase);

            foreach (var nestedDir in nestedDirs)
            {
                string nestedName = $"{folderName} - {Path.GetFileName(nestedDir)}";
                var nestedAudioFiles = Directory.GetFiles(nestedDir)
                    .Where(f => SupportedExtensions.Contains(Path.GetExtension(f)))
                    .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                    .Take(3)
                    .ToList();

                if (nestedAudioFiles.Count > 0)
                {
                    var stem = new Stem
                    {
                        Name = nestedName,
                        CategoryName = categoryName
                    };
                    foreach (var file in nestedAudioFiles)
                    {
                        stem.Tracks.Add(new Track
                        {
                            FilePath = Path.GetFullPath(file),
                            FileName = Path.GetFileName(file)
                        });
                    }
                    targetList.Add(stem);
                }
            }
        }
    }

    private void PopulateSampleMusicIfEmpty(string musicPath)
    {
        try
        {
            if (Directory.GetDirectories(musicPath).Length > 0) return;

            string tabletopMusic = "./TabletopAudio/music";
            if (!Directory.Exists(tabletopMusic)) return;

            var files = Directory.GetFiles(tabletopMusic).Where(f => SupportedExtensions.Contains(Path.GetExtension(f))).ToList();
            var groups = files.GroupBy(f =>
            {
                string fn = Path.GetFileName(f);
                int dashIdx = fn.IndexOf('-');
                return dashIdx > 0 ? fn.Substring(0, dashIdx) : "General";
            });

            foreach (var grp in groups)
            {
                string destFolder = Path.Combine(musicPath, grp.Key);
                Directory.CreateDirectory(destFolder);
                foreach (var file in grp.Take(3))
                {
                    string destFile = Path.Combine(destFolder, Path.GetFileName(file));
                    File.Copy(file, destFile, overwrite: true);
                }
            }
        }
        catch { }
    }

    private void PopulateSampleStemsIfEmpty(string stemsPath)
    {
        try
        {
            if (Directory.GetDirectories(stemsPath).Length > 0) return;

            string tabletopSounds = "./TabletopAudio/sounds";
            if (!Directory.Exists(tabletopSounds)) return;

            var files = Directory.GetFiles(tabletopSounds).Where(f => SupportedExtensions.Contains(Path.GetExtension(f))).ToList();
            var groups = files.GroupBy(f =>
            {
                string fn = Path.GetFileName(f);
                int dashIdx = fn.IndexOf('-');
                return dashIdx > 0 ? fn.Substring(0, dashIdx) : "Ambience";
            });

            foreach (var grp in groups)
            {
                string destFolder = Path.Combine(stemsPath, grp.Key);
                Directory.CreateDirectory(destFolder);
                foreach (var file in grp.Take(3))
                {
                    string destFile = Path.Combine(destFolder, Path.GetFileName(file));
                    File.Copy(file, destFile, overwrite: true);
                }
            }
        }
        catch { }
    }
}
