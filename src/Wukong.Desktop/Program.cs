namespace Wukong.Desktop;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        BootstrapLog.WriteRaw("program_main_enter");
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
