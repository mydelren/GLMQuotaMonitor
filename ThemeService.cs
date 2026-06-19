using Microsoft.Win32;

namespace GLMQuotaMonitor;

/// <summary>
/// 主题检测服务
/// 支持深色/浅色/自动（跟随系统）模式
/// </summary>
public class ThemeService : IDisposable
{
    private const string RegistryKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string RegistryValueName = "AppsUseLightTheme";

    private Models.ThemeMode _mode = Models.ThemeMode.Auto;
    private bool _isDark;

    public ThemeService()
    {
        // 初始检测
        _isDark = DetectSystemDarkMode();

        // 订阅系统主题变更事件
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    /// <summary>当前是否为深色模式</summary>
    public bool IsDark => _mode switch
    {
        Models.ThemeMode.Dark => true,
        Models.ThemeMode.Light => false,
        _ => _isDark
    };

    /// <summary>主题变更事件</summary>
    public event Action<bool>? ThemeChanged;

    /// <summary>
    /// 设置主题模式
    /// </summary>
    public void SetMode(Models.ThemeMode mode)
    {
        _mode = mode;
        bool newDark = IsDark;
        ThemeChanged?.Invoke(newDark);
    }

    /// <summary>
    /// 检测系统是否为深色模式
    /// </summary>
    private static bool DetectSystemDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
            if (key != null)
            {
                var value = key.GetValue(RegistryValueName);
                if (value is int intValue)
                    return intValue == 0; // 0 = dark, 1 = light
            }
        }
        catch
        {
            // 注册表读取失败，默认浅色
        }

        return false; // 默认浅色
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General)
        {
            bool newDark = DetectSystemDarkMode();
            if (newDark != _isDark)
            {
                _isDark = newDark;
                if (_mode == Models.ThemeMode.Auto)
                    ThemeChanged?.Invoke(_isDark);
            }
        }
    }

    public void Dispose()
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }
}
