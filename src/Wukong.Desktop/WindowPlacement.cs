using System.Windows;

namespace Wukong.Desktop;

public static class WindowPlacement
{
    public static Point BottomRight(Rect workArea, double width, double height, double margin)
    {
        var left = Math.Max(workArea.Left, workArea.Right - width - margin);
        var top = Math.Max(workArea.Top, workArea.Bottom - height - margin);
        return new Point(left, top);
    }
}
