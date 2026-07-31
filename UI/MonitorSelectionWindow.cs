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
/// TopMost Glassmorphism HUD overlay displayed when cycling target display monitors using the Launch Control XL Device button (Note 104).
/// </summary>
public class MonitorSelectionWindow : Window
{
    private readonly TextBlock _headerTagText;
    private readonly TextBlock _titleText;
    private readonly TextBlock _detailText;
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

    public MonitorSelectionWindow()
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
            Width = 440,
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0x0F, 0x11, 0x1B)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99)), // Emerald accent
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(20, 14, 20, 14),
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

        // Header Tag
        _headerTagText = new TextBlock
        {
            Text = "MONITOR 1 OF 3  ●  DEVICE SELECTION",
            FontWeight = FontWeights.Bold,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99)),
            Margin = new Thickness(0, 0, 0, 4)
        };
        mainStack.Children.Add(_headerTagText);

        // Title
        _titleText = new TextBlock
        {
            Text = "HUD Target Display Selected",
            FontWeight = FontWeights.Bold,
            FontSize = 17,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 4)
        };
        mainStack.Children.Add(_titleText);

        // Detail / Resolution
        _detailText = new TextBlock
        {
            Text = "1920 × 1080 (Primary Monitor)",
            FontWeight = FontWeights.Medium,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF))
        };
        mainStack.Children.Add(_detailText);

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

    public void UpdateDisplay(DisplayMonitorInfo monitor, int totalMonitors)
    {
        string primaryTag = monitor.IsPrimary ? "PRIMARY" : "SECONDARY";
        _headerTagText.Text = $"MONITOR {monitor.Index + 1} OF {totalMonitors}  ●  {primaryTag}";
        _titleText.Text = $"HUD Target Display Selected";
        _detailText.Text = $"{monitor.Bounds.Width:F0} × {monitor.Bounds.Height:F0} ({monitor.DeviceName})";

        Left = monitor.Bounds.Left + (monitor.Bounds.Width - 440) / 2;
        Top = monitor.Bounds.Top + 60;
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
            Duration = TimeSpan.FromMilliseconds(180)
        };
        anim.Completed += (s, e) =>
        {
            Visibility = Visibility.Hidden;
        };
        BeginAnimation(OpacityProperty, anim);
    }
}
