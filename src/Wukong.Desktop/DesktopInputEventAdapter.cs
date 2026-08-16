using System.Windows;
using Wukong.Domain;

namespace Wukong.Desktop;

public static class DesktopInputEventAdapter
{
    public static InputEvent PointerDown(Point point) =>
        Pointer(InputEventKind.PointerDown, point);

    public static InputEvent PointerMove(Point point) =>
        Pointer(InputEventKind.PointerMove, point);

    public static InputEvent PointerUp(Point point) =>
        Pointer(InputEventKind.PointerUp, point);

    private static InputEvent Pointer(InputEventKind kind, Point point) =>
        InputEvent.Create(
            kind,
            DateTimeOffset.Now,
            BehaviorRequestSource.OwnerUi,
            new Dictionary<string, string>
            {
                ["x"] = point.X.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                ["y"] = point.Y.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
            });
}
