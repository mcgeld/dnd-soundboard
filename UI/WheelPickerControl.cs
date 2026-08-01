using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoundBoard.UI;

/// <summary>
/// Clean, fully-visible Glassmorphism Selector Control for MIDI Fader Navigation.
/// Renders all items cleanly with zero clipping, full text visibility, and glowing selection highlight.
/// </summary>
public class WheelPickerControl : UserControl
{
    private readonly StackPanel _wheelContainer;

    public WheelPickerControl()
    {
        _wheelContainer = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top
        };

        ClipToBounds = false;
        Content = _wheelContainer;
    }

    public void RenderWheel(List<string> items, int selectedIndex)
    {
        _wheelContainer.Children.Clear();

        if (items == null || items.Count == 0)
        {
            var emptyLabel = new TextBlock
            {
                Text = "(No items available)",
                FontStyle = FontStyles.Italic,
                FontSize = 13,
                Foreground = Brushes.Gray,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 20, 0, 20)
            };
            _wheelContainer.Children.Add(emptyLabel);
            return;
        }

        selectedIndex = Math.Clamp(selectedIndex, 0, items.Count - 1);

        for (int i = 0; i < items.Count; i++)
        {
            string title = items[i];
            bool isSelected = i == selectedIndex;

            Border itemBorder;

            if (isSelected)
            {
                // GLOWING SELECTED ITEM HIGHLIGHT
                itemBorder = new Border
                {
                    Height = 34,
                    CornerRadius = new CornerRadius(8),
                    Background = new SolidColorBrush(Color.FromArgb(0x55, 0xA2, 0x9B, 0xFE)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xA2, 0x9B, 0xFE)),
                    BorderThickness = new Thickness(1.5),
                    Padding = new Thickness(14, 2, 14, 2),
                    Margin = new Thickness(0, 3, 0, 3),
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                var text = new TextBlock
                {
                    Text = title,
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                itemBorder.Child = text;
            }
            else
            {
                // UNSELECTED ITEM (Crisp & Fully Visible)
                itemBorder = new Border
                {
                    Height = 34,
                    CornerRadius = new CornerRadius(8),
                    Background = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF)),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(12, 2, 12, 2),
                    Margin = new Thickness(0, 3, 0, 3),
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                var text = new TextBlock
                {
                    Text = title,
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xC7, 0xD2, 0xFE)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                itemBorder.Child = text;
            }

            _wheelContainer.Children.Add(itemBorder);
        }
    }
}
