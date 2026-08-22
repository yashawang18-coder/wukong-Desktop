namespace Wukong.Desktop;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        BootstrapLog.WriteRaw("program_main_enter");
        using var singleInstance = DesktopSingleInstance.Acquire();
        if (!singleInstance.IsPrimary)
        {
            BootstrapLog.WriteRaw("secondary_instance_activation_requested");
            singleInstance.SignalPrimary();
            return;
        }

        try
        {
            BootstrapLog.Write("Program Main entered", new
            {
                Thread.CurrentThread.ManagedThreadId,
                Apartment = Thread.CurrentThread.GetApartmentState().ToString()
            });

            BootstrapLog.WriteRaw("app_construct_before");
            var app = new App();
            BootstrapLog.WriteRaw("app_construct_after");

            BootstrapLog.WriteRaw("app_initialize_before");
            app.InitializeComponent();
            BootstrapLog.WriteRaw("app_initialize_after");

            singleInstance.StartListening(() => app.Dispatcher.BeginInvoke(() =>
                DesktopStartup.ActivateMainWindow(app)));

            BootstrapLog.WriteRaw("app_run_before");
            app.Run();
            BootstrapLog.WriteRaw("app_run_after");
        }
        catch (Exception ex)
        {
            BootstrapLog.WriteRaw($"startup_exception_{ex.GetType().Name}");
        }
    }
}
