namespace GLMQuotaMonitor;

internal static class Program
{
    private const string MutexName = "Global\\GLMQuotaMonitor_{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}";

    [STAThread]
    static void Main(string[] args)
    {
        // 单实例保证；--instance=名称 可启动并行实例（独立互斥体）；--config-dir 指定配置目录；--demo 演示数据
        string mutexName = MutexName;
        const string instancePrefix = "--instance=";
        foreach (string arg in args)
        {
            if (arg.StartsWith(instancePrefix, StringComparison.OrdinalIgnoreCase))
            {
                // 值会拼进 Global\ 互斥体名，仅允许安全字符，防止非法字符导致启动崩溃
                string raw = arg[instancePrefix.Length..].Trim('"');
                string safe = new string(raw.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray());
                mutexName = $"Global\\GLMQuotaMonitor_{safe}_{{B7E4C9A1-3F2D-4E58-9A6B-C01D23456789}}";
            }
            else if (arg.Equals("--demo", StringComparison.OrdinalIgnoreCase))
                QuotaService.DemoMode = true;
            else if (arg.StartsWith("--config-dir=", StringComparison.OrdinalIgnoreCase))
                ConfigService.DirOverride = arg["--config-dir=".Length..].Trim('"');
        }

        using var mutex = new Mutex(true, mutexName, out bool createdNew);
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
