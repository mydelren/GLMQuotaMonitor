namespace GLMQuotaMonitor.Models;

/// <summary>
/// 平台类型
/// </summary>
public enum PlatformType
{
    Auto,
    Zhipu,
    ZhipuDev,
    Zai
}

/// <summary>
/// 主题模式
/// </summary>
public enum ThemeMode
{
    Auto,
    Dark,
    Light
}

/// <summary>
/// 应用配置
/// </summary>
public class AppConfig
{
    /// <summary>API Key</summary>
    public string AuthToken { get; set; } = "";

    /// <summary>平台选择</summary>
    public PlatformType Platform { get; set; } = PlatformType.Auto;

    /// <summary>轮询间隔（分钟）</summary>
    public int PollingIntervalMinutes { get; set; } = 5;

    /// <summary>主题模式</summary>
    public ThemeMode Theme { get; set; } = ThemeMode.Auto;

    /// <summary>是否显示浮动条</summary>
    public bool ShowFloatingBar { get; set; }

    /// <summary>警告阈值 (百分比)</summary>
    public int WarningThreshold { get; set; } = 50;

    /// <summary>临界阈值 (百分比)</summary>
    public int CriticalThreshold { get; set; } = 80;

    /// <summary>浮动条 X 坐标（null = 默认位置）</summary>
    public int? FloatingBarX { get; set; }

    /// <summary>浮动条 Y 坐标（null = 默认位置）</summary>
    public int? FloatingBarY { get; set; }

    /// <summary>
    /// 贴边状态持久化：直接存 DockStyle 枚举值——0=未贴边，1=顶，3=左，4=右（2=底，暂未支持贴底）
    /// </summary>
    public int SnapEdgeValue { get; set; }

    /// <summary>贴边时沿边缘的位置坐标（垂直边存 Y，水平边存 X；null = 未记录）</summary>
    public int? SnapPosition { get; set; }

    /// <summary>
    /// 获取实际使用的平台域名
    /// </summary>
    public string GetBaseUrl()
    {
        return Platform switch
        {
            PlatformType.Zai => "https://api.z.ai",
            PlatformType.ZhipuDev => "https://dev.bigmodel.cn",
            PlatformType.Zhipu => "https://open.bigmodel.cn",
            _ => DetectPlatformFromEnv()
        };
    }

    /// <summary>
    /// 从环境变量自动检测平台
    /// </summary>
    private static string DetectPlatformFromEnv()
    {
        string baseUrl = Environment.GetEnvironmentVariable("ANTHROPIC_BASE_URL") ?? "";

        if (baseUrl.Contains("api.z.ai"))
            return "https://api.z.ai";
        if (baseUrl.Contains("dev.bigmodel.cn"))
            return "https://dev.bigmodel.cn";
        if (baseUrl.Contains("open.bigmodel.cn"))
            return "https://open.bigmodel.cn";

        // 默认使用智谱
        return "https://open.bigmodel.cn";
    }

    /// <summary>
    /// 获取实际使用的 Auth Token（手动配置优先于环境变量）
    /// </summary>
    public string GetEffectiveAuthToken()
    {
        if (!string.IsNullOrWhiteSpace(AuthToken))
            return AuthToken;

        return Environment.GetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN") ?? "";
    }

    /// <summary>
    /// 验证轮询间隔范围
    /// </summary>
    public int GetEffectivePollingInterval()
    {
        return Math.Clamp(PollingIntervalMinutes, 1, 30);
    }
}
