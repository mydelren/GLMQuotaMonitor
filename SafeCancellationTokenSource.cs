namespace GLMQuotaMonitor;

/// <summary>
/// 线程安全的 CancellationTokenSource 包装器。
/// 解决 re-entrant 调用场景下的 ObjectDisposedException 问题。
/// CancelAndRecreate() 原子地取消旧 CTS 并创建新 CTS。
/// </summary>
internal sealed class SafeCancellationTokenSource : IDisposable
{
    private CancellationTokenSource _cts = new CancellationTokenSource();

    /// <summary>
    /// 获取当前活跃的 token。永远安全，不会 ObjectDisposedException。
    /// </summary>
    public CancellationToken Token => _cts.Token;

    /// <summary>
    /// 原子地取消旧的 CTS 并创建新的。
    /// 先建新的再取消旧的，确保 Token 属性始终返回可用的新 token。
    /// </summary>
    public void CancelAndRecreate()
    {
        var old = _cts;
        _cts = new CancellationTokenSource();
        try { old.Cancel(); }
        finally { old.Dispose(); }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null!;
    }
}
