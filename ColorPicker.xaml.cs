using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ValCC;

public partial class ColorPicker : UserControl
{
    private double hue;
    private double saturation;
    private double brightness;
    private bool isUpdating;

    public ColorPicker()
    {
        InitializeComponent();
        ShowColor(Colors.Black);
    }

    public event EventHandler? ColorChanged;

    public Color SelectedColor { get; private set; }

    public void ShowColor(Color color)
    {
        ReadHsv(color);
        SelectedColor = color;
        UpdateControls(updateHexBox: true);
    }

    public static string ToHex(Color color)
    {
        return $"#{color.R:x2}{color.G:x2}{color.B:x2}";
    }

    public static bool TryParseHex(string text, out Color color)
    {
        color = Colors.Black;
        string digits = text.Trim().TrimStart('#');
        if (digits.Length != 6 || !int.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int value))
        {
            return false;
        }

        color = Color.FromRgb((byte)(value >> 16), (byte)(value >> 8), (byte)value);
        return true;
    }

    private void ColorArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ColorArea.CaptureMouse();
        PickAt(e.GetPosition(ColorArea));
    }

    private void ColorArea_MouseMove(object sender, MouseEventArgs e)
    {
        if (ColorArea.IsMouseCaptured)
        {
            PickAt(e.GetPosition(ColorArea));
        }
    }

    private void ColorArea_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        ColorArea.ReleaseMouseCapture();
    }

    private void ColorArea_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        PlaceMarker();
    }

    private void HueSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (isUpdating)
        {
            return;
        }

        hue = HueSlider.Value;
        ChangeColor(ColorFromHsv(hue, saturation, brightness), updateHexBox: true);
    }

    private void HexBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (isUpdating || !TryParseHex(HexBox.Text, out Color color))
        {
            return;
        }

        ReadHsv(color);
        ChangeColor(color, updateHexBox: false);
    }

    private void HexBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        UpdateControls(updateHexBox: true);
    }

    private void HexBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            UpdateControls(updateHexBox: true);
            HexBox.SelectAll();
        }
    }

    private void PickAt(Point point)
    {
        saturation = Math.Clamp(point.X / ColorArea.ActualWidth, 0, 1);
        brightness = 1 - Math.Clamp(point.Y / ColorArea.ActualHeight, 0, 1);
        ChangeColor(ColorFromHsv(hue, saturation, brightness), updateHexBox: true);
    }

    private void ChangeColor(Color color, bool updateHexBox)
    {
        bool changed = color != SelectedColor;
        SelectedColor = color;
        UpdateControls(updateHexBox);
        if (changed)
        {
            ColorChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void UpdateControls(bool updateHexBox)
    {
        isUpdating = true;
        HueLayer.Fill = new SolidColorBrush(ColorFromHsv(hue, 1, 1));
        HueSlider.Value = hue;
        Swatch.Background = new SolidColorBrush(SelectedColor);
        RgbText.Text = $"rgb {SelectedColor.R} {SelectedColor.G} {SelectedColor.B}";
        if (updateHexBox)
        {
            HexBox.Text = ToHex(SelectedColor);
        }

        PlaceMarker();
        isUpdating = false;
    }

    private void PlaceMarker()
    {
        Canvas.SetLeft(Marker, saturation * ColorArea.ActualWidth - Marker.Width / 2);
        Canvas.SetTop(Marker, (1 - brightness) * ColorArea.ActualHeight - Marker.Height / 2);
    }

    private void ReadHsv(Color color)
    {
        double red = color.R / 255.0;
        double green = color.G / 255.0;
        double blue = color.B / 255.0;
        double max = Math.Max(red, Math.Max(green, blue));
        double min = Math.Min(red, Math.Min(green, blue));
        double delta = max - min;

        if (delta > 0)
        {
            if (max == red)
            {
                hue = 60 * ((green - blue) / delta % 6);
            }
            else if (max == green)
            {
                hue = 60 * ((blue - red) / delta + 2);
            }
            else
            {
                hue = 60 * ((red - green) / delta + 4);
            }

            if (hue < 0)
            {
                hue += 360;
            }
        }

        if (max > 0)
        {
            saturation = delta / max;
        }

        brightness = max;
    }

    private static Color ColorFromHsv(double hue, double saturation, double brightness)
    {
        double chroma = brightness * saturation;
        double section = hue % 360 / 60;
        double second = chroma * (1 - Math.Abs(section % 2 - 1));
        double match = brightness - chroma;

        (double red, double green, double blue) = (int)section switch
        {
            0 => (chroma, second, 0.0),
            1 => (second, chroma, 0.0),
            2 => (0.0, chroma, second),
            3 => (0.0, second, chroma),
            4 => (second, 0.0, chroma),
            _ => (chroma, 0.0, second)
        };

        return Color.FromRgb(ToByte(red + match), ToByte(green + match), ToByte(blue + match));
    }

    private static byte ToByte(double value)
    {
        return (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);
    }
}
