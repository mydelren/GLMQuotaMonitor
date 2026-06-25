using System.Text.Json;
using GLMQuotaMonitor.Models;

namespace GLMQuotaMonitor;

/// <summary>
/// 配置管理服务
/// 负责配置的读取、保存和热重载
/// 配置文件优先存放在 exe 同目录（绿色模式），写入失败则回退到 %AppData%
/// </summary>
public class ConfigService
{
    private static readonly string ConfigDir;
    private static readonly string ConfigPath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    static ConfigService()
    {
        // 优先 exe 同目录（绿色模式，删 exe 零残留）
        string exeDir = AppContext.BaseDirectory;
        string localPath = Path.Combine(exeDir, "config.json");

        if (CanWriteToDir(exeDir))
        {
            ConfigDir = exeDir;
            ConfigPath = localPath;
        }
        else
        {
            // 受保护目录（如 Program Files），回退到 %AppData%
            ConfigDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "GLMQuotaMonitor");
            ConfigPath = Path.Combine(ConfigDir, "config.json");
        }
    }

    private static bool CanWriteToDir(string dir)
    {
        try
        {
            string testFile = Path.Combine(dir, $".write_test_{Guid.NewGuid():N}");
            File.WriteAllText(testFile, "");
            File.Delete(testFile);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private AppConfig _config;

    public ConfigService()
    {
        _config = Load();
    }

    /// <summary>当前配置</summary>
    public AppConfig Config => _config;

    /// <summary>配置文件所在目录（供外部显示）</summary>
    public static string ConfigDirectory => ConfigDir;

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
