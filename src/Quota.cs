using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DragonPet;

public sealed record QuotaWindow(string Bucket, string Label, double Remaining, long? ResetsAt, double DurationMins = 0);
public sealed record QuotaSnapshot(List<QuotaWindow> Windows, DateTimeOffset FetchedAt, string Error = "")
{
    public bool IsStale(DateTimeOffset now) => Error.Length > 0 || now - FetchedAt > TimeSpan.FromMinutes(5);
    public string Format(DateTimeOffset now, int maxWindows = 4, bool compact = false)
    {
        if (Windows.Count == 0) return "额度暂不可用\n" + (Error.Length > 0 ? Error : "尚未查询，请稍等。 ");
        var stale = IsStale(now);
        var lines = Windows.Take(maxWindows).Select(w => {
            bool expired = w.ResetsAt.HasValue && w.ResetsAt <= now.ToUnixTimeSeconds();
            string value = expired ? "等待刷新" : $"剩余 {w.Remaining:0.#}%";
            string reset = !expired && w.ResetsAt.HasValue ? (compact ? "\n" : " · ") + ResetText(w.ResetsAt.Value, now) : "";
            return $"{w.Bucket} · {w.Label}  {value}{reset}";
        });
        string missing = Windows.Any(w => w.DurationMins >= 10080) && !Windows.Any(w => w.DurationMins > 0 && w.DurationMins < 1440) ? "\n短周期窗口：接口未返回" : "";
        return string.Join("\n", lines) + missing + $"\n{(stale ? "上次结果 · " : "更新于 ")}{FetchedAt.ToLocalTime():HH:mm}" + (stale ? "（待刷新）" : "");
    }
    static string ResetText(long timestamp, DateTimeOffset now)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, timestamp - now.ToUnixTimeSeconds()));
        return t.TotalDays >= 1 ? $"{(int)t.TotalDays}天{t.Hours}小时后重置" : t.TotalHours >= 1 ? $"{(int)t.TotalHours}小时{t.Minutes}分后重置" : $"{Math.Max(1, (int)t.TotalMinutes)}分后重置";
    }
}

