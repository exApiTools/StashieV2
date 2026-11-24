namespace Stashie.Classes;

public static class RectangleExtensions
{
    public static System.Drawing.RectangleF ToDrawing(this SharpDX.RectangleF rect) => new(rect.X, rect.Y, rect.Width, rect.Height);
}
