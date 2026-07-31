using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using SoundBoard.Models;

namespace SoundBoard.UI;

/// <summary>
/// Frameless, ultralight translucent Topmost HUD overlay with Windows DWM Acrylic Glass Blur and ~20% opacity.
/// </summary>
public class HudOverlayWindow : Window
{
    private readonly TextBlock _channelBadgeText;
    private readonly TextBlock _categoryTagText;
    private readonly TextBlock _stemTitleText;
    private readonly StackPanel _tracksStackPanel;
    private readonly Border _containerBorder;

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

        // Position near top-center of primary screen
        double screenWidth = SystemParameters.PrimaryScreenWidth;
        Left = (screenWidth - 440) / 2;
        Top = 60;

        // Ultralight Translucent Glassmorphism Container (Opacity ~20%)
        _containerBorder = new Border
        {
            Width = 440,
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0x0F, 0x11, 0x1B)), // ~20% ultralight translucent glass tint
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0x81, 0x8C, 0xF8)), // Subtle glowing indigo border
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(22, 16, 22, 16),
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                Direction = 270,
                ShadowDepth = 4,
                BlurRadius = 16,
                Opacity = 0.3
            }
        };

        var mainStack = new StackPanel();

        // Header Row (Channel Badge + Category Pill Tag)
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _channelBadgeText = new TextBlock
        {
            Text = "CHANNEL 1",
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0xC7, 0xD2, 0xFE)),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(_channelBadgeText, 0);

        _categoryTagText = new TextBlock
        {
            Text = "Weather",
            FontWeight = FontWeights.Bold,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x93, 0xC5, 0xFD)),
            Background = new SolidColorBrush(Color.FromArgb(0x44, 0x1E, 0x40, 0xAF)),
            Padding = new Thickness(8, 3, 8, 3),
            VerticalAlignment = VerticalAlignment.Center
        };
        var categoryBorder = new Border
        {
            CornerRadius = new CornerRadius(6),
            Child = _categoryTagText
        };
        Grid.SetColumn(categoryBorder, 1);

        headerGrid.Children.Add(_channelBadgeText);
        headerGrid.Children.Add(categoryBorder);
        mainStack.Children.Add(headerGrid);

        // Stem Title
        _stemTitleText = new TextBlock
        {
            Text = "Thunderstorm",
            FontWeight = FontWeights.Bold,
            FontSize = 22,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 6, 0, 10),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        mainStack.Children.Add(_stemTitleText);

        // Divider Line
        var divider = new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            Margin = new Thickness(0, 0, 0, 10)
        };
        mainStack.Children.Add(divider);

        // Track Allocation Stack
        _tracksStackPanel = new StackPanel();
        mainStack.Children.Add(_tracksStackPanel);

        _containerBorder.Child = mainStack;
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
                AccentState = 4, // ACCENT_ENABLE_ACRYLICBLURBEHIND (Windows 10/11 Glass Blur)
                GradientColor = (0x22 << 24) | 0x1A1012 // 0x22 Ultralight Alpha ARGB Gradient
            };

            int accentSize = Marshal.SizeOf(accent);
            IntPtr accentPtr = Marshal.AllocHGlobal(accentSize);
            Marshal.StructureToPtr(accent, accentPtr, false);

            var data = new WindowCompositionAttributeData
            {
                Attribute = 19, // WCA_ACCENT_POLICY
                SizeOfData = accentSize,
                Data = accentPtr
            };

            SetWindowCompositionAttribute(windowHelper.Handle, ref data);
            Marshal.FreeHGlobal(accentPtr);
        }
        catch { }
    }

    public void UpdateDisplay(int channelIndex, Stem? stem)
    {
        _channelBadgeText.Text = $"CHANNEL {channelIndex + 1}";

        if (stem == null)
        {
            _categoryTagText.Text = "Unassigned";
            _stemTitleText.Text = "No Stem Loaded";
            _tracksStackPanel.Children.Clear();

            var emptyText = new TextBlock
            {
                Text = "This channel is currently unassigned.",
                FontStyle = FontStyles.Italic,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0xBD, 0xC3, 0xC7))
            };
            _tracksStackPanel.Children.Add(emptyText);
            return;
        }

        _categoryTagText.Text = stem.CategoryName;
        _stemTitleText.Text = stem.Name;
        _tracksStackPanel.Children.Clear();

        int trackCount = stem.Tracks.Count;
        if (trackCount == 0)
        {
            var noTracksText = new TextBlock
            {
                Text = "No tracks in this stem.",
                FontStyle = FontStyles.Italic,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0xBD, 0xC3, 0xC7))
            };
            _tracksStackPanel.Children.Add(noTracksText);
            return;
        }

        // Display Top-to-Bottom matching physical knob order on board: Knob 1 (Top), Knob 2 (Middle), Knob 3 (Bottom)
        // Allocation rule is bottom-to-top: Track 0 = Knob 3 (Bottom), Track 1 = Knob 2 (Middle), Track 2 = Knob 1 (Top)
        for (int knobIndex = 0; knobIndex < 3; knobIndex++)
        {
            int knobNumber = knobIndex + 1; // 1 = Top, 2 = Middle, 3 = Bottom
            string knobLabel = knobNumber == 1 ? "Knob 1 (Top)" : knobNumber == 2 ? "Knob 2 (Middle)" : "Knob 3 (Bottom)";
            int trackIdx = 3 - knobNumber;  // Map Knob 3 -> Track 0, Knob 2 -> Track 1, Knob 1 -> Track 2

            var rowGrid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            if (trackIdx < trackCount)
            {
                string displayName = Path.GetFileNameWithoutExtension(stem.Tracks[trackIdx].FileName);

                var knobText = new TextBlock
                {
                    Text = $"● {knobLabel}:",
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99)) // Emerald green dial indicator
                };
                var trackText = new TextBlock
                {
                    Text = displayName,
                    FontSize = 12,
                    Foreground = Brushes.White,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Grid.SetColumn(knobText, 0);
                Grid.SetColumn(trackText, 1);
                rowGrid.Children.Add(knobText);
                rowGrid.Children.Add(trackText);
            }
            else
            {
                var knobText = new TextBlock
                {
                    Text = $"○ {knobLabel}:",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)) // OFF dial indicator
                };
                var trackText = new TextBlock
                {
                    Text = "Unassigned",
                    FontSize = 12,
                    FontStyle = FontStyles.Italic,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF))
                };
                Grid.SetColumn(knobText, 0);
                Grid.SetColumn(trackText, 1);
                rowGrid.Children.Add(knobText);
                rowGrid.Children.Add(trackText);
            }

            _tracksStackPanel.Children.Add(rowGrid);
        }
    }

    public void ShowHud()
    {
        double screenWidth = SystemParameters.PrimaryScreenWidth;
        Left = (screenWidth - 440) / 2;
        Top = 60;

        Opacity = 0;
        Topmost = true;
        Visibility = Visibility.Visible;
        Show();

        var anim = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(180)
        };
        BeginAnimation(OpacityProperty, anim);
    }

    public void HideHud()
    {
        var anim = new DoubleAnimation
        {
            From = Opacity,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(250)
        };
        anim.Completed += (s, e) =>
        {
            Visibility = Visibility.Hidden;
        };
        BeginAnimation(OpacityProperty, anim);
    }
}
