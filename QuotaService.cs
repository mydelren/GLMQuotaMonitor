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
    /// <summary>串行化网络请求，避免手动刷新与轮询周期并发写 _lastSnapshot</summary>
    private readonly SemaphoreSlim _fetchGate = new(1, 1);
    /// <summary>轮询生命周期令牌：线程安全地 Cancel+换新，永远不因 Dispose 竞态抛 ODE</summary>
    private readonly SafeCancellationTokenSource _pollCts = new();
    private int _consecutiveFailures;
    private bool _enteredBackoff;
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
    /// 启动轮询（线程安全，可重复调用）；连续失败进入指数退避（封顶 30 分钟），不再永久停摆
    /// </summary>
    public void StartPolling(int intervalMinutes, Func<string> tokenGetter, Func<string> baseUrlGetter)
    {
        _pollCts.CancelAndRecreate();
        var token = _pollCts.Token;

        _consecutiveFailures = 0;
        _enteredBackoff = false;
        int intervalMs = Math.Clamp(intervalMinutes, 1, 30) * 60 * 1000;

        _ = Task.Run(async () =>
        {
            try
            {
                // 启动延迟
                await Task.Delay(StartupDelayMs, token);

                while (!token.IsCancellationRequested)
                {
                    await FetchQuota(tokenGetter(), baseUrlGetter(), token);

                    if (_consecutiveFailures >= MaxRetryCount)
                    {
                        if (!_enteredBackoff)
                        {
                            _enteredBackoff = true;
                            Error?.Invoke("连续失败，已进入退避重试模式");
                        }
                        // 指数退避：每多失败一轮翻一倍，封顶 30 分钟
                        int shift = Math.Min(_consecutiveFailures - MaxRetryCount, 20);
                        int retryDelay = (int)Math.Min((long)intervalMs * (1L << shift), 30L * 60L * 1000L);
                        await Task.Delay(retryDelay, token);
                    }
                    else
                    {
                        await Task.Delay(intervalMs, token);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 正常取消，忽略
            }
        });
    }

    /// <summary>
    /// 停止当前轮询周期（原子换新 CTS，旧的会被取消并安全释放）
    /// </summary>
    public void StopPolling()
    {
        _pollCts.CancelAndRecreate();
        _enteredBackoff = false;
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

    /// <summary>演示模式：跳过网络，输出固定的示例数据（用于截图宣传与 UI 调试）</summary>
    public static bool DemoMode { get; set; }

    /// <summary>
    /// 执行一次配额查询（并行请求 quota/limit 和 model-usage）
    /// </summary>
    private async Task FetchQuota(string token, string baseUrl, CancellationToken ct)
    {
        // 手动刷新与轮询周期可能重叠，串行化以保护 _lastSnapshot
        await _fetchGate.WaitAsync(ct);
        try
        {
            if (DemoMode)
            {
                var demo = MakeDemoSnapshot();
                _lastSnapshot = demo;
                _consecutiveFailures = 0;
                QuotaUpdated?.Invoke(demo);
                return;
            }

            if (string.IsNullOrWhiteSpace(token))
            {
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

            QuotaUpdated?.Invoke(snapshot);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _consecutiveFailures++;

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
        finally
        {
            _fetchGate.Release();
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
            // 总量优先读 limit* 字段；"usage" 仅作兼容兜底（若 API 语义变化会被 percentage 兜住）
            long total = GetLongAny(item, "limit_value", "limitValue", "usage");
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

    /// <summary>
    /// 演示快照：MCP 7.6%（展示一位小数与最小填充），5h Token 61.8%（展示警告色），3h47m 后重置
    /// </summary>
    private static QuotaSnapshot MakeDemoSnapshot()
    {
        return new QuotaSnapshot
        {
            IsOffline = false,
            Timestamp = DateTime.Now,
            McpQuota = new QuotaItem
            {
                Name = "MCP 配额", Type = "TIME_LIMIT",
                Total = 1000, Used = 76, Remaining = 924,
                DirectPercentage = 7.6,
                NextResetTime = DateTimeOffset.Now.AddMonths(1).ToUnixTimeMilliseconds()
            },
            Token5hQuota = new QuotaItem
            {
                Name = "5h Token", Type = "TOKENS_LIMIT",
                Total = 120_000_000, Used = 74_160_000, Remaining = 45_840_000,
                DirectPercentage = 61.8,
                NextResetTime = DateTimeOffset.Now.AddHours(3).AddMinutes(47).ToUnixTimeMilliseconds()
            },
            CallCount = 1342,
            TokenUsage = 181_000_000
        };
    }

    public void Dispose()
    {
        // 注意：不 Dispose _fetchGate——手动刷新持 CancellationToken.None，
        // 若刷新仍在途，释放 gate 会让 WaitAsync/Release 抛 ODE（async void 刷新会直接崩进程）。
        // 进程生命周期对象，随进程回收即可。
        StopPolling();
        _http.Dispose();
    }
}
