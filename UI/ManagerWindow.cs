using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ListBox = System.Windows.Controls.ListBox;
using TabControl = System.Windows.Controls.TabControl;
using CheckBox = System.Windows.Controls.CheckBox;
using Button = System.Windows.Controls.Button;
using Orientation = System.Windows.Controls.Orientation;
using System.Windows.Media;
using SoundBoard.Helpers;
using SoundBoard.Models;
using SoundBoard.Services;

namespace SoundBoard.UI;

/// <summary>
/// TopMost Glassmorphic WPF Management Window for Audio Library Folders, Files, Presets, and Settings.
/// </summary>
public class ManagerWindow : Window
{
    private readonly AudioEngine _audioEngine;
    private readonly AudioLibraryService _libraryService;
    private readonly PresetStorageService _presetStorage;
    private readonly HudService _hudService;
    private readonly Action _onLibraryUpdated;

    private List<Category> _categories;

    private TabControl? _tabControl;
    private ListBox? _musicFoldersList;
    private ListBox? _stemFoldersList;
    private ListBox? _tracksList;
    private ListBox? _presetsList;
    private TextBlock? _presetDetailsText;
    private CheckBox? _startupCheckBox;

    private Stem? _selectedStem;
    private NAudio.Wave.WaveOutEvent? _previewPlayer;
    private NAudio.Wave.WaveStream? _previewReader;

