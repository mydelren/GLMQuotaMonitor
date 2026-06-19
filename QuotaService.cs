using System.Text.Json;
using GLMQuotaMonitor.Models;

namespace GLMQuotaMonitor;

/// <summary>
/// 配额查询服务
/// 负责调用 GLM/Z.ai 监控 API，解析配额数据，管理轮询
/// </summary>
public class QuotaService : IDisposable
{
    private const int RequestTimeoutMs = 10_000;
    private const int MaxRetryCount = 3;
    private const int StartupDelayMs = 3_000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private System.Windows.Forms.Timer? _pollTimer;
    private int _consecutiveFailures;
    private QuotaSnapshot _lastSnapshot = new() { IsOffline = true };

    public QuotaService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(RequestTimeoutMs) };
    }

    /// <summary>配额数据更新事件</summary>
    public event Action<QuotaSnapshot>? QuotaUpdated;

    /// <summary>错误事件</summary>
    public event Action<string>? Error;

    /// <summary>
    /// 启动轮询
    /// </summary>
    public async void StartPolling(int intervalMinutes, Func<string> tokenGetter, Func<string> baseUrlGetter)
    {
        StopPolling();

        // 启动延迟，等待网络就绪
        await Task.Delay(StartupDelayMs);

        // 首次请求
        await FetchQuota(tokenGetter(), baseUrlGetter());

        // 如果连续失败次数未超限，启动定时器
        if (_consecutiveFailures < MaxRetryCount)
        {
            _pollTimer = new System.Windows.Forms.Timer
            {
                Interval = Math.Clamp(intervalMinutes, 1, 30) * 60 * 1000
            };
            _pollTimer.Tick += async (_, _) => await FetchQuota(tokenGetter(), baseUrlGetter());
            _pollTimer.Start();
        }
    }

    /// <summary>
    /// 停止轮询
    /// </summary>
    public void StopPolling()
    {
        _pollTimer?.Stop();
        _pollTimer?.Dispose();
        _pollTimer = null;
    }

    /// <summary>
    /// 手动刷新一次
    /// </summary>
    public async Task QuickRefresh(string token, string baseUrl)
    {
        _consecutiveFailures = 0; // 手动刷新重置失败计数
        await FetchQuota(token, baseUrl);

        // 如果定时器已停止（因连续失败），重新启动
        if (_pollTimer == null && _consecutiveFailures < MaxRetryCount)
        {
            StartPolling(3, () => token, () => baseUrl);
        }
    }

    /// <summary>
    /// 获取最后一次成功/离线的快照
    /// </summary>
    public QuotaSnapshot GetLastSnapshot() => _lastSnapshot;

    /// <summary>
    /// 执行一次配额查询
    /// </summary>
    private async Task FetchQuota(string token, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            _lastSnapshot = new QuotaSnapshot { IsOffline = true };
            QuotaUpdated?.Invoke(_lastSnapshot);
            Error?.Invoke("Token 未配置");
            return;
        }

        try
        {
            string domain = NormalizeBaseUrl(baseUrl);
            string url = $"{domain}/api/monitor/usage/quota/limit";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Authorization", token);
            request.Headers.Add("Accept-Language", "en-US,en");

            using var response = await _http.SendAsync(request);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync();
            var snapshot = ParseQuotaResponse(json);
            snapshot.Timestamp = DateTime.Now;

            _lastSnapshot = snapshot;
            _consecutiveFailures = 0;

            QuotaUpdated?.Invoke(snapshot);
        }
        catch (Exception ex)
        {
            _consecutiveFailures++;
            _lastSnapshot.IsOffline = true;

            Error?.Invoke($"请求失败 ({_consecutiveFailures}/{MaxRetryCount}): {ex.Message}");

            if (_consecutiveFailures >= MaxRetryCount)
            {
                StopPolling();
                Error?.Invoke("连续失败次数过多，已停止自动轮询，请手动刷新");
            }

            // 仍然通知 UI 更新（显示离线状态）
            QuotaUpdated?.Invoke(_lastSnapshot);
        }
    }

    /// <summary>
    /// 解析配额 API 响应
    /// </summary>
    private static QuotaSnapshot ParseQuotaResponse(string json)
    {
        var snapshot = new QuotaSnapshot { IsOffline = false };

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("data", out var data))
            return snapshot;

        if (!data.TryGetProperty("limits", out var limits))
            return snapshot;

        foreach (var item in limits.EnumerateArray())
        {
            string type = item.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";

            // 兼容多种字段名
            long total = GetLongAny(item, "usage", "limit_value", "limitValue");
            long used = GetLongAny(item, "currentValue", "used_value", "usedValue");
            long remaining = GetLongAny(item, "remaining", "remaining_value", "remainingValue");

            if (remaining == 0 && total > 0)
                remaining = total - used;

            var quotaItem = new QuotaItem
            {
                Type = type,
                Total = total,
                Used = used,
                Remaining = remaining
            };

            switch (type)
            {
                case "TIME_LIMIT":
                    quotaItem.Name = "MCP 配额";
                    snapshot.McpQuota = quotaItem;
                    break;
                case "TOKENS_LIMIT":
                    quotaItem.Name = "5h Token";
                    snapshot.Token5hQuota = quotaItem;
                    break;
            }
        }

        return snapshot;
    }

    /// <summary>
    /// 从多个可能的字段名中获取 long 值
    /// </summary>
    private static long GetLongAny(JsonElement element, params string[] names)
    {
        foreach (string name in names)
        {
            if (element.TryGetProperty(name, out var val))
            {
                if (val.TryGetInt64(out long result))
                    return result;
            }
        }
        return 0;
    }

    /// <summary>
    /// 标准化 base URL，确保有 scheme
    /// </summary>
    private static string NormalizeBaseUrl(string url)
    {
        url = url.TrimEnd('/');

        if (!url.StartsWith("http://") && !url.StartsWith("https://"))
            url = "https://" + url;

        // 如果用户填了带 /api/anthropic 后缀的路径，去掉
        int apiIdx = url.IndexOf("/api/anthropic", StringComparison.OrdinalIgnoreCase);
        if (apiIdx > 0)
            url = url[..apiIdx];

        return url;
    }

    public void Dispose()
    {
        StopPolling();
        _http.Dispose();
    }
}