public sealed class QuotaService : IDisposable
{
    private readonly PetSettings settings;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource shutdown = new();
    private DateTimeOffset attempted = DateTimeOffset.MinValue;
    public QuotaSnapshot Current { get; private set; } = new(new(), DateTimeOffset.MinValue);
    public QuotaService(PetSettings settings) { this.settings = settings; }
    public static List<QuotaWindow> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("result", out var result)) root = result;
        var windows = new List<QuotaWindow>();
        void Bucket(JsonElement bucket, string name)
        {
            if (bucket.ValueKind != JsonValueKind.Object) return;
            if (bucket.TryGetProperty("limitName", out var label) && label.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(label.GetString())) name = label.GetString()!;
            if (name.Equals("codex", StringComparison.OrdinalIgnoreCase)) name = "Codex";
            foreach (var key in new[] { "primary", "secondary" })
            {
                if (!bucket.TryGetProperty(key, out var w) || w.ValueKind != JsonValueKind.Object || !w.TryGetProperty("usedPercent", out var used) || used.ValueKind != JsonValueKind.Number || !used.TryGetDouble(out var number) || !double.IsFinite(number)) continue;
                double minutes = w.TryGetProperty("windowDurationMins", out var mins) && mins.ValueKind == JsonValueKind.Number && mins.TryGetDouble(out var m) ? m : 0;
                string duration = minutes >= 10080 ? $"{minutes / 10080:0.#}周" : minutes >= 1440 ? $"{minutes / 1440:0.#}天" : minutes >= 60 ? $"{minutes / 60:0.#}小时" : minutes > 0 ? $"{minutes:0}分钟" : key == "primary" ? "主窗口" : "次窗口";
                long? reset = w.TryGetProperty("resetsAt", out var time) && time.ValueKind == JsonValueKind.Number && time.TryGetInt64(out var r) && r >= 0 && r <= 253402300799 ? r : null;
                windows.Add(new(name, duration, Math.Clamp(100 - number, 0, 100), reset, minutes));
            }
        }
        if (root.TryGetProperty("rateLimitsByLimitId", out var map) && map.ValueKind == JsonValueKind.Object && map.EnumerateObject().Any())
            foreach (var pair in map.EnumerateObject()) Bucket(pair.Value, pair.Name);
        else if (root.TryGetProperty("rateLimits", out var single))
            Bucket(single, single.ValueKind == JsonValueKind.Object && single.TryGetProperty("limitId", out var id) ? id.GetString() ?? "Codex" : "Codex");
        return windows;
    }
    public static string? FindCodex(string configured = "")
    {
        if (!string.IsNullOrWhiteSpace(configured)) return File.Exists(configured) && Path.GetExtension(configured).Equals(".exe", StringComparison.OrdinalIgnoreCase) ? configured : null;
        foreach (var part in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            try { var file = Path.Combine(part.Trim('"'), "codex.exe"); if (File.Exists(file)) return file; } catch (ArgumentException) { }
        }
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
        if (Directory.Exists(root))
            return Directory.EnumerateFiles(root, "codex.exe", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        return null;
    }
    public async Task<QuotaSnapshot> Refresh(bool force = false)
    {
        if (!settings.QuotaEnabled) return new(new(), DateTimeOffset.UtcNow, "已暂停自动额度查询。");
        await gate.WaitAsync();
        try
        {
            if (!force && DateTimeOffset.UtcNow - attempted < TimeSpan.FromMinutes(3)) return Current;
            attempted = DateTimeOffset.UtcNow;
            var exe = FindCodex(settings.CodexPath);
            if (exe == null) throw new InvalidOperationException("未找到 Codex，请在额度设置中选择 codex.exe。");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = AppContext.BaseDirectory };
            start.ArgumentList.Add("app-server"); start.ArgumentList.Add("--listen"); start.ArgumentList.Add("stdio://");
            using var process = new Process { StartInfo = start };
            process.Start();
            // Drain stderr without retaining or logging potentially sensitive diagnostic text.
            process.ErrorDataReceived += (_, _) => { }; process.BeginErrorReadLine();
            try
            {
                async Task<JsonElement> Rpc(int id, string method, object? parameters)
                {
                    await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { id, method, @params = parameters }));
                    await process.StandardInput.FlushAsync();
                    while (true)
                    {
                        var line = await process.StandardOutput.ReadLineAsync(timeout.Token);
                        if (line == null) throw new InvalidOperationException("Codex 服务已退出，请检查登录状态。");
                        try
                        {
                            using var response = JsonDocument.Parse(line);
                            var root = response.RootElement;
                            if (!root.TryGetProperty("id", out var responseId) || !responseId.TryGetInt32(out var number) || number != id) continue;
                            if (root.TryGetProperty("error", out _)) throw new InvalidOperationException("官方额度接口未返回数据，请先在 Codex 中登录并检查网络。");
                            return root.Clone();
                        }
                        catch (JsonException) { }
                    }
                }
                await Rpc(1, "initialize", new { clientInfo = new { name = "gpt_dragon_pet", title = "GPT 小龙桌宠", version = "1.0.0" } });
                await process.StandardInput.WriteLineAsync("{\"method\":\"initialized\",\"params\":{}}");
                var response = await Rpc(2, "account/rateLimits/read", null);
                var windows = Parse(response.GetRawText());
                if (windows.Count == 0) throw new InvalidOperationException("此账号未返回订阅额度。API Key 登录可能没有订阅窗口。");
                Current = new(windows, DateTimeOffset.UtcNow);
            }
            finally
            {
                try { process.StandardInput.Close(); if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(); } catch (InvalidOperationException) { }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException or IOException or JsonException or System.ComponentModel.Win32Exception)
        {
            string error = ex is OperationCanceledException ? "查询超时，请稍后刷新。" : ex is InvalidOperationException ? ex.Message : "无法连接本机 Codex 服务。";
            Current = Current with { Error = error };
        }
        finally { gate.Release(); }
        return Current;
    }
    public void Dispose() { shutdown.Cancel(); }
}
