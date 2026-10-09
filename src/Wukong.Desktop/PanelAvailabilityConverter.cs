using System.Globalization;
using System.Windows.Data;

namespace Wukong.Desktop;

public sealed class PanelAvailabilityContext(DesktopRuntimeHost runtime)
{
    public DesktopRuntimeHost Runtime { get; } = runtime;
}

public sealed class PanelAvailabilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length >= 2 && values[0] is PlayableMotion motion && values[1] is PanelAvailabilityContext { Runtime: var runtime }
            ? runtime.DescribePanelAvailability(motion) : "正在检查执行条件";
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
