namespace GLMQuotaMonitor;

internal static class Program
{
    private const string MutexName = "Global\\GLMQuotaMonitor_{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}";

    [STAThread]
    static void Main()
    {
        // 单实例保证
        using var mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            // 已有实例在运行，静默退出
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.SystemAware);

        Application.Run(new TrayApplicationContext());

        GC.KeepAlive(mutex);
    }
}
