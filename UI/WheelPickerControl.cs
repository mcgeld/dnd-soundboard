using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SoundBoard.UI;

/// <summary>
/// Compact custom WPF 3D rotating wheel picker control with dead-center alignment for lists of any size.
/// </summary>
public class WheelPickerControl : UserControl
{
    private readonly StackPanel _wheelContainer;
    private readonly TranslateTransform _translateTransform;
    private int _previousIndex = -1;
    private const double ItemRowHeight = 36.0;

    public WheelPickerControl()
    {
        _translateTransform = new TranslateTransform();

        _wheelContainer = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top, // Top-aligned so TranslateTransform coordinates are exact
            RenderTransform = _translateTransform
        };

        ClipToBounds = true;
        Height = 190; // Viewport height (5 full rows visible without clipping)
        Content = _wheelContainer;
    }

    public void RenderWheel(List<string> items, int selectedIndex)
    {
        _wheelContainer.Children.Clear();

        if (items == null || items.Count == 0)
        {
            _previousIndex = -1;
            _translateTransform.Y = 0;

            var emptyLabel = new TextBlock
            {
                Text = "(No items available)",
                FontStyle = FontStyles.Italic,
                FontSize = 13,
                Foreground = Brushes.Gray,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 80, 0, 0)
            };
            _wheelContainer.Children.Add(emptyLabel);
            return;
        }

        selectedIndex = Math.Clamp(selectedIndex, 0, items.Count - 1);

        for (int i = 0; i < items.Count; i++)
        {
            string title = items[i];
            bool isSelected = i == selectedIndex;
            int dist = Math.Abs(i - selectedIndex);

            Border itemBorder;

            if (isSelected)
            {
                // CENTER SELECTED ITEM (Glowing iOS Wheel Highlight)
                itemBorder = new Border
                {
                    Height = 32,
                    CornerRadius = new CornerRadius(8),
                    Background = new SolidColorBrush(Color.FromArgb(0x44, 0xA2, 0x9B, 0xFE)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xA2, 0x9B, 0xFE)),
                    BorderThickness = new Thickness(1.5),
                    Padding = new Thickness(12, 2, 12, 2),
                    Margin = new Thickness(0, 2, 0, 2),
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                var text = new TextBlock
                {
                    Text = title,
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                itemBorder.Child = text;
            }
            else
            {
                // UNSELECTED ITEM (Smooth fade for items further away)
                double opacity = dist == 1 ? 0.65 : (dist == 2 ? 0.35 : 0.18);
                double fontSize = dist == 1 ? 13 : 11;

                itemBorder = new Border
                {
                    Height = 32,
                    Padding = new Thickness(8, 2, 8, 2),
                    Margin = new Thickness(0, 2, 0, 2),
                    Opacity = opacity,
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                var text = new TextBlock
                {
                    Text = title,
                    FontSize = fontSize,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xC7, 0xD2, 0xFE)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                itemBorder.Child = text;
            }

            _wheelContainer.Children.Add(itemBorder);
        }

        // Target Y position to center selected item inside 190px viewport
        // Viewport height = 190px, Center offset = (190 / 2) - (ItemRowHeight / 2) = 95 - 18 = 77.0px
        double targetY = 77.0 - (selectedIndex * ItemRowHeight);

        if (_previousIndex < 0 || Math.Abs(selectedIndex - _previousIndex) > 3)
        {
            _translateTransform.Y = targetY;
        }
        else if (selectedIndex != _previousIndex)
        {
            var anim = new DoubleAnimation
            {
                From = _translateTransform.Y,
                To = targetY,
                Duration = TimeSpan.FromMilliseconds(130),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            _translateTransform.BeginAnimation(TranslateTransform.YProperty, anim);
        }

        _previousIndex = selectedIndex;
    }
}
