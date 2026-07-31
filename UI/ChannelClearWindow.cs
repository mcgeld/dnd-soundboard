using System;
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
/// TopMost translucent WPF Confirmation Window for Clear Channel operations with visual green/red action pills.
/// </summary>
public class ChannelClearWindow : Window
{
    private readonly TextBlock _headerBadgeText;
    private readonly TextBlock _stemTitleText;
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

        // Center on primary screen
        double screenWidth = SystemParameters.PrimaryScreenWidth;
        double screenHeight = SystemParameters.PrimaryScreenHeight;
        Left = (screenWidth - 440) / 2;
        Top = (screenHeight - 220) / 2;

        _containerBorder = new Border
        {
            Width = 440,
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0x0F, 0x11, 0x1B)), // ~20% translucent dark glass
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xEE, 0xFF, 0x47, 0x57)), // Glowing bright red border
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(22, 16, 22, 16),
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                Direction = 270,
                ShadowDepth = 5,
                BlurRadius = 20,
                Opacity = 0.4
            }
        };

        var mainStack = new StackPanel();

        // Header Badge Row
        _headerBadgeText = new TextBlock
        {
            Text = "CLEAR CHANNEL 1 CONFIRMATION",
            FontWeight = FontWeights.Bold,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x47, 0x57)),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        mainStack.Children.Add(_headerBadgeText);

        // Main Question Title
        var titleText = new TextBlock
        {
            Text = "Clear Channel & Unload Stem?",
            FontWeight = FontWeights.Bold,
            FontSize = 18,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 4)
        };
        mainStack.Children.Add(titleText);

        // Stem Info Title
        _stemTitleText = new TextBlock
        {
            Text = "[Thunderstorm] (Weather)",
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromRgb(0xC7, 0xD2, 0xFE)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 10)
        };
        mainStack.Children.Add(_stemTitleText);

        // Divider Line
        var divider = new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            Margin = new Thickness(0, 0, 0, 12)
        };
        mainStack.Children.Add(divider);

        // Visual Green & Red Action Button Pills (matching hardware LED colors)
        var actionButtonsGrid = new Grid();
        actionButtonsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actionButtonsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // GREEN BUTTON (Operation Button -> Confirm Clear)
        var greenPill = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(0x44, 0x34, 0xD3, 0x99)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99)),
            BorderThickness = new Thickness(1.5),
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 6, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var greenText = new TextBlock
        {
            Text = "● Confirm Clear",
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99)),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        greenPill.Child = greenText;
        Grid.SetColumn(greenPill, 0);

        // RED BUTTON (Mute Button -> Cancel)
        var redPill = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0x47, 0x57)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x47, 0x57)),
            BorderThickness = new Thickness(1.5),
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(6, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var redText = new TextBlock
        {
            Text = "● Cancel",
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x47, 0x57)),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        redPill.Child = redText;
        Grid.SetColumn(redPill, 1);

        actionButtonsGrid.Children.Add(greenPill);
        actionButtonsGrid.Children.Add(redPill);
        mainStack.Children.Add(actionButtonsGrid);

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

    public void UpdateDisplay(int channelIndex, Stem? stem)
    {
        int chNum = channelIndex + 1;
        _headerBadgeText.Text = $"CLEAR CHANNEL {chNum} CONFIRMATION";
        string stemName = stem != null ? $"[{stem.Name}] ({stem.CategoryName})" : "Unassigned Channel";
        _stemTitleText.Text = stemName;
    }

    public void ShowWindow()
    {
        double screenWidth = SystemParameters.PrimaryScreenWidth;
        double screenHeight = SystemParameters.PrimaryScreenHeight;
        Left = (screenWidth - 440) / 2;
        Top = (screenHeight - 220) / 2;

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

    public void HideWindow()
    {
        var anim = new DoubleAnimation
        {
            From = Opacity,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(200)
        };
        anim.Completed += (s, e) =>
        {
            Visibility = Visibility.Hidden;
        };
        BeginAnimation(OpacityProperty, anim);
    }
}
