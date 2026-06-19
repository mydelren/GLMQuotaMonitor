namespace GLMQuotaMonitor.Models;

/// <summary>
/// 配额状态等级
/// </summary>
public enum QuotaStatus
{
    /// <summary>离线/错误/未配置</summary>
    Offline,
    /// <summary>正常（所有配额 &lt; 50%）</summary>
    Normal,
    /// <summary>警告（任一配额 ≥ 50%）</summary>
    Warning,
    /// <summary>临界（任一配额 ≥ 80%）</summary>
    Critical
}

/// <summary>
/// 单项配额数据
/// </summary>
public class QuotaItem
{
    /// <summary>配额类型名称（MCP 配额 / 5h Token）</summary>
    public string Name { get; set; } = "";

    /// <summary>配额类型标识（TIME_LIMIT / TOKENS_LIMIT）</summary>
    public string Type { get; set; } = "";

    /// <summary>总量</summary>
    public long Total { get; set; }

    /// <summary>已用</summary>
    public long Used { get; set; }

    /// <summary>剩余</summary>
    public long Remaining { get; set; }

    /// <summary>使用百分比 (0-100)</summary>
    public double Percentage => Total > 0 ? (double)Used / Total * 100 : 0;
}

/// <summary>
/// 完整的配额快照
/// </summary>
public class QuotaSnapshot
{
    /// <summary>MCP 月度配额</summary>
    public QuotaItem McpQuota { get; set; } = new() { Name = "MCP 配额", Type = "TIME_LIMIT" };

    /// <summary>5h Token 流控配额</summary>
    public QuotaItem Token5hQuota { get; set; } = new() { Name = "5h Token", Type = "TOKENS_LIMIT" };

    /// <summary>获取时间</summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;

    /// <summary>是否为离线/错误数据</summary>
    public bool IsOffline { get; set; }

    /// <summary>
    /// 根据阈值计算整体配额状态
    /// </summary>
    public QuotaStatus GetStatus(int warningThreshold = 50, int criticalThreshold = 80)
    {
        if (IsOffline) return QuotaStatus.Offline;

        double maxPct = Math.Max(McpQuota.Percentage, Token5hQuota.Percentage);
        if (maxPct >= criticalThreshold) return QuotaStatus.Critical;
        if (maxPct >= warningThreshold) return QuotaStatus.Warning;
        return QuotaStatus.Normal;
    }

    /// <summary>调用次数（来自 model-usage 接口，可选）</summary>
    public long CallCount { get; set; }

    /// <summary>Token 用量（来自 model-usage 接口，可选）</summary>
    public long TokenUsage { get; set; }
}
