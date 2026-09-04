using System.Windows;
using System.Windows.Threading;

namespace Wukong.Desktop;

public partial class App : System.Windows.Application
{
    public App()
    {
        BootstrapLog.WriteRaw("app_constructed");
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        BootstrapLog.WriteRaw("app_onstartup_enter");
        BootstrapLog.Write("OnStartup entered", new
        {
            Thread.CurrentThread.ManagedThreadId,
            Apartment = Thread.CurrentThread.GetApartmentState().ToString(),
            DispatcherHasShutdownStarted = Dispatcher.HasShutdownStarted,
            DispatcherHasShutdownFinished = Dispatcher.HasShutdownFinished
        });

        BootstrapLog.WriteRaw("base_onstartup_before");
        base.OnStartup(e);
        BootstrapLog.WriteRaw("base_onstartup_after");

        var window = DesktopStartup.EnsureMainWindow(this);
        BootstrapLog.WriteRaw("mainwindow_show_before");
        window.Show();
        BootstrapLog.WriteRaw("mainwindow_show_after");
        BootstrapLog.Write("MainWindow Show returned", window.Snapshot());
    }

    protected override void OnExit(ExitEventArgs e)
    {
        BootstrapLog.Write("OnExit", new { e.ApplicationExitCode });
        base.OnExit(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        BootstrapLog.WriteRaw($"dispatcher_unhandled_exception_{e.Exception.GetType().Name}");
        BootstrapLog.Write("DispatcherUnhandledException", e.Exception);
        e.Handled = false;
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        BootstrapLog.WriteRaw($"appdomain_unhandled_exception_{e.ExceptionObject?.GetType().Name ?? "unknown"}");
        BootstrapLog.Write("AppDomainUnhandledException", e.ExceptionObject);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        BootstrapLog.WriteRaw($"unobserved_task_exception_{e.Exception.GetType().Name}");
        BootstrapLog.Write("UnobservedTaskException", e.Exception);
    }
}
