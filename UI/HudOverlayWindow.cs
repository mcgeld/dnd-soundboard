using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using SoundBoard.Helpers;
using SoundBoard.Models;

namespace SoundBoard.UI;

/// <summary>
/// Frameless, ultralight translucent Topmost Channel Overview HUD overlay displaying Vertical Master Fader on left + Track Knobs stacked top-to-bottom on right.
/// </summary>
public class HudOverlayWindow : Window
{
    private readonly TextBlock _channelBadgeText;
    private readonly TextBlock _categoryTagText;
    private readonly TextBlock _muteStatusText;
    private readonly Border _muteStatusPill;
    private readonly StackPanel _mainStackPanel;
    private readonly Border _containerBorder;

    private DisplayMonitorInfo? _targetMonitor;

    #region Win32 Acrylic Blur API
    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);
    #endregion

    public HudOverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        Focusable = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;

        _containerBorder = new Border
        {
            Width = 480,
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0x0F, 0x11, 0x1B)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99)),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(18, 14, 18, 14),
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                Direction = 270,
                ShadowDepth = 4,
                BlurRadius = 18,
                Opacity = 0.35
            }
        };

        _mainStackPanel = new StackPanel();

        // 1. Header Row (Channel Tag + Category Badge + Mute Status Pill)
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleStack = new StackPanel { Orientation = Orientation.Horizontal };

        _channelBadgeText = new TextBlock
        {
            Text = "CHANNEL 1  ●  THUNDERSTORM",
            FontWeight = FontWeights.ExtraBold,
            FontSize = 13,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        };
        titleStack.Children.Add(_channelBadgeText);

        _categoryTagText = new TextBlock
        {
            Text = "WEATHER",
            FontWeight = FontWeights.Bold,
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99)),
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        titleStack.Children.Add(_categoryTagText);

        Grid.SetColumn(titleStack, 0);

        _muteStatusPill = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 2, 8, 2),
            VerticalAlignment = VerticalAlignment.Center
        };
        _muteStatusText = new TextBlock
        {
            FontWeight = FontWeights.Bold,
            FontSize = 10
        };
        _muteStatusPill.Child = _muteStatusText;
        Grid.SetColumn(_muteStatusPill, 1);

        headerGrid.Children.Add(titleStack);
        headerGrid.Children.Add(_muteStatusPill);

        _mainStackPanel.Children.Add(headerGrid);

        _containerBorder.Child = _mainStackPanel;
        Content = _containerBorder;

        Visibility = Visibility.Hidden;

        Loaded += (s, e) => EnableAcrylicBlur();
    }

    private void EnableAcrylicBlur()
    {
        try
        {
            var windowHelper = new WindowInteropHelper(this);
            var accent = new AccentPolicy
            {
                AccentState = 4,
                GradientColor = (0x22 << 24) | 0x1A1012
            };

            int accentSize = Marshal.SizeOf(accent);
            IntPtr accentPtr = Marshal.AllocHGlobal(accentSize);
            Marshal.StructureToPtr(accent, accentPtr, false);

            var data = new WindowCompositionAttributeData
            {
                Attribute = 19,
                SizeOfData = accentSize,
                Data = accentPtr
            };

            SetWindowCompositionAttribute(windowHelper.Handle, ref data);
            Marshal.FreeHGlobal(accentPtr);
        }
        catch { }
    }

    public void UpdateChannelOverview(
        int channelIndex,
        Channel channel,
        string activeControl = "", // "fader", "knob_0", "knob_1", "knob_2", or ""
        bool isFaderDirty = false,
        bool isFaderMoving = false,
        bool[]? isKnobDirty = null,
        bool[]? isKnobMoving = null,
        float[]? lastFaderVol = null,
        float[][]? lastKnobVol = null)
    {
        int chNum = channelIndex + 1;
        var stem = channel.LoadedStem;
        string stemName = stem?.Name ?? "Unassigned";
        string catName = stem?.CategoryName ?? "UNASSIGNED";

        _channelBadgeText.Text = $"CHANNEL {chNum}  ●  {stemName.ToUpper()}";
        _categoryTagText.Text = catName.ToUpper();

        // Mute Status Pill
        if (channel.IsMuted)
        {
            _muteStatusPill.Background = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0x47, 0x57));
            _muteStatusPill.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x47, 0x57));
            _muteStatusPill.BorderThickness = new Thickness(1);
            _muteStatusText.Text = "MUTED";
            _muteStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x47, 0x57));
        }
        else
        {
            _muteStatusPill.Background = new SolidColorBrush(Color.FromArgb(0x44, 0x34, 0xD3, 0x99));
            _muteStatusPill.BorderBrush = new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99));
            _muteStatusPill.BorderThickness = new Thickness(1);
            _muteStatusText.Text = "ACTIVE";
            _muteStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99));
        }

        // Clear dynamic control rows (keep header row at index 0)
        while (_mainStackPanel.Children.Count > 1)
        {
            _mainStackPanel.Children.RemoveAt(1);
        }

        // Separator
        _mainStackPanel.Children.Add(new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            Margin = new Thickness(0, 10, 0, 10)
        });

        // 2. DENSE 2-COLUMN MIXER STRIP LAYOUT (Left: Vertical Master Fader | Right: Knob Rows 1..3 Top-to-Bottom)
        var bodyGrid = new Grid();
        bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Left Vertical Master
        bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Right Knob Stack

        // LEFT COLUMN: VERTICAL MASTER FADER
        float faderHwVal = (lastFaderVol != null && lastFaderVol.Length > channelIndex) ? lastFaderVol[channelIndex] : channel.MasterVolume;
        float? faderGhostVal = isFaderDirty ? channel.MasterVolume : null;
        bool isFaderActive = activeControl == "fader";

        var masterVerticalColumn = CreateVerticalMasterFader(
            chNum,
            hardwareValue: isFaderDirty ? faderHwVal : channel.MasterVolume,
            audioGhostValue: faderGhostVal,
            isDirty: isFaderDirty,
            isMoving: isFaderMoving,
            isActiveControl: isFaderActive
        );
        Grid.SetColumn(masterVerticalColumn, 0);
        bodyGrid.Children.Add(masterVerticalColumn);

        // RIGHT COLUMN: TRACK KNOB ROWS (Knob 1 Top, Knob 2 Middle, Knob 3 Bottom)
        var knobStack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Stretch
        };

        string[] knobLabels = new string[] { "Knob 3 (Bottom)", "Knob 2 (Middle)", "Knob 1 (Top)" };
        int trackCount = stem?.Tracks.Count ?? 0;

        // Render from Top to Bottom matching control board layout (t = 2 -> Top, t = 1 -> Mid, t = 0 -> Bot)
        for (int t = 2; t >= 0; t--)
        {
            bool isKnobDirtyVal = (isKnobDirty != null && isKnobDirty.Length > t) ? isKnobDirty[t] : false;
            bool isKnobMovingVal = (isKnobMoving != null && isKnobMoving.Length > t) ? isKnobMoving[t] : false;
            float knobHwVal = (lastKnobVol != null && lastKnobVol.Length > channelIndex && lastKnobVol[channelIndex].Length > t) ? lastKnobVol[channelIndex][t] : channel.TrackVolumes[t];
            float? knobGhostVal = isKnobDirtyVal ? channel.TrackVolumes[t] : null;
            bool isKnobActive = activeControl == $"knob_{t}";

            if (t < trackCount && stem != null && stem.Tracks.Count > t)
            {
                var track = stem.Tracks[t];
                var trackRow = CreateVolumeRow(
                    labelText: knobLabels[t],
                    subLabelText: $"Track {t + 1}",
                    titleText: Path.GetFileNameWithoutExtension(track.FileName),
                    hardwareValue: isKnobDirtyVal ? knobHwVal : channel.TrackVolumes[t],
                    audioGhostValue: knobGhostVal,
                    isDirty: isKnobDirtyVal,
                    isMoving: isKnobMovingVal,
                    isActiveControl: isKnobActive
                );
                knobStack.Children.Add(trackRow);
            }
            else
            {
                // Unassigned / Empty Slot Row
                var emptyRow = CreateEmptyRow(knobLabels[t]);
                knobStack.Children.Add(emptyRow);
            }
        }

        Grid.SetColumn(knobStack, 1);
        bodyGrid.Children.Add(knobStack);

        _mainStackPanel.Children.Add(bodyGrid);
    }

    private Border CreateVerticalMasterFader(
        int channelNumber,
        float hardwareValue,
        float? audioGhostValue,
        bool isDirty,
        bool isMoving,
        bool isActiveControl)
    {
        Color accentColor = isDirty ? Color.FromRgb(0xF5, 0x9E, 0x0B) : Color.FromRgb(0x81, 0x8C, 0xF8); // Amber or Indigo

        var outerBorder = new Border
        {
            Width = 64,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(6, 8, 6, 8),
            Margin = new Thickness(0, 2, 12, 4),
            Background = isActiveControl
                ? new SolidColorBrush(Color.FromArgb(0x44, accentColor.R, accentColor.G, accentColor.B))
                : new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)),
            BorderBrush = isActiveControl
                ? new SolidColorBrush(accentColor)
                : new SolidColorBrush(Color.FromArgb(0x33, accentColor.R, accentColor.G, accentColor.B)),
            BorderThickness = new Thickness(isActiveControl ? 1.5 : 1)
        };

        var mainGrid = new Grid();
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Vertical Progress Bar
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                      // Bottom Text

        // 1. VERTICAL DUAL PROGRESS BAR
        var progressContainer = new Border
        {
            Width = 10,
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 8),
            ClipToBounds = true
        };

        var verticalDualGrid = new Grid();

        // Layer 0: Primary Hardware Vertical Bar (Row 0 = Empty Top, Row 1 = Filled Bottom)
        var hwGrid = new Grid();
        float filledWeight = Math.Clamp(hardwareValue, 0.001f, 1.0f);
        float emptyWeight = 1.0f - hardwareValue;

        hwGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(emptyWeight, GridUnitType.Star) });
        hwGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(filledWeight, GridUnitType.Star) });

        var hwFillBar = new Border
        {
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(accentColor),
            VerticalAlignment = VerticalAlignment.Stretch
        };
        Grid.SetRow(hwFillBar, 1);
        hwGrid.Children.Add(hwFillBar);
        verticalDualGrid.Children.Add(hwGrid);

        // Layer 1: Ghost Current Audio Vertical Bar (Renders ON TOP)
        if (audioGhostValue.HasValue)
        {
            var ghostGrid = new Grid();
            float ghostFilled = Math.Clamp(audioGhostValue.Value, 0.001f, 1.0f);
            float ghostEmpty = 1.0f - audioGhostValue.Value;

            ghostGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(ghostEmpty, GridUnitType.Star) });
            ghostGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(ghostFilled, GridUnitType.Star) });

            var ghostFillBar = new Border
            {
                CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(0, 2, 0, 0), // Top marker line for vertical bar
                VerticalAlignment = VerticalAlignment.Stretch
            };
            Grid.SetRow(ghostFillBar, 1);
            ghostGrid.Children.Add(ghostFillBar);
            verticalDualGrid.Children.Add(ghostGrid);
        }

        progressContainer.Child = verticalDualGrid;
        Grid.SetRow(progressContainer, 0);
        mainGrid.Children.Add(progressContainer);

        // 2. BOTTOM TEXT BLOCK (Percentage + Vertical Rotated "MASTER" Label)
        var bottomStack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center
        };

        int hwPct = (int)Math.Round(hardwareValue * 100);
        var pctBlock = new TextBlock
        {
            Text = $"{hwPct}%",
            FontWeight = FontWeights.Bold,
            FontSize = 11,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 2)
        };
        bottomStack.Children.Add(pctBlock);

        var rotText = new TextBlock
        {
            Text = "MASTER",
            FontWeight = FontWeights.ExtraBold,
            FontSize = 9,
            Foreground = new SolidColorBrush(accentColor),
            HorizontalAlignment = HorizontalAlignment.Center,
            LayoutTransform = new RotateTransform(-90)
        };
        bottomStack.Children.Add(rotText);

        Grid.SetRow(bottomStack, 1);
        mainGrid.Children.Add(bottomStack);

        outerBorder.Child = mainGrid;
        return outerBorder;
    }

    private Border CreateVolumeRow(
        string labelText,
        string subLabelText,
        string titleText,
        float hardwareValue,
        float? audioGhostValue,
        bool isDirty,
        bool isMoving,
        bool isActiveControl)
    {
        Color accentColor = isDirty ? Color.FromRgb(0xF5, 0x9E, 0x0B) : Color.FromRgb(0x34, 0xD3, 0x99); // Amber or Emerald

        var rowBorder = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 2, 0, 4),
            Background = isActiveControl
                ? new SolidColorBrush(Color.FromArgb(0x44, accentColor.R, accentColor.G, accentColor.B))
                : new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)),
            BorderBrush = isActiveControl
                ? new SolidColorBrush(accentColor)
                : new SolidColorBrush(Color.FromArgb(0x33, accentColor.R, accentColor.G, accentColor.B)),
            BorderThickness = new Thickness(isActiveControl ? 1.5 : 1)
        };

        var rowStack = new StackPanel();

        // Top Info Line (Label + Track Title)
        var infoGrid = new Grid();
        infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleStack = new StackPanel { Orientation = Orientation.Horizontal };

        var labelBlock = new TextBlock
        {
            Text = labelText.ToUpper(),
            FontWeight = FontWeights.Bold,
            FontSize = 10,
            Foreground = new SolidColorBrush(accentColor),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        titleStack.Children.Add(labelBlock);

        var titleBlock = new TextBlock
        {
            Text = titleText,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        titleStack.Children.Add(titleBlock);
        Grid.SetColumn(titleStack, 0);

        int hwPct = (int)Math.Round(hardwareValue * 100);
        string pctString = audioGhostValue.HasValue
            ? $"{hwPct}% (Audio: {(int)Math.Round(audioGhostValue.Value * 100)}%)"
            : $"{hwPct}%";

        var pctBlock = new TextBlock
        {
            Text = pctString,
            FontWeight = FontWeights.Bold,
            FontSize = 11,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(pctBlock, 1);

        infoGrid.Children.Add(titleStack);
        infoGrid.Children.Add(pctBlock);
        rowStack.Children.Add(infoGrid);

        // Dual Track Bar
        var progressBgContainer = new Border
        {
            Height = 8,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF)),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 6, 0, 0),
            ClipToBounds = true
        };

        var dualStackGrid = new Grid();

        // 1. PRIMARY HARDWARE BAR (Layer 0)
        var progressGrid = new Grid();
        float filledWeight = Math.Clamp(hardwareValue, 0.001f, 1.0f);
        float emptyWeight = 1.0f - hardwareValue;

        var filledCol = new ColumnDefinition { Width = new GridLength(filledWeight, GridUnitType.Star) };
        var emptyCol = new ColumnDefinition { Width = new GridLength(emptyWeight, GridUnitType.Star) };
        progressGrid.ColumnDefinitions.Add(filledCol);
        progressGrid.ColumnDefinitions.Add(emptyCol);

        var fillBar = new Border
        {
            Height = 8,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(accentColor),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        Grid.SetColumn(fillBar, 0);
        progressGrid.Children.Add(fillBar);
        dualStackGrid.Children.Add(progressGrid);

        // 2. GHOST CURRENT AUDIO BAR (Layer 1 - Renders ON TOP)
        if (audioGhostValue.HasValue)
        {
            var ghostGrid = new Grid();
            float ghostFilled = Math.Clamp(audioGhostValue.Value, 0.001f, 1.0f);
            float ghostEmpty = 1.0f - audioGhostValue.Value;

            var ghostFilledCol = new ColumnDefinition { Width = new GridLength(ghostFilled, GridUnitType.Star) };
            var ghostEmptyCol = new ColumnDefinition { Width = new GridLength(ghostEmpty, GridUnitType.Star) };
            ghostGrid.ColumnDefinitions.Add(ghostFilledCol);
            ghostGrid.ColumnDefinitions.Add(ghostEmptyCol);

            var ghostFillBar = new Border
            {
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(0, 0, 2, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            Grid.SetColumn(ghostFillBar, 0);
            ghostGrid.Children.Add(ghostFillBar);
            dualStackGrid.Children.Add(ghostGrid);
        }

        progressBgContainer.Child = dualStackGrid;
        rowStack.Children.Add(progressBgContainer);

        rowBorder.Child = rowStack;
        return rowBorder;
    }

    private Border CreateEmptyRow(string labelText)
    {
        var rowBorder = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 2, 0, 4),
            Background = new SolidColorBrush(Color.FromArgb(0x0C, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1)
        };

        var infoGrid = new Grid();
        infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleStack = new StackPanel { Orientation = Orientation.Horizontal };

        var labelBlock = new TextBlock
        {
            Text = labelText.ToUpper(),
            FontWeight = FontWeights.Bold,
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        titleStack.Children.Add(labelBlock);

        var titleBlock = new TextBlock
        {
            Text = "Unassigned Slot",
            FontWeight = FontWeights.Medium,
            FontSize = 12,
            FontStyle = FontStyles.Italic,
            Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
            VerticalAlignment = VerticalAlignment.Center
        };
        titleStack.Children.Add(titleBlock);
        Grid.SetColumn(titleStack, 0);

        var pctBlock = new TextBlock
        {
            Text = "--",
            FontWeight = FontWeights.Bold,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(pctBlock, 1);

        infoGrid.Children.Add(titleStack);
        infoGrid.Children.Add(pctBlock);

        rowBorder.Child = infoGrid;
        return rowBorder;
    }

    public void SetTargetMonitor(DisplayMonitorInfo monitor)
    {
        _targetMonitor = monitor;
        PositionOnMonitor();
    }

    private void PositionOnMonitor()
    {
        if (_targetMonitor != null)
        {
            Left = _targetMonitor.Bounds.Left + (_targetMonitor.Bounds.Width - 480) / 2;
            Top = _targetMonitor.Bounds.Top + 50;
        }
        else
        {
            double screenWidth = SystemParameters.PrimaryScreenWidth;
            Left = (screenWidth - 480) / 2;
            Top = 50;
        }
    }

    public void ShowHud()
    {
        PositionOnMonitor();

        BeginAnimation(OpacityProperty, null);
        Opacity = 1.0;
        Topmost = true;
        Visibility = Visibility.Visible;
        Show();
    }

    public void HideHud()
    {
        var anim = new DoubleAnimation
        {
            From = Opacity,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(180)
        };
        anim.Completed += (s, e) =>
        {
            Visibility = Visibility.Hidden;
        };
        BeginAnimation(OpacityProperty, anim);
    }
}
