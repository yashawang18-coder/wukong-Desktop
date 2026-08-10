using System.Windows;

namespace Wukong.Desktop;

public static class DesktopChatPlacement
{
    private const double Gap = 10;
    private const double Margin = 8;

    public static Point Place(Rect workArea, Rect petBounds, Size overlaySize)
    {
        var width = Math.Min(Math.Max(overlaySize.Width, 1), Math.Max(1, workArea.Width - Margin * 2));
        var height = Math.Min(Math.Max(overlaySize.Height, 1), Math.Max(1, workArea.Height - Margin * 2));
        var preferredLeft = petBounds.Left + (petBounds.Width - width) / 2;
        var below = petBounds.Bottom + Gap;
        var above = petBounds.Top - height - Gap;
        var preferredTop = below + height <= workArea.Bottom - Margin ? below : above;
        var left = Math.Clamp(preferredLeft, workArea.Left + Margin, workArea.Right - width - Margin);
        var top = Math.Clamp(preferredTop, workArea.Top + Margin, workArea.Bottom - height - Margin);
        return new Point(left, top);
    }
}
