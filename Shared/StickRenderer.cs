using System.Drawing;
using System.Drawing.Drawing2D;
using System.Numerics;

namespace MMDiva.Debug;

public record Segment(Vector3 A, Vector3 B, Color Color);

public static class StickRenderer
{
    public static void Render(List<Segment> segments, string path, float viewHeight = 2.0f, int cell = 260)
    {
        using var bmp = new Bitmap(cell, cell * 2);
        using var graphics = Graphics.FromImage(bmp);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.White);

        using var font = new Font("Arial", 9);
        using var gridPen = new Pen(Color.FromArgb(220, 220, 220));

        float scale = cell / viewHeight * 0.95f;

        for (int view = 0; view < 2; view++)
        {
            int y0 = view * cell;
            graphics.DrawRectangle(gridPen, 0, y0, cell - 1, cell - 1);
            float cx = cell / 2.0f;
            float floorY = y0 + cell - 10;
            graphics.DrawLine(gridPen, 0, floorY, cell, floorY);
            graphics.DrawString(view == 0 ? "front (red = left side)" : "side (facing right)", font, Brushes.Gray, 4, y0 + 2);

            PointF P(Vector3 p) => view == 0 ? new PointF(cx + p.X * scale, floorY - p.Y * scale) : new PointF(cx + p.Z * scale, floorY - p.Y * scale);

            foreach (var segment in segments)
            {
                using var pen = new Pen(segment.Color, 2.0f);
                graphics.DrawLine(pen, P(segment.A), P(segment.B));
            }
        }

        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    public static Color SideColor(string name)
    {
        string n = name.ToLowerInvariant();
        if (n.Contains("_l_") || n.EndsWith("_l") || n.Contains("左")) return Color.Crimson;
        if (n.Contains("_r_") || n.EndsWith("_r") || n.Contains("右")) return Color.RoyalBlue;
        return Color.Black;
    }
}
