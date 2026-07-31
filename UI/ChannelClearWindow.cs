using System;
using System.Collections.Generic;
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
/// Glassmorphism TopMost Window displaying live multi-channel clear selection checklist.
/// </summary>
public class ChannelClearWindow : Window
{
    private readonly TextBlock _titleText;
    private readonly TextBlock _subtitleText;
    private readonly StackPanel _channelListStack;
    private readonly TextBlock _footerText;
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

    public ChannelClearWindow()
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
            Background = new SolidColorBrush(Color.FromArgb(0xEE, 0x0F, 0x11, 0x1B)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x47, 0x57)), // Red Accent
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(22, 18, 22, 18),
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                Direction = 270,
                ShadowDepth = 4,
                BlurRadius = 18,
                Opacity = 0.35
            }
        };

        var mainStack = new StackPanel();

        // 1. Header (Title + Subtitle)
        _titleText = new TextBlock
        {
            Text = "CLEAR CHANNELS MODE",
            FontWeight = FontWeights.ExtraBold,
            FontSize = 16,
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x47, 0x57)),
            Margin = new Thickness(0, 0, 0, 4)
        };
        mainStack.Children.Add(_titleText);

        _subtitleText = new TextBlock
        {
            Text = "Tap Operation buttons (1-8) to toggle channels to clear (Red = Clear).",
            FontWeight = FontWeights.Medium,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
            Margin = new Thickness(0, 0, 0, 12)
        };
        mainStack.Children.Add(_subtitleText);

        // Separator
        mainStack.Children.Add(new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            Margin = new Thickness(0, 0, 0, 12)
        });

        // 2. Channel Selection Checklist Stack
        _channelListStack = new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 12)
        };
        mainStack.Children.Add(_channelListStack);

        // 3. Footer Helper Text
        _footerText = new TextBlock
        {
            Text = "Press Mute to confirm clear  ●  Long-press Mute to CLEAR ALL",
            FontWeight = FontWeights.SemiBold,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x47, 0x57)),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        mainStack.Children.Add(_footerText);

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

    public void UpdateClearSelection(IReadOnlyList<Channel> channels, bool[] selectedFlags)
    {
        _channelListStack.Children.Clear();

        int selectedCount = 0;
        for (int i = 0; i < 8; i++)
        {
            var ch = channels[i];
            if (ch.LoadedStem == null) continue;

            bool isSelected = selectedFlags[i];
            if (isSelected) selectedCount++;

            var rowBorder = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 2, 0, 4),
                Background = isSelected
                    ? new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0x47, 0x57)) // Red tint if selected to clear
                    : new SolidColorBrush(Color.FromArgb(0x44, 0xF5, 0x9E, 0x0B)), // Amber tint if unselected (keep)
                BorderBrush = isSelected
                    ? new SolidColorBrush(Color.FromRgb(0xFF, 0x47, 0x57))
                    : new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)),
                BorderThickness = new Thickness(1)
            };

            var rowGrid = new Grid();
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var checkBlock = new TextBlock
            {
                Text = isSelected ? "[ ✕ ]" : "[   ]",
                FontWeight = FontWeights.ExtraBold,
                FontSize = 12,
                Foreground = isSelected ? new SolidColorBrush(Color.FromRgb(0xFF, 0x47, 0x57)) : new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)),
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(checkBlock, 0);
            rowGrid.Children.Add(checkBlock);

            var infoBlock = new TextBlock
            {
                Text = $"Channel {i + 1}  ●  {ch.LoadedStem.Name} ({ch.LoadedStem.CategoryName})",
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(infoBlock, 1);
            rowGrid.Children.Add(infoBlock);

            var statusBlock = new TextBlock
            {
                Text = isSelected ? "WILL CLEAR" : "KEEP",
                FontWeight = FontWeights.ExtraBold,
                FontSize = 10,
                Foreground = isSelected ? new SolidColorBrush(Color.FromRgb(0xFF, 0x47, 0x57)) : new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            Grid.SetColumn(statusBlock, 2);
            rowGrid.Children.Add(statusBlock);

            rowBorder.Child = rowGrid;
            _channelListStack.Children.Add(rowBorder);
        }

        if (_channelListStack.Children.Count == 0)
        {
            _channelListStack.Children.Add(new TextBlock
            {
                Text = "No assigned channels available to clear.",
                FontWeight = FontWeights.Medium,
                FontSize = 12,
                FontStyle = FontStyles.Italic,
                Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
                HorizontalAlignment = HorizontalAlignment.Center
            });
        }
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
            Top = _targetMonitor.Bounds.Top + 60;
        }
        else
        {
            double screenWidth = SystemParameters.PrimaryScreenWidth;
            Left = (screenWidth - 480) / 2;
            Top = 60;
        }
    }

    public void ShowWindow()
    {
        PositionOnMonitor();

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
