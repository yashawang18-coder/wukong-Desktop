namespace Wukong.Desktop;

public static class DesktopStartup
{
    public static MainWindow EnsureMainWindow(System.Windows.Application application)
    {
        if (application.MainWindow is MainWindow existing)
            return existing;

        BootstrapLog.Write("MainWindow construct before");
        var window = new MainWindow();
        BootstrapLog.Write("MainWindow construct after", window.Snapshot());

        application.MainWindow = window;
        BootstrapLog.Write("Application.MainWindow assigned");
        return window;
    }
}