    public ManagerWindow(
        AudioEngine audioEngine,
        AudioLibraryService libraryService,
        PresetStorageService presetStorage,
        HudService hudService,
        List<Category> categories,
        Action onLibraryUpdated)
    {
        _audioEngine = audioEngine;
        _libraryService = libraryService;
        _presetStorage = presetStorage;
        _hudService = hudService;
        _categories = categories;
        _onLibraryUpdated = onLibraryUpdated;

        Title = "TTRPG SoundBoard - Audio & Preset Manager";
        Width = 720;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x12, 0x14, 0x20));
        Foreground = System.Windows.Media.Brushes.White;
        Topmost = true;

        var mainGrid = new Grid { Margin = new Thickness(16) };
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Header Title
        var headerPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        headerPanel.Children.Add(new TextBlock
        {
            Text = "TTRPG SOUNDBOARD MANAGER",
            FontWeight = FontWeights.Bold,
            FontSize = 18,
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x47, 0x57))
        });
        headerPanel.Children.Add(new TextBlock
        {
            Text = "Manage audio folders, files, presets, and Windows startup settings.",
            FontSize = 12,
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x9C, 0xA3, 0xAF))
        });
        mainGrid.Children.Add(headerPanel);
        Grid.SetRow(headerPanel, 0);

        // Tab Control
        _tabControl = new TabControl
        {
            Background = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = System.Windows.Media.Brushes.White
        };

        // Tab 1: Audio Library Manager
        var libraryTab = new TabItem
        {
            Header = "🎵 Audio Library",
            Content = CreateAudioLibraryTab()
        };
        _tabControl.Items.Add(libraryTab);

        // Tab 2: Presets Manager
        var presetsTab = new TabItem
        {
            Header = "📋 Presets",
            Content = CreatePresetsTab()
        };
        _tabControl.Items.Add(presetsTab);

        // Tab 3: Settings
        var settingsTab = new TabItem
        {
            Header = "⚙️ Settings",
            Content = CreateSettingsTab()
        };
        _tabControl.Items.Add(settingsTab);

        mainGrid.Children.Add(_tabControl);
        Grid.SetRow(_tabControl, 1);

        Content = mainGrid;

        RefreshLibraryLists();
        RefreshPresetsList();

        Closing += (s, e) => StopPreviewAudio();
    }

    private UIElement CreateAudioLibraryTab()
    {
        var grid = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Left Column: Music & Stem Folders
        var foldersPanel = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };

        foldersPanel.Children.Add(new TextBlock { Text = "Music Folders (./audio/Music/)", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4), Foreground = System.Windows.Media.Brushes.White });
        _musicFoldersList = new ListBox
        {
            Height = 130,
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1B, 0x1E, 0x2E)),
            Foreground = System.Windows.Media.Brushes.White,
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x33, 0x38, 0x4F))
        };
        _musicFoldersList.SelectionChanged += (s, e) => OnFolderSelected(_musicFoldersList);
        foldersPanel.Children.Add(_musicFoldersList);

        var btnPanel1 = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 12) };
        var addMusicBtn = new System.Windows.Controls.Button { Content = "+ New Music Folder", Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(8, 4, 8, 4) };
        addMusicBtn.Click += (s, e) => CreateNewFolder("./audio/Music");
        btnPanel1.Children.Add(addMusicBtn);
        foldersPanel.Children.Add(btnPanel1);

        foldersPanel.Children.Add(new TextBlock { Text = "Stem Folders (./audio/Stems/)", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4), Foreground = System.Windows.Media.Brushes.White });
        _stemFoldersList = new ListBox
        {
            Height = 130,
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1B, 0x1E, 0x2E)),
            Foreground = System.Windows.Media.Brushes.White,
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x33, 0x38, 0x4F))
        };
        _stemFoldersList.SelectionChanged += (s, e) => OnFolderSelected(_stemFoldersList);
        foldersPanel.Children.Add(_stemFoldersList);

        var btnPanel2 = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        var addStemBtn = new System.Windows.Controls.Button { Content = "+ New Stem Folder", Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(8, 4, 8, 4) };
        addStemBtn.Click += (s, e) => CreateNewFolder("./audio/Stems");
        btnPanel2.Children.Add(addStemBtn);
        foldersPanel.Children.Add(btnPanel2);

        grid.Children.Add(foldersPanel);
        Grid.SetColumn(foldersPanel, 0);

        // Right Column: Folder Audio Files & Audio Preview Player
        var rightPanel = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
        rightPanel.Children.Add(new TextBlock { Text = "Tracks in Selected Folder (Max 3)", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4), Foreground = System.Windows.Media.Brushes.White });

        _tracksList = new ListBox
        {
            Height = 220,
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1B, 0x1E, 0x2E)),
            Foreground = System.Windows.Media.Brushes.White,
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x33, 0x38, 0x4F))
        };
        rightPanel.Children.Add(_tracksList);

        var previewPanel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var playBtn = new System.Windows.Controls.Button { Content = "▶ Play Preview", Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(10, 4, 10, 4) };
        playBtn.Click += (s, e) => PlaySelectedTrackPreview();
        previewPanel.Children.Add(playBtn);

        var stopBtn = new System.Windows.Controls.Button { Content = "⏹ Stop", Padding = new Thickness(10, 4, 10, 4) };
        stopBtn.Click += (s, e) => StopPreviewAudio();
        previewPanel.Children.Add(stopBtn);
        rightPanel.Children.Add(previewPanel);

        grid.Children.Add(rightPanel);
        Grid.SetColumn(rightPanel, 1);

        return grid;
    }

    private UIElement CreatePresetsTab()
    {
        var grid = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var leftPanel = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        leftPanel.Children.Add(new TextBlock { Text = "Saved Presets", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4), Foreground = System.Windows.Media.Brushes.White });

        _presetsList = new ListBox
        {
            Height = 300,
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1B, 0x1E, 0x2E)),
            Foreground = System.Windows.Media.Brushes.White,
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x33, 0x38, 0x4F))
        };
        _presetsList.SelectionChanged += (s, e) => OnPresetSelected();
        leftPanel.Children.Add(_presetsList);

        var btnPanel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var deletePresetBtn = new System.Windows.Controls.Button { Content = "🗑️ Delete Selected Preset", Padding = new Thickness(10, 4, 10, 4) };
        deletePresetBtn.Click += (s, e) => DeleteSelectedPreset();
        btnPanel.Children.Add(deletePresetBtn);
        leftPanel.Children.Add(btnPanel);

        grid.Children.Add(leftPanel);
        Grid.SetColumn(leftPanel, 0);

        var rightPanel = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
        rightPanel.Children.Add(new TextBlock { Text = "Preset Details & Stem Snapshots", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4), Foreground = System.Windows.Media.Brushes.White });

        _presetDetailsText = new TextBlock
        {
            Height = 300,
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1B, 0x1E, 0x2E)),
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x9C, 0xA3, 0xAF)),
            Padding = new Thickness(8),
            TextWrapping = TextWrapping.Wrap,
            Text = "Select a preset from the left to view channel stem allocations."
        };
        rightPanel.Children.Add(_presetDetailsText);

        grid.Children.Add(rightPanel);
        Grid.SetColumn(rightPanel, 1);

        return grid;
    }

    private UIElement CreateSettingsTab()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };

        _startupCheckBox = new CheckBox
        {
            Content = "Run TTRPG SoundBoard automatically when Windows starts",
            IsChecked = StartupManager.IsStartupEnabled,
            Foreground = System.Windows.Media.Brushes.White,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 16)
        };
        _startupCheckBox.Click += (s, e) =>
        {
            bool enable = _startupCheckBox.IsChecked ?? false;
            StartupManager.SetStartup(enable);
        };
        stack.Children.Add(_startupCheckBox);

        var monPanel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
        monPanel.Children.Add(new TextBlock { Text = "Target HUD Display Monitor: ", Foreground = System.Windows.Media.Brushes.White, VerticalAlignment = VerticalAlignment.Center });
        var cycleMonBtn = new System.Windows.Controls.Button { Content = $"Monitor {_hudService.TargetMonitorIndex + 1} / {_hudService.MonitorCount} (Click to Cycle)", Padding = new Thickness(10, 4, 10, 4) };
        cycleMonBtn.Click += (s, e) =>
        {
            int nextIdx = _hudService.ShowOrCycleTargetMonitor();
            cycleMonBtn.Content = $"Monitor {nextIdx + 1} / {_hudService.MonitorCount} (Click to Cycle)";
        };
        monPanel.Children.Add(cycleMonBtn);
        stack.Children.Add(monPanel);

        return stack;
    }

    private void RefreshLibraryLists()
    {
        _categories = _libraryService.ScanAudioLibrary("./audio");
        _onLibraryUpdated?.Invoke();

        var musicCat = _categories.FirstOrDefault(c => c.Name.Equals("Music", StringComparison.OrdinalIgnoreCase));
        var stemCat = _categories.FirstOrDefault(c => c.Name.Equals("Stems", StringComparison.OrdinalIgnoreCase) || c.Name.Equals("Stem", StringComparison.OrdinalIgnoreCase));

        if (_musicFoldersList != null) _musicFoldersList.ItemsSource = musicCat?.Stems.Select(s => s.Name).ToList();
        if (_stemFoldersList != null) _stemFoldersList.ItemsSource = stemCat?.Stems.Select(s => s.Name).ToList();
    }

    private void OnFolderSelected(ListBox listBox)
    {
        if (listBox.SelectedItem == null) return;
        string folderName = listBox.SelectedItem.ToString()!;

        if (_musicFoldersList != null && _stemFoldersList != null)
        {
            if (listBox == _musicFoldersList) _stemFoldersList.UnselectAll();
            else _musicFoldersList.UnselectAll();
        }

        string category = listBox == _musicFoldersList ? "Music" : "Stems";
        var catObj = _categories.FirstOrDefault(c => c.Name.Equals(category, StringComparison.OrdinalIgnoreCase));
        _selectedStem = catObj?.Stems.FirstOrDefault(s => s.Name.Equals(folderName, StringComparison.OrdinalIgnoreCase));

        if (_tracksList != null)
        {
            if (_selectedStem != null)
            {
                _tracksList.ItemsSource = _selectedStem.Tracks.Select(t => t.FileName).ToList();
            }
            else
            {
                _tracksList.ItemsSource = null;
            }
        }
    }

    private void CreateNewFolder(string parentPath)
    {
        string inputName = Microsoft.VisualBasic.Interaction.InputBox("Enter new folder name:", "New Audio Folder", "New_Category");
        if (string.IsNullOrWhiteSpace(inputName)) return;

        string targetPath = Path.Combine(parentPath, inputName.Trim());
        if (!Directory.Exists(targetPath))
        {
            Directory.CreateDirectory(targetPath);
            System.Windows.MessageBox.Show($"Created folder: {targetPath}", "Folder Created", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            RefreshLibraryLists();
        }
    }

    private void PlaySelectedTrackPreview()
    {
        if (_tracksList?.SelectedItem == null || _selectedStem == null) return;
        string trackFileName = _tracksList.SelectedItem.ToString()!;
        var trackObj = _selectedStem.Tracks.FirstOrDefault(t => t.FileName.Equals(trackFileName, StringComparison.OrdinalIgnoreCase));

        if (trackObj != null && File.Exists(trackObj.FilePath))
        {
            StopPreviewAudio();
            try
            {
                string ext = Path.GetExtension(trackObj.FilePath).ToLowerInvariant();
                if (ext == ".ogg")
                {
                    _previewReader = new NAudio.Vorbis.VorbisWaveReader(trackObj.FilePath);
                }
                else
                {
                    _previewReader = new NAudio.Wave.AudioFileReader(trackObj.FilePath);
                }

                _previewPlayer = new NAudio.Wave.WaveOutEvent();
                _previewPlayer.Init(_previewReader);
                _previewPlayer.Play();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Preview playback failed: {ex.Message}", "Playback Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
    }

    private void StopPreviewAudio()
    {
        try
        {
            _previewPlayer?.Stop();
            _previewPlayer?.Dispose();
            _previewReader?.Dispose();
        }
        catch { }
        _previewPlayer = null;
        _previewReader = null;
    }

    private void RefreshPresetsList()
    {
        var presets = _presetStorage.GetAlphabetizedPresets();
        if (_presetsList != null) _presetsList.ItemsSource = presets.Select(p => p.Name).ToList();
    }

    private void OnPresetSelected()
    {
        if (_presetsList?.SelectedItem == null || _presetDetailsText == null) return;
        string presetName = _presetsList.SelectedItem.ToString()!;
        var preset = _presetStorage.GetAlphabetizedPresets().FirstOrDefault(p => p.Name.Equals(presetName, StringComparison.OrdinalIgnoreCase));

        if (preset != null)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Preset Name: {preset.Name}");
            sb.AppendLine($"Saved Channels: {preset.ChannelSnapshots.Count}");
            sb.AppendLine();
            for (int i = 0; i < preset.ChannelSnapshots.Count; i++)
            {
                var snap = preset.ChannelSnapshots[i];
                sb.AppendLine($"Channel {i + 1}: Stem [{snap.StemName}] ({snap.CategoryName})");
                sb.AppendLine($"  - Master Volume: {(int)Math.Round(snap.MasterVolume * 100)}%");
            }
            _presetDetailsText.Text = sb.ToString();
        }
    }

    private void DeleteSelectedPreset()
    {
        if (_presetsList?.SelectedItem == null) return;
        string presetName = _presetsList.SelectedItem.ToString()!;
        var res = System.Windows.MessageBox.Show($"Delete preset '{presetName}' permanently?", "Confirm Delete", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
        if (res == System.Windows.MessageBoxResult.Yes)
        {
            _presetStorage.DeletePreset(presetName);
            RefreshPresetsList();
            if (_presetDetailsText != null) _presetDetailsText.Text = "Preset deleted.";
        }
    }
}
