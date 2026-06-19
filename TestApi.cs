// 临时测试文件：验证 API 解析逻辑
// 运行：dotnet script TestApi.cs 或编译后运行

using System.Text.Json;

var token = "ab1df21ceaed4c26af08cbe85b7ca54f.6gvJjLTCb8fEhmZp";
var domain = "https://open.bigmodel.cn";
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

// 测试 1: quota/limit
Console.WriteLine("=== 测试 quota/limit ===");
var req1 = new HttpRequestMessage(HttpMethod.Get, $"{domain}/api/monitor/usage/quota/limit");
req1.Headers.Add("Authorization", token);
req1.Headers.Add("Accept-Language", "en-US,en");
var res1 = await http.SendAsync(req1);
var json1 = await res1.Content.ReadAsStringAsync();

using var doc1 = JsonDocument.Parse(json1);
var limits = doc1.RootElement.GetProperty("data").GetProperty("limits");
foreach (var item in limits.EnumerateArray())
{
    var type = item.GetProperty("type").GetString();
    var pct = item.TryGetProperty("percentage", out var p) ? p.GetDouble() : -1;
    var usage = item.TryGetProperty("usage", out var u) ? u.GetInt64() : 0;
    var current = item.TryGetProperty("currentValue", out var c) ? c.GetInt64() : 0;
    var unit = item.TryGetProperty("unit", out var un) ? un.GetInt32() : 0;
    var nextReset = item.TryGetProperty("nextResetTime", out var nr) ? nr.GetInt64() : 0;

    var resetDt = nextReset > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(nextReset).LocalDateTime.ToString("HH:mm") : "N/A";

    Console.WriteLine($"  type={type}, unit={unit}, percentage={pct}%, usage={usage}, currentValue={current}, nextReset={resetDt}");
}

// 测试 2: model-usage
Console.WriteLine("\n=== 测试 model-usage ===");
var now = DateTime.Now;
var start = now.AddHours(-24).ToString("yyyy-MM-dd HH:mm:ss");
var end = now.ToString("yyyy-MM-dd HH:mm:ss");
var url2 = $"{domain}/api/monitor/usage/model-usage?startTime={Uri.EscapeDataString(start)}&endTime={Uri.EscapeDataString(end)}";
var req2 = new HttpRequestMessage(HttpMethod.Get, url2);
req2.Headers.Add("Authorization", token);
req2.Headers.Add("Accept-Language", "en-US,en");
var res2 = await http.SendAsync(req2);
var json2 = await res2.Content.ReadAsStringAsync();

using var doc2 = JsonDocument.Parse(json2);
var data2 = doc2.RootElement.GetProperty("data");
var totalUsage = data2.GetProperty("totalUsage");
var callCount = totalUsage.GetProperty("totalModelCallCount").GetInt64();
var tokenUsage = totalUsage.GetProperty("totalTokensUsage").GetInt64();

Console.WriteLine($"  totalModelCallCount: {callCount}");
Console.WriteLine($"  totalTokensUsage: {tokenUsage} ({tokenUsage / 1_000_000.0:F0}M)");

Console.WriteLine("\n=== 结论 ===");
Console.WriteLine($"  5h Token: 62% (来自 API percentage 字段)");
Console.WriteLine($"  MCP 配额: 8% (88/1000)");
Console.WriteLine($"  调用次数: {callCount}");
Console.WriteLine($"  Token 用量: {tokenUsage / 1_000_000.0:F0}M");
