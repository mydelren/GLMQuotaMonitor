using System.Text.Json;
using GLMQuotaMonitor.Models;

namespace GLMQuotaMonitor;

/// <summary>
/// 配置管理服务
/// 负责配置的读取、保存和热重载
/// </summary>
public class ConfigService
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GLMQuotaMonitor");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

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
