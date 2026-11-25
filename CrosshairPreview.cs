using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ValCC;

public static class CrosshairPreview
{
    private const double FloorStart = 0.7;
    private const int SmallestScale = 2;
    private const int LargestScale = 12;

    private record Piece(Rect Area, double Opacity);

    public static int ChooseScale(double width, double height, params JsonObject?[] crosshairs)
    {
        double extent = 2;
        foreach (JsonObject? crosshair in crosshairs)
        {
            extent = Math.Max(extent, MeasureExtent(crosshair));
        }

        int scale = (int)((Math.Min(width, height) / 2 - 14) / extent);
        scale -= scale % 2;
        return Math.Clamp(scale, SmallestScale, LargestScale);
    }

    public static void Draw(Canvas canvas, JsonObject? crosshair, int scale)
    {
        canvas.Children.Clear();
        double width = canvas.ActualWidth;
        double height = canvas.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        Brush floorBrush = (Brush)Application.Current.FindResource("PreviewFloorBrush");
        AddRectangle(canvas, new Rect(0, height * FloorStart, width, height * (1 - FloorStart)), floorBrush, 1);
        if (crosshair == null)
        {
            return;
        }

        double centerX = Math.Round(width / 2);
        double centerY = Math.Round(height / 2);
        List<Piece> pieces = BuildPieces(crosshair);

        double outlineThickness = Outline(crosshair);
        if (outlineThickness > 0)
        {
            var outlineBrush = new SolidColorBrush(JsonHelper.ReadColor(crosshair, "outlineColor"));
            double outlineOpacity = JsonHelper.Number(crosshair, "outlineOpacity");
            foreach (Piece piece in pieces)
            {
                Rect area = piece.Area;
                area.Inflate(outlineThickness, outlineThickness);
                AddRectangle(canvas, ToCanvas(area, centerX, centerY, scale), outlineBrush, outlineOpacity);
            }
        }

        var lineBrush = new SolidColorBrush(LineColor(crosshair));
        foreach (Piece piece in pieces)
        {
            AddRectangle(canvas, ToCanvas(piece.Area, centerX, centerY, scale), lineBrush, piece.Opacity);
        }
    }

    private static List<Piece> BuildPieces(JsonObject crosshair)
    {
        var pieces = new List<Piece>();
        double dotSize = JsonHelper.Number(crosshair, "centerDotSize");
        if (JsonHelper.Flag(crosshair, "bDisplayCenterDot") && dotSize > 0)
        {
            Rect dot = new Rect(-dotSize / 2, -dotSize / 2, dotSize, dotSize);
            pieces.Add(new Piece(dot, JsonHelper.Number(crosshair, "centerDotOpacity")));
        }

        AddLines(pieces, JsonHelper.Child(crosshair, "innerLines"));
        AddLines(pieces, JsonHelper.Child(crosshair, "outerLines"));
        return pieces;
    }

    private static void AddLines(List<Piece> pieces, JsonNode? lines)
    {
        double thickness = JsonHelper.Number(lines, "lineThickness");
        if (!JsonHelper.Flag(lines, "bShowLines") || thickness <= 0)
        {
            return;
        }

        double length = JsonHelper.Number(lines, "lineLength");
        double verticalLength = JsonHelper.Flag(lines, "bAllowVertScaling") ? JsonHelper.Number(lines, "lineLengthVertical") : length;
        double offset = JsonHelper.Number(lines, "lineOffset");
        double opacity = JsonHelper.Number(lines, "opacity");
        double half = thickness / 2;

        if (length > 0)
        {
            pieces.Add(new Piece(new Rect(offset, -half, length, thickness), opacity));
            pieces.Add(new Piece(new Rect(-offset - length, -half, length, thickness), opacity));
        }

        if (verticalLength > 0)
        {
            pieces.Add(new Piece(new Rect(-half, offset, thickness, verticalLength), opacity));
            pieces.Add(new Piece(new Rect(-half, -offset - verticalLength, thickness, verticalLength), opacity));
        }
    }

    private static double MeasureExtent(JsonObject? crosshair)
    {
        if (crosshair == null)
        {
            return 0;
        }

        double outline = Math.Max(0, Outline(crosshair));
        double extent = 0;
        foreach (Piece piece in BuildPieces(crosshair))
        {
            Rect area = piece.Area;
            double farthest = Math.Max(Math.Max(-area.Left, area.Right), Math.Max(-area.Top, area.Bottom));
            extent = Math.Max(extent, farthest + outline);
        }

        return extent;
    }

    private static double Outline(JsonObject crosshair)
    {
        return JsonHelper.Flag(crosshair, "bHasOutline") ? JsonHelper.Number(crosshair, "outlineThickness") : 0;
    }

    private static Color LineColor(JsonObject crosshair)
    {
        string colorName = JsonHelper.Flag(crosshair, "bUseCustomColor") ? "colorCustom" : "color";
        return JsonHelper.ReadColor(crosshair, colorName);
    }

    private static Rect ToCanvas(Rect area, double centerX, double centerY, int scale)
    {
        return new Rect(centerX + area.X * scale, centerY + area.Y * scale, area.Width * scale, area.Height * scale);
    }

    private static void AddRectangle(Canvas canvas, Rect area, Brush brush, double opacity)
    {
        var rectangle = new Rectangle
        {
            Width = area.Width,
            Height = area.Height,
            Fill = brush,
            Opacity = opacity
        };
        Canvas.SetLeft(rectangle, area.X);
        Canvas.SetTop(rectangle, area.Y);
        canvas.Children.Add(rectangle);
    }
}
