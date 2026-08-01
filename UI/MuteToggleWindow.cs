using System;
using System.Collections.Generic;
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
/// Glassmorphism TopMost Window displaying live scene mute toggle selection checklist (Note 107 Solo Button).
/// </summary>
public class MuteToggleWindow : Window
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

    public MuteToggleWindow()
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
            Width = 520,
            Background = new SolidColorBrush(Color.FromArgb(0xEE, 0x0F, 0x11, 0x1B)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xA2, 0x9B, 0xFE)), // Soft Purple/Violet Accent
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

        // 1. Header
        _titleText = new TextBlock
        {
            Text = "SCENE MUTE TOGGLE MODE",
            FontWeight = FontWeights.ExtraBold,
            FontSize = 16,
            Foreground = new SolidColorBrush(Color.FromRgb(0xA2, 0x9B, 0xFE)),
            Margin = new Thickness(0, 0, 0, 4)
        };
        mainStack.Children.Add(_titleText);

        _subtitleText = new TextBlock
        {
            Text = "Tap Operation buttons (1-8) to select channels to toggle mute state.",
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

        // 2. Channel Checklist Stack
        _channelListStack = new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 12)
        };
        mainStack.Children.Add(_channelListStack);

        // 3. Footer Helper Text
        _footerText = new TextBlock
        {
            Text = "Press Solo (Note 107) again to EXECUTE TOGGLE  ●  Touch control to cancel",
            FontWeight = FontWeights.SemiBold,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0xA2, 0x9B, 0xFE)),
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

    public void UpdateMuteToggleSelection(IReadOnlyList<Channel> channels, bool[] selectedFlags)
    {
        _channelListStack.Children.Clear();

        int selectedCount = 0;
        int assignedCount = 0;

        for (int i = 0; i < channels.Count; i++)
        {
            var ch = channels[i];
            if (ch.LoadedStem == null) continue;

            assignedCount++;
            bool isSelected = selectedFlags[i];
            if (isSelected) selectedCount++;

            bool currentlyMuted = ch.IsMuted;
            string currentStateStr = currentlyMuted ? "MUTED" : "AUDIBLE";
            string newStateStr = isSelected
                ? (currentlyMuted ? "Will UNMUTE (Audible)" : "Will MUTE (Silent)")
                : $"Keep {currentStateStr}";

            Color bgCol = isSelected ? Color.FromArgb(0x44, 0x00, 0xB8, 0x94) : Color.FromArgb(0x22, 0x2D, 0x34, 0x36);
            Color borderCol = isSelected ? Color.FromRgb(0x00, 0xB8, 0x94) : Color.FromRgb(0x63, 0x6E, 0x72);
            Color textCol = isSelected ? Brushes.White.Color : Color.FromRgb(0xB2, 0xBE, 0xB5);

            var rowBorder = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 3, 0, 3),
                Background = new SolidColorBrush(bgCol),
                BorderBrush = new SolidColorBrush(borderCol),
                BorderThickness = new Thickness(1)
            };

            var rowGrid = new Grid();
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Col 0: Checkbox
            var chkText = new TextBlock
            {
                Text = isSelected ? "[✓]" : "[   ]",
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                Foreground = isSelected ? new SolidColorBrush(Color.FromRgb(0x00, 0xB8, 0x94)) : Brushes.Gray,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(chkText, 0);
            rowGrid.Children.Add(chkText);

            // Col 1: Stem Title
            var titleBlock = new TextBlock
            {
                Text = $"Ch {i + 1}: {ch.LoadedStem.Name}",
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                Foreground = new SolidColorBrush(textCol),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(titleBlock, 1);
            rowGrid.Children.Add(titleBlock);

            // Col 2: Action transition
            var actionBlock = new TextBlock
            {
                Text = newStateStr,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Foreground = isSelected
                    ? (currentlyMuted ? new SolidColorBrush(Color.FromRgb(0x55, 0xEF, 0xC4)) : new SolidColorBrush(Color.FromRgb(0xFF, 0x76, 0x75)))
                    : Brushes.Gray,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(actionBlock, 2);
            rowGrid.Children.Add(actionBlock);

            rowBorder.Child = rowGrid;
            _channelListStack.Children.Add(rowBorder);
        }

        if (assignedCount == 0)
        {
            var emptyBlock = new TextBlock
            {
                Text = "(No assigned channels available on board to toggle)",
                FontStyle = FontStyles.Italic,
                FontSize = 12,
                Foreground = Brushes.Gray,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 8, 0, 8)
            };
            _channelListStack.Children.Add(emptyBlock);
        }

        _subtitleText.Text = $"Selected {selectedCount} of {assignedCount} assigned channel(s) to toggle mute state.";
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
            Left = _targetMonitor.Bounds.Left + (_targetMonitor.Bounds.Width - 520) / 2;
            Top = _targetMonitor.Bounds.Top + (_targetMonitor.Bounds.Height - 260) / 2;
        }
        else
        {
            double screenWidth = SystemParameters.PrimaryScreenWidth;
            double screenHeight = SystemParameters.PrimaryScreenHeight;
            Left = (screenWidth - 520) / 2;
            Top = (screenHeight - 260) / 2;
        }
    }

    public void ShowWindow()
    {
        PositionOnMonitor();
        Visibility = Visibility.Visible;
    }

    public void HideWindow()
    {
        Visibility = Visibility.Hidden;
    }
}
