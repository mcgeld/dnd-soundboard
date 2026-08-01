using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
/// TopMost translucent WPF Modal Window hosting the 3D Wheel Channel Assignment Wizard with support for Stem and Preset choices.
/// </summary>
public class StemAssignmentWindow : Window
{
    private readonly TextBlock _headerBadgeText;
    private readonly TextBlock _stepTitleText;
    private readonly TextBlock _subtitleText;
    private readonly WheelPickerControl _wheelPicker;
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

    public StemAssignmentWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        Focusable = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;

        double screenWidth = SystemParameters.PrimaryScreenWidth;
        double screenHeight = SystemParameters.PrimaryScreenHeight;
        Left = (screenWidth - 440) / 2;
        Top = (screenHeight - 270) / 2;

        _containerBorder = new Border
        {
            Width = 440,
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0x0F, 0x11, 0x1B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0x47, 0x57)),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(20, 14, 20, 14),
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
            Text = "CHANNEL 1 ASSIGNMENT",
            FontWeight = FontWeights.Bold,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x81)),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        mainStack.Children.Add(_headerBadgeText);

        // Step Title
        _stepTitleText = new TextBlock
        {
            Text = "Select Assignment Type",
            FontWeight = FontWeights.Bold,
            FontSize = 19,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 2)
        };
        mainStack.Children.Add(_stepTitleText);

        // Subtitle Instructions
        _subtitleText = new TextBlock
        {
            Text = "Move Slider 1 to rotate | Oper: Confirm | Mute: Back",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8)
        };
        mainStack.Children.Add(_subtitleText);

        // Divider
        var divider = new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            Margin = new Thickness(0, 0, 0, 6)
        };
        mainStack.Children.Add(divider);

        // 3D Wheel Picker Control
        _wheelPicker = new WheelPickerControl();
        mainStack.Children.Add(_wheelPicker);

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

    public void UpdateWizard(StemAssignmentWizard wizard)
    {
        int chNum = wizard.TargetChannelIndex + 1;
        _headerBadgeText.Text = $"CHANNEL {chNum} ASSIGNMENT";
        _subtitleText.Text = $"Move Slider {chNum} to rotate wheel | Oper: Confirm | Mute: Back/Cancel";

        if (wizard.CurrentStep == AssignmentStep.ModeChoice)
        {
            _stepTitleText.Text = "Select Assignment Type";
            var options = new List<string> { "Stem", "Preset" };
            _wheelPicker.RenderWheel(options, wizard.SelectedModeIndex);
        }
        else if (wizard.CurrentStep == AssignmentStep.CategorySelection)
        {
            _stepTitleText.Text = "Select Category";
            var categoryNames = wizard.Categories.Select(c => c.Name).ToList();
            _wheelPicker.RenderWheel(categoryNames, wizard.SelectedCategoryIndex);
        }
        else if (wizard.CurrentStep == AssignmentStep.StemSelection)
        {
            string catName = wizard.CurrentCategory?.Name ?? "Category";
            _stepTitleText.Text = $"Select Stem ({catName})";
            var stemNames = wizard.CurrentStems.Select(s => s.Name).ToList();
            _wheelPicker.RenderWheel(stemNames, wizard.SelectedStemIndex);
        }
        else if (wizard.CurrentStep == AssignmentStep.PresetSelection)
        {
            _stepTitleText.Text = "Select Preset";
            var presetLabels = wizard.Presets.Select(p => $"{p.Name} ({p.ChannelSnapshots.Count} ch)").ToList();
            _wheelPicker.RenderWheel(presetLabels, wizard.SelectedPresetIndex);

            var selPreset = wizard.SelectedPreset;
            if (selPreset != null)
            {
                int count = selPreset.ChannelSnapshots.Count;
                int startCh = wizard.TargetChannelIndex + 1;
                int endCh = startCh + count - 1;

                if (endCh <= 8)
                {
                    _subtitleText.Text = $"{count} channel(s) -> Occupies Ch {startCh} to {endCh} | Oper: Confirm | Mute: Back";
                    _subtitleText.Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF));
                }
                else
                {
                    int truncated = endCh - 8;
                    _subtitleText.Text = $"{count} channel(s) -> Occupies Ch {startCh} to 8 ({truncated} truncated!) | Oper: Confirm";
                    _subtitleText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x47, 0x57));
                }
            }
        }
    }

    private Helpers.DisplayMonitorInfo? _targetMonitor;

    public void SetTargetMonitor(Helpers.DisplayMonitorInfo monitor)
    {
        _targetMonitor = monitor;
        PositionOnMonitor();
    }

    private void PositionOnMonitor()
    {
        if (_targetMonitor != null)
        {
            Left = _targetMonitor.Bounds.Left + (_targetMonitor.Bounds.Width - 440) / 2;
            Top = _targetMonitor.Bounds.Top + (_targetMonitor.Bounds.Height - 270) / 2;
        }
        else
        {
            double screenWidth = SystemParameters.PrimaryScreenWidth;
            double screenHeight = SystemParameters.PrimaryScreenHeight;
            Left = (screenWidth - 440) / 2;
            Top = (screenHeight - 270) / 2;
        }
    }

    public void ShowWindow()
    {
        PositionOnMonitor();

        BeginAnimation(OpacityProperty, null);
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
