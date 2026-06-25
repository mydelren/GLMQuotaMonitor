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
    private readonly SafeCancellationTokenSource _pollCts = new();
    private int _consecutiveFailures;
    private volatile bool _enteredBackoff;
    private QuotaSnapshot _lastSnapshot = new() { IsOffline = true };

    // 临时调试日志（排查轮询问题后删除）
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GLMQuotaMonitor", "poll-debug.log");
    private static void Log(string msg)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {msg}\n");
        }
        catch { }
    }

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
        _pollCts.CancelAndRecreate();
        var token = _pollCts.Token;

        _consecutiveFailures = 0;
        _enteredBackoff = false;
        int intervalMs = Math.Clamp(intervalMinutes, 1, 30) * 60 * 1000;
        Log($"[StartPolling] interval={intervalMs}ms, token={tokenGetter()[..Math.Min(8, tokenGetter().Length)]}...");

        _ = Task.Run(async () =>
        {
            try
            {
                // 启动延迟
                Log("[PollTask] startup delay begin");
                await Task.Delay(StartupDelayMs, token);
                Log("[PollTask] startup delay done, entering loop");

                while (!token.IsCancellationRequested)
                {
                    Log("[PollTask] fetching...");
                    await FetchQuota(tokenGetter(), baseUrlGetter(), token);
                    Log($"[PollTask] fetch done, failures={_consecutiveFailures}, backoff={_enteredBackoff}");

                    if (_consecutiveFailures >= MaxRetryCount)
                    {
                        if (!_enteredBackoff)
                        {
                            _enteredBackoff = true;
                            Error?.Invoke("连续失败，已进入退避重试模式");
                        }
                        int shift = Math.Min(_consecutiveFailures - MaxRetryCount, 20);
                        int retryDelay = (int)Math.Min((long)intervalMs * (1L << shift), 30L * 60 * 1000);
                        await Task.Delay(retryDelay, token);
                    }
                    else
                    {
                        _enteredBackoff = false;
                        await Task.Delay(intervalMs, token);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Log("[PollTask] cancelled");
                // 正常取消，忽略
            }
            catch (Exception ex)
            {
                Log($"[PollTask] UNHANDLED: {ex}");
            }
        });
    }

    /// <summary>
    /// 停止轮询
    /// </summary>
    public void StopPolling()
    {
        _pollCts.CancelAndRecreate();
    }

    /// <summary>
    /// 手动刷新一次
    /// </summary>
    public async Task QuickRefresh(string token, string baseUrl, int intervalMinutes = 3)
    {
        _consecutiveFailures = 0;
        await FetchQuota(token, baseUrl, CancellationToken.None);

        // 无论轮询是否在运行，都重启（确保轮询不中断）
        StartPolling(intervalMinutes, () => token, () => baseUrl);
    }

    /// <summary>
    /// 获取最后一次成功/离线的快照
    /// </summary>
    public QuotaSnapshot GetLastSnapshot() => _lastSnapshot;

    /// <summary>
    /// 执行一次配额查询（并行请求 quota/limit 和 model-usage）
    /// </summary>
    private async Task FetchQuota(string token, string baseUrl, CancellationToken ct)
    {
        Log($"[FetchQuota] token={token[..Math.Min(8, token.Length)]}..., base={baseUrl}");
        if (string.IsNullOrWhiteSpace(token))
        {
            Log("[FetchQuota] token empty, returning offline");
            var offline = new QuotaSnapshot
            {
                IsOffline = true,
                McpQuota = _lastSnapshot.McpQuota,
                Token5hQuota = _lastSnapshot.Token5hQuota,
                CallCount = _lastSnapshot.CallCount,
                TokenUsage = _lastSnapshot.TokenUsage
            };
            _lastSnapshot = offline;
            QuotaUpdated?.Invoke(offline);
            Error?.Invoke("Token 未配置");
            return;
        }

        try
        {
            string domain = NormalizeBaseUrl(baseUrl);

            // 并行请求两个接口
            var quotaTask = FetchJson(domain, "/api/monitor/usage/quota/limit", token, ct);
            var usageTask = FetchModelUsage(domain, token, ct);

            // 等待主接口
            await quotaTask;
            var snapshot = ParseQuotaResponse(quotaTask.Result);

            // model-usage 单独处理，失败不影响主流程
            try
            {
                if (!usageTask.IsCompleted) await usageTask;
                if (usageTask.Status == TaskStatus.RanToCompletion && usageTask.Result != null)
                {
                    ParseModelUsage(usageTask.Result, snapshot);
                    System.Diagnostics.Debug.WriteLine($"[QuotaService] model-usage parsed: calls={snapshot.CallCount}, tokens={snapshot.TokenUsage}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[QuotaService] model-usage failed: {ex.Message}");
            }
            snapshot.Timestamp = DateTime.Now;
            snapshot.IsOffline = false;

            _lastSnapshot = snapshot;
            _consecutiveFailures = 0;
            _enteredBackoff = false;

            Log($"[FetchQuota] OK, failures reset");
            QuotaUpdated?.Invoke(snapshot);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _consecutiveFailures++;
            Log($"[FetchQuota] FAIL ({_consecutiveFailures}): {ex.Message}");

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

            QuotaUpdated?.Invoke(offline);
        }
    }

    /// <summary>
    /// 通用 GET JSON 请求
    /// </summary>
    private async Task<string> FetchJson(string domain, string path, string token, CancellationToken ct)
    {
        string url = $"{domain}{path}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Authorization", token);
        request.Headers.Add("Accept-Language", "en-US,en");

        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    /// <summary>
    /// 请求 model-usage 接口（24 小时滑动窗口）
    /// </summary>
    private async Task<string> FetchModelUsage(string domain, string token, CancellationToken ct)
    {
        var now = DateTime.Now;
        string start = now.AddHours(-24).ToString("yyyy-MM-dd HH:mm:ss");
        string end = now.ToString("yyyy-MM-dd HH:mm:ss");
        string path = $"/api/monitor/usage/model-usage?startTime={Uri.EscapeDataString(start)}&endTime={Uri.EscapeDataString(end)}";
        return await FetchJson(domain, path, token, ct);
    }

    /// <summary>
    /// 解析 quota/limit 响应
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
            System.Diagnostics.Debug.WriteLine($"[QuotaService] limit item: {item}");

            string type = item.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
            long total = GetLongAny(item, "usage", "limit_value", "limitValue");
            long used = GetLongAny(item, "currentValue", "used_value", "usedValue");
            long remaining = GetLongAny(item, "remaining", "remaining_value", "remainingValue");

            // 读取 API 直接返回的百分比
            double directPct = -1;
            if (item.TryGetProperty("percentage", out var pctElem))
            {
                if (pctElem.TryGetDouble(out double pctVal)) directPct = pctVal;
                else if (pctElem.ValueKind == JsonValueKind.String && double.TryParse(pctElem.GetString(), out double pctStr)) directPct = pctStr;
            }

            if (remaining == 0 && total > 0)
                remaining = total - used;

            long nextReset = GetLongAny(item, "nextResetTime", "next_reset_time", "resetTime");

            var quotaItem = new QuotaItem
            {
                Type = type,
                Total = total,
                Used = used,
                Remaining = remaining,
                DirectPercentage = directPct,
                NextResetTime = nextReset
            };

            switch (type)
            {
                case "TIME_LIMIT":
                    quotaItem.Name = "MCP 配额";
                    snapshot.McpQuota = quotaItem;
                    break;
                case "TOKENS_LIMIT":
                    // API 可能返回多个 TOKENS_LIMIT（unit=3 是 5h 流控，unit=6 是日流控）
                    // 取第一个（unit=3 的 5h 窗口），如果已有则跳过
                    if (snapshot.Token5hQuota.Total == 0 && snapshot.Token5hQuota.DirectPercentage < 0)
                    {
                        quotaItem.Name = "5h Token";
                        snapshot.Token5hQuota = quotaItem;
                    }
                    break;
            }
        }

        return snapshot;
    }

    /// <summary>
    /// 解析 model-usage 响应，填充调用次数和 Token 用量
    /// </summary>
    private static void ParseModelUsage(string json, QuotaSnapshot snapshot)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[QuotaService] model-usage response: {json[..Math.Min(500, json.Length)]}");

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("data", out var data))
                return;

            // totalUsage 包含总计数据
            if (data.TryGetProperty("totalUsage", out var totalUsage))
            {
                snapshot.CallCount = GetLongAny(totalUsage, "totalModelCallCount", "modelCallCount");
                snapshot.TokenUsage = GetLongAny(totalUsage, "totalTokensUsage", "tokensUsage");
            }
        }
        catch
        {
            // 解析失败不影响主流程
        }
    }

    private static long GetLongAny(JsonElement element, params string[] names)
    {
        foreach (string name in names)
        {
            if (!element.TryGetProperty(name, out var val))
                continue;

            // 尝试各种数值类型
            if (val.TryGetInt64(out long result))
                return result;
            if (val.ValueKind == JsonValueKind.Number && val.TryGetDouble(out double d))
                return (long)d;
            if (val.ValueKind == JsonValueKind.String && long.TryParse(val.GetString(), out long parsed))
                return parsed;
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
        _pollCts.Dispose();
        _http.Dispose();
    }
}
