using System.Text.Json;
using GLMQuotaMonitor.Models;

namespace GLMQuotaMonitor;

/// <summary>
/// 配置管理服务
/// 负责配置的读取、保存和热重载
/// </summary>
public class ConfigService
{
    /// <summary>
    /// 配置目录覆盖（--config-dir 启动参数），用于便携模式与多实例调试；
    /// 注意 SpecialFolder.ApplicationData 不受 APPDATA 环境变量影响，隔离必须走显式目录
    /// </summary>
    public static string? DirOverride { get; set; }

    // 必须是计算属性：static readonly 初始化会在首次触碰类型时执行，
    // 那时 Main 对 DirOverride 的赋值还没发生，覆盖会被静默丢弃
    private static string ConfigDir => DirOverride
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GLMQuotaMonitor");

    // 同上：依赖 ConfigDir 的路径也必须每次现算，否则会在类型初始化时被固化为默认目录
    private static string ConfigPath => Path.Combine(ConfigDir, "config.json");

    /// <summary>当前生效的配置文件完整路径（诊断用）</summary>
    public static string CurrentConfigPath => ConfigPath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private AppConfig _config;

    public ConfigService()
    {
        _config = Load();
    }

    /// <summary>当前配置</summary>
    public AppConfig Config => _config;

    /// <summary>配置变更事件</summary>
    public event Action<AppConfig>? ConfigChanged;

    /// <summary>
    /// 从文件加载配置，文件不存在则返回默认值
    /// </summary>
    public AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                string json = File.ReadAllText(ConfigPath);
                var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
                if (config != null)
                {
                    _config = config;
                    return _config;
                }
            }
            else if (Environment.GetEnvironmentVariable("GLMQM_DEBUG") is "1")
            {
                SafeDebugLog($"[Load] config not found: {ConfigPath}");
            }
        }
        catch (JsonException ex) when (Environment.GetEnvironmentVariable("GLMQM_DEBUG") is "1")
        {
            SafeDebugLog($"[Load] JsonException: {ex.Message}");
        }
        catch (JsonException)
        {
            // JSON 损坏，使用默认值
        }
        catch (IOException)
        {
            // 文件读取失败，使用默认值
        }

        _config = new AppConfig();
        return _config;
    }

    /// <summary>调试日志（设 GLMQM_DEBUG=1 环境变量启用），写 %TEMP%\glmqm-debug.log</summary>
    internal static void SafeDebugLog(string message)
    {
        if (Environment.GetEnvironmentVariable("GLMQM_DEBUG") is not "1") return;
        try
        {
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "glmqm-debug.log"),
                $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
        }
        catch { }
    }

    /// <summary>
    /// 保存配置到文件，写入成功后才触发 ConfigChanged
    /// </summary>
    public void Save(AppConfig config)
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            string json = JsonSerializer.Serialize(config, JsonOptions);
            File.WriteAllText(ConfigPath, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"[ConfigService] Save failed: {ex.Message}");
            return; // 写入失败，不更新内存，不触发事件
        }

        _config = config;
        ConfigChanged?.Invoke(_config);
    }

    /// <summary>
    /// 检查 Token 是否已配置
    /// </summary>
    public bool HasToken()
    {
        return !string.IsNullOrWhiteSpace(_config.GetEffectiveAuthToken());
    }
}
