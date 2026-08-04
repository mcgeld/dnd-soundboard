using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using SoundBoard.Helpers;
using SoundBoard.Models;

namespace SoundBoard.UI;

/// <summary>
/// Glassmorphism TopMost Window displaying live Global Master Volume level (Track Select ▲ / ▼ buttons).
/// </summary>
public class MasterVolumeWindow : Window
{
    private readonly TextBlock _titleText;
    private readonly TextBlock _volumePercentText;
    private readonly Border _barContainer;
    private readonly Border _barFill;
    private readonly TextBlock _subtitleText;
    private readonly Border _containerBorder;

    private DisplayMonitorInfo? _targetMonitor;
    private const double MaxBarWidth = 320.0;

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

    public MasterVolumeWindow()
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
            Width = 420,
            Background = new SolidColorBrush(Color.FromArgb(0xEE, 0x0F, 0x11, 0x1B)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x70, 0xA1, 0xFF)), // Electric Cyan/Blue Accent
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(22, 16, 22, 18),
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                Direction = 270,
                ShadowDepth = 4,
                BlurRadius = 18,
                Opacity = 0.4
            }
        };

        var mainStack = new StackPanel();

        // 1. Header Title Badge
        _titleText = new TextBlock
        {
            Text = "🔊 MASTER VOLUME",
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0x70, 0xA1, 0xFF)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 6)
        };

        // 2. Large Volume Percentage Text
        _volumePercentText = new TextBlock
        {
            Text = "100%",
            FontSize = 32,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 10)
        };

        // 3. Glowing Progress Bar Container
        _barContainer = new Border
        {
            Width = MaxBarWidth,
            Height = 14,
            CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 10),
            ClipToBounds = true
        };

        // Gradient Fill for Volume Bar (Cyan -> Purple)
        var grad = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5)
        };
        grad.GradientStops.Add(new GradientStop(Color.FromRgb(0x70, 0xA1, 0xFF), 0.0));
        grad.GradientStops.Add(new GradientStop(Color.FromRgb(0xA2, 0x9B, 0xFE), 1.0));

        _barFill = new Border
        {
            Width = MaxBarWidth * (1.0 / 1.5), // Default 100% of 150% max
            Height = 14,
            CornerRadius = new CornerRadius(7),
            Background = grad,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        _barContainer.Child = _barFill;

        // 4. Subtitle Text
        _subtitleText = new TextBlock
        {
            Text = "Hold ▲ / ▼ Track Select sideboard buttons to adjust",
            FontSize = 11,
            FontStyle = FontStyles.Italic,
            Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xAB, 0xBA)),
            HorizontalAlignment = HorizontalAlignment.Center
        };

        mainStack.Children.Add(_titleText);
        mainStack.Children.Add(_volumePercentText);
        mainStack.Children.Add(_barContainer);
        mainStack.Children.Add(_subtitleText);

        _containerBorder.Child = mainStack;
        Content = _containerBorder;

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        EnableAcrylicBlur();
    }

    private void EnableAcrylicBlur()
    {
        try
        {
            var windowHelper = new WindowInteropHelper(this);
            var accent = new AccentPolicy
            {
                AccentState = 3, // ENABLE_BLURBEHIND / ACRYLIC
                GradientColor = unchecked((int)0x99000000)
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
        catch
        {
            // Fallback to solid WPF background
        }
    }

    public void SetTargetMonitor(DisplayMonitorInfo monitor)
    {
        _targetMonitor = monitor;
        UpdatePosition();
    }

    public void UpdateMasterVolume(float globalMasterVolume)
    {
        int pct = (int)Math.Round(globalMasterVolume * 100.0f);
        _volumePercentText.Text = $"{pct}%";

        // Boost Indicator color if over 100%
        if (pct > 100)
        {
            _volumePercentText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xA5, 0x02)); // Vivid Amber Boost
            _titleText.Text = "🔊 MASTER VOLUME (BOOSTED)";
            _titleText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xA5, 0x02));
        }
        else
        {
            _volumePercentText.Foreground = Brushes.White;
            _titleText.Text = "🔊 MASTER VOLUME";
            _titleText.Foreground = new SolidColorBrush(Color.FromRgb(0x70, 0xA1, 0xFF));
        }

        // Scale bar fill width relative to 150% max (1.5f)
        double ratio = Math.Clamp(globalMasterVolume / 1.5f, 0.0, 1.0);
        _barFill.Width = MaxBarWidth * ratio;

        UpdatePosition();
        if (!IsVisible) Show();
    }

    private void UpdatePosition()
    {
        if (_targetMonitor == null)
        {
            double screenWidth = SystemParameters.PrimaryScreenWidth;
            double screenHeight = SystemParameters.PrimaryScreenHeight;
            Left = (screenWidth - 420) / 2;
            Top = (screenHeight - 150) / 2;
            return;
        }

        Left = _targetMonitor.Bounds.Left + (_targetMonitor.Bounds.Width - 420) / 2;
        Top = _targetMonitor.Bounds.Top + (_targetMonitor.Bounds.Height - 150) / 2;
    }
}
