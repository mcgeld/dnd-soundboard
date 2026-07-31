using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace SoundBoard.UI;

/// <summary>
/// Compact, translucent TopMost HUD overlay displaying live master fader and track knob volume adjustments
/// with dual progress bars for dirty control soft-catch transitions.
/// </summary>
public class TrackVolumeOverlayWindow : Window
{
    private readonly TextBlock _stemTagText;
    private readonly TextBlock _trackTitleText;
    private readonly TextBlock _knobLabelText;
    private readonly TextBlock _percentText;
    private readonly ColumnDefinition _filledColumn;
    private readonly ColumnDefinition _emptyColumn;
    private readonly ColumnDefinition _ghostFilledColumn;
    private readonly ColumnDefinition _ghostEmptyColumn;
    private readonly Border _fillBar;
    private readonly Border _ghostFillBar;
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

    public TrackVolumeOverlayWindow()
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
        Left = (screenWidth - 420) / 2;
        Top = 60;

        _containerBorder = new Border
        {
            Width = 420,
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0x0F, 0x11, 0x1B)), // ~20% ultralight translucent glass tint
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x88, 0x34, 0xD3, 0x99)),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(18, 12, 18, 12),
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

        // Header Tag Row
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _stemTagText = new TextBlock
        {
            Text = "THUNDERSTORM",
            FontWeight = FontWeights.Bold,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99)),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(_stemTagText, 0);

        _knobLabelText = new TextBlock
        {
            Text = "Knob 3 (Bottom)",
            FontWeight = FontWeights.SemiBold,
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(_knobLabelText, 1);

        headerGrid.Children.Add(_stemTagText);
        headerGrid.Children.Add(_knobLabelText);
        mainStack.Children.Add(headerGrid);

        // Title
        _trackTitleText = new TextBlock
        {
            Text = "Dark_Forest-strong_rain_loop",
            FontWeight = FontWeights.Bold,
            FontSize = 16,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 4, 0, 8),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        mainStack.Children.Add(_trackTitleText);

        // Volume Level Row (Percentage + Dual Progress Track)
        var volGrid = new Grid();
        volGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        volGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Background Track Bar Container (Dual Bar Overlay)
        var progressBgContainer = new Border
        {
            Height = 10,
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF)),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 12, 0),
            ClipToBounds = true
        };

        var dualStackGrid = new Grid();

        // 1. PRIMARY HARDWARE BAR (Layer 0 - Base Hardware Level)
        var progressGrid = new Grid();
        _filledColumn = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
        _emptyColumn = new ColumnDefinition { Width = new GridLength(0, GridUnitType.Star) };
        progressGrid.ColumnDefinitions.Add(_filledColumn);
        progressGrid.ColumnDefinitions.Add(_emptyColumn);

        _fillBar = new Border
        {
            Height = 10,
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99)),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        Grid.SetColumn(_fillBar, 0);
        progressGrid.Children.Add(_fillBar);
        dualStackGrid.Children.Add(progressGrid);

        // 2. GHOST CURRENT AUDIO BAR (Layer 1 - Renders ON TOP with translucent white fill and crisp white tick line)
        var ghostGrid = new Grid();
        _ghostFilledColumn = new ColumnDefinition { Width = new GridLength(0, GridUnitType.Star) };
        _ghostEmptyColumn = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
        ghostGrid.ColumnDefinitions.Add(_ghostFilledColumn);
        ghostGrid.ColumnDefinitions.Add(_ghostEmptyColumn);

        _ghostFillBar = new Border
        {
            Height = 10,
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)), // Translucent white overlay
            BorderBrush = Brushes.White,                                                // Crisp white marker line
            BorderThickness = new Thickness(0, 0, 2, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Visibility = Visibility.Collapsed
        };
        Grid.SetColumn(_ghostFillBar, 0);
        ghostGrid.Children.Add(_ghostFillBar);
        dualStackGrid.Children.Add(ghostGrid);

        progressBgContainer.Child = dualStackGrid;
        Grid.SetColumn(progressBgContainer, 0);

        _percentText = new TextBlock
        {
            Text = "100%",
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 55
        };
        Grid.SetColumn(_percentText, 1);

        volGrid.Children.Add(progressBgContainer);
        volGrid.Children.Add(_percentText);
        mainStack.Children.Add(volGrid);

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

    public void UpdateVolumeDisplay(string tagText, string titleText, string controlLabel, float hardwareVolumePercent, bool isMasterVolume = false, float? audioVolumePercent = null)
    {
        _stemTagText.Text = tagText.ToUpper();
        _trackTitleText.Text = Path.GetFileNameWithoutExtension(titleText);
        _knobLabelText.Text = controlLabel;

        // Accent Colors: Amber (#F59E0B) for Dirty Soft-Catch, Indigo (#818CF8) for Master Fader, Emerald (#34D399) for Track Dial
        Color accentColor;
        if (audioVolumePercent.HasValue)
        {
            accentColor = Color.FromRgb(0xF5, 0x9E, 0x0B); // Amber for Dirty Soft-Catch
        }
        else if (isMasterVolume)
        {
            accentColor = Color.FromRgb(0x81, 0x8C, 0xF8); // Indigo for Master
        }
        else
        {
            accentColor = Color.FromRgb(0x34, 0xD3, 0x99); // Emerald for Track Dial
        }

        _stemTagText.Foreground = new SolidColorBrush(accentColor);
        _fillBar.Background = new SolidColorBrush(accentColor);
        _containerBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x88, accentColor.R, accentColor.G, accentColor.B));

        int hwPct = (int)Math.Round(hardwareVolumePercent * 100);

        if (audioVolumePercent.HasValue)
        {
            int audioPct = (int)Math.Round(audioVolumePercent.Value * 100);
            _percentText.Text = $"{hwPct}%  (Audio: {audioPct}%)";

            _ghostFillBar.Visibility = Visibility.Visible;
            float ghostFilled = Math.Clamp(audioVolumePercent.Value, 0.001f, 1.0f);
            float ghostEmpty = 1.0f - audioVolumePercent.Value;
            _ghostFilledColumn.Width = new GridLength(ghostFilled, GridUnitType.Star);
            _ghostEmptyColumn.Width = new GridLength(ghostEmpty, GridUnitType.Star);
        }
        else
        {
            _percentText.Text = $"{hwPct}%";
            _ghostFillBar.Visibility = Visibility.Collapsed;
        }

        float filledWeight = Math.Clamp(hardwareVolumePercent, 0.001f, 1.0f);
        float emptyWeight = 1.0f - hardwareVolumePercent;

        _filledColumn.Width = new GridLength(filledWeight, GridUnitType.Star);
        _emptyColumn.Width = new GridLength(emptyWeight, GridUnitType.Star);
    }

    public void ShowOverlay()
    {
        double screenWidth = SystemParameters.PrimaryScreenWidth;
        Left = (screenWidth - 420) / 2;
        Top = 60;

        BeginAnimation(OpacityProperty, null);

        Opacity = 1.0;
        Topmost = true;
        Visibility = Visibility.Visible;
        Show();
    }

    public void HideOverlay()
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
