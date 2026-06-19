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
    private CancellationTokenSource? _pollCts;
    private Task? _pollTask;
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
    /// 启动轮询（线程安全，可重复调用）
    /// </summary>
    public void StartPolling(int intervalMinutes, Func<string> tokenGetter, Func<string> baseUrlGetter)
    {
        StopPolling();

        _consecutiveFailures = 0;
        var cts = new CancellationTokenSource();
        _pollCts = cts;
        int intervalMs = Math.Clamp(intervalMinutes, 1, 30) * 60 * 1000;

        _pollTask = Task.Run(async () =>
        {
            try
            {
                // 启动延迟
                await Task.Delay(StartupDelayMs, cts.Token);

                while (!cts.Token.IsCancellationRequested)
                {
                    await FetchQuota(tokenGetter(), baseUrlGetter(), cts.Token);

                    if (_consecutiveFailures >= MaxRetryCount)
                        break;

                    await Task.Delay(intervalMs, cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // 正常取消，忽略
            }
        }, cts.Token);
    }

    /// <summary>
    /// 停止轮询
    /// </summary>
    public void StopPolling()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = null;

        if (_pollTask != null)
        {
            // 不等待，避免阻塞 UI 线程
            _pollTask = null;
        }
    }

    /// <summary>
    /// 手动刷新一次
    /// </summary>
    public async Task QuickRefresh(string token, string baseUrl, int intervalMinutes = 3)
    {
        _consecutiveFailures = 0;
        await FetchQuota(token, baseUrl, CancellationToken.None);

        // 如果轮询已停止（因连续失败），重新启动
        if (_pollCts == null && _consecutiveFailures < MaxRetryCount)
        {
            StartPolling(intervalMinutes, () => token, () => baseUrl);
        }
    }

    /// <summary>
    /// 获取最后一次成功/离线的快照
    /// </summary>
    public QuotaSnapshot GetLastSnapshot() => _lastSnapshot;

    /// <summary>
    /// 执行一次配额查询
    /// </summary>
    private async Task FetchQuota(string token, string baseUrl, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            var offline = new QuotaSnapshot
            {
                IsOffline = true,
                McpQuota = _lastSnapshot.McpQuota,
                Token5hQuota = _lastSnapshot.Token5hQuota
            };
            _lastSnapshot = offline;
            QuotaUpdated?.Invoke(offline);
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

            using var response = await _http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync(ct);
            var snapshot = ParseQuotaResponse(json);
            snapshot.Timestamp = DateTime.Now;
            snapshot.IsOffline = false;

            _lastSnapshot = snapshot;
            _consecutiveFailures = 0;

            QuotaUpdated?.Invoke(snapshot);
        }
        catch (OperationCanceledException)
        {
            throw; // 让调用方处理取消
        }
        catch (Exception ex)
        {
            _consecutiveFailures++;

            // 创建新的离线快照，保留上次数据
            var offline = new QuotaSnapshot
            {
                IsOffline = true,
                McpQuota = _lastSnapshot.McpQuota,
                Token5hQuota = _lastSnapshot.Token5hQuota,
                CallCount = _lastSnapshot.CallCount,
                TokenUsage = _lastSnapshot.TokenUsage
            };
            _lastSnapshot = offline;

            Error?.Invoke($"请求失败 ({_consecutiveFailures}/{MaxRetryCount}): {ex.Message}");

            if (_consecutiveFailures >= MaxRetryCount)
                Error?.Invoke("连续失败次数过多，已停止自动轮询，请手动刷新");

            QuotaUpdated?.Invoke(offline);
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

    private static long GetLongAny(JsonElement element, params string[] names)
    {
        foreach (string name in names)
        {
            if (element.TryGetProperty(name, out var val) && val.TryGetInt64(out long result))
                return result;
        }
        return 0;
    }

    private static string NormalizeBaseUrl(string url)
    {
        url = url.TrimEnd('/');

        if (!url.StartsWith("http://") && !url.StartsWith("https://"))
            url = "https://" + url;

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
