using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using SoundBoard.Helpers;

namespace SoundBoard.UI;

/// <summary>
/// Full-screen TopMost visual highlight window displaying a glowing border frame and prominent hero monitor number badge when cycling target displays.
/// </summary>
public class MonitorSelectionWindow : Window
{
    private readonly TextBlock _numberBadgeText;
    private readonly TextBlock _headerTagText;
    private readonly TextBlock _titleText;
    private readonly TextBlock _detailText;
    private readonly Border _screenBorder;

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

    public MonitorSelectionWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        Focusable = false;
        WindowStartupLocation = WindowStartupLocation.Manual;

        // Full-screen outer border framing the entire monitor
        _screenBorder = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = new SolidColorBrush(Color.FromArgb(0x18, 0x0F, 0x11, 0x1B)), // Subtle 10% dark glass tint
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99)),       // Glowing Emerald border
            BorderThickness = new Thickness(6)
        };

        // Center Hero Card Container
        var centerGrid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var heroCard = new Border
        {
            Width = 480,
            Background = new SolidColorBrush(Color.FromArgb(0xEE, 0x0F, 0x11, 0x1B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xAA, 0x34, 0xD3, 0x99)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(24, 20, 24, 20),
            Effect = new DropShadowEffect
            {
                Color = Color.FromRgb(0x34, 0xD3, 0x99),
                Direction = 270,
                ShadowDepth = 0,
                BlurRadius = 32,
                Opacity = 0.5
            }
        };

        var cardStack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center
        };

        // Big Giant Monitor Number Badge
        _numberBadgeText = new TextBlock
        {
            Text = "1",
            FontWeight = FontWeights.ExtraBold,
            FontSize = 76,
            Foreground = new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, -8),
            Effect = new DropShadowEffect
            {
                Color = Color.FromRgb(0x34, 0xD3, 0x99),
                BlurRadius = 16,
                ShadowDepth = 0,
                Opacity = 0.8
            }
        };
        cardStack.Children.Add(_numberBadgeText);

        // Header Tag
        _headerTagText = new TextBlock
        {
            Text = "MONITOR 1 OF 3  ●  PRIMARY",
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4)
        };
        cardStack.Children.Add(_headerTagText);

        // Title
        _titleText = new TextBlock
        {
            Text = "HUD TARGET DISPLAY SELECTED",
            FontWeight = FontWeights.Bold,
            FontSize = 18,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 6)
        };
        cardStack.Children.Add(_titleText);

        // Detail / Resolution
        _detailText = new TextBlock
        {
            Text = "1920 × 1080 (Primary Monitor)",
            FontWeight = FontWeights.Medium,
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        cardStack.Children.Add(_detailText);

        heroCard.Child = cardStack;
        centerGrid.Children.Add(heroCard);
        _screenBorder.Child = centerGrid;

        Content = _screenBorder;

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

    public void UpdateDisplay(DisplayMonitorInfo monitor, int totalMonitors)
    {
        string primaryTag = monitor.IsPrimary ? "PRIMARY" : "SECONDARY";
        _numberBadgeText.Text = $"{monitor.Index + 1}";
        _headerTagText.Text = $"MONITOR {monitor.Index + 1} OF {totalMonitors}  ●  {primaryTag}";
        _detailText.Text = $"{monitor.Bounds.Width:F0} × {monitor.Bounds.Height:F0} ({monitor.DeviceName})";

        // Span full monitor bounds
        Left = monitor.Bounds.Left;
        Top = monitor.Bounds.Top;
        Width = monitor.Bounds.Width;
        Height = monitor.Bounds.Height;
    }

    public void ShowWindow()
    {
        BeginAnimation(OpacityProperty, null);

        Opacity = 1.0;
        Topmost = true;
        Visibility = Visibility.Visible;
        Show();
    }

    public void HideWindow()
    {
        var anim = new DoubleAnimation
        {
            From = Opacity,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(300)
        };
        anim.Completed += (s, e) =>
        {
            Visibility = Visibility.Hidden;
        };
        BeginAnimation(OpacityProperty, anim);
    }
}
