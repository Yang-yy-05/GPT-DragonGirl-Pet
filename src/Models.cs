using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace DragonPet;

public sealed class PetSettings
{
    public int Size { get; set; } = 245;
    public double? Left { get; set; }
    public double? Top { get; set; }
    public bool WatchApps { get; set; } = true;
    public bool Bubbles { get; set; } = true;
    public string BubbleFont { get; set; } = "@zcool-kuaile";
    public double BubbleFontSize { get; set; } = 16.5;
    public bool AlwaysOnTop { get; set; } = true;
    public bool ClickSound { get; set; } = true;
    public int SoundVolume { get; set; } = 55;
    public bool SnapToEdge { get; set; } = true;
    public bool Motion { get; set; } = true;
    public bool FullscreenQuiet { get; set; } = true;
    public bool QuotaEnabled { get; set; } = true;
    public bool FocusEnabled { get; set; } = true;
    public int FocusMinutes { get; set; } = 25;
    public int BreakMinutes { get; set; } = 5;
    public bool QuotaAlertsEnabled { get; set; } = true;
    public int QuotaAlertPercent { get; set; } = 20;
    public bool QuotaRecoveryEnabled { get; set; } = true;
    public bool TimeEggsEnabled { get; set; } = true;
    public bool MorningEnabled { get; set; } = true;
    public bool NightEnabled { get; set; } = true;
    public bool HolidayEnabled { get; set; } = true;
    public int BubbleSeconds { get; set; } = 9;
    public int CooldownSeconds { get; set; } = 90;
    public string CodexPath { get; set; } = "";
    public bool FirstRun { get; set; } = true;
    public void Normalize()
    {
        Size = Math.Clamp(Size, 150, 400);
        SoundVolume = Math.Clamp(SoundVolume, 0, 100);
        BubbleFont = string.IsNullOrWhiteSpace(BubbleFont) ? "@zcool-kuaile" : BubbleFont.Trim();
        BubbleFontSize = double.IsFinite(BubbleFontSize) ? Math.Clamp(BubbleFontSize, 12, 28) : 16.5;
        BubbleSeconds = Math.Clamp(BubbleSeconds, 3, 30);
        CooldownSeconds = Math.Clamp(CooldownSeconds, 15, 1800);
        FocusMinutes = Math.Clamp(FocusMinutes, 1, 180);
        BreakMinutes = Math.Clamp(BreakMinutes, 1, 60);
        QuotaAlertPercent = Math.Clamp(QuotaAlertPercent, 1, 99);
        CodexPath ??= "";
        if (Left.HasValue && !double.IsFinite(Left.Value)) Left = null;
        if (Top.HasValue && !double.IsFinite(Top.Value)) Top = null;
    }
}

public sealed class AppRule
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    // Process names are exact matches; title fragments are literal, case-insensitive matches.
    public string Processes { get; set; } = "";
    public string Titles { get; set; } = "";
    public string Expression { get; set; } = "default";
    public string Lines { get; set; } = "";
    public bool ShowQuota { get; set; }
    public string Source { get; set; } = "";
    public bool Matches(ForegroundInfo info)
    {
        return ProcessMatches(info) || TitleMatches(info);
    }
    public bool ProcessMatches(ForegroundInfo info) => Enabled && Split(Processes).Any(p => string.Equals(Path.GetFileNameWithoutExtension(p), Path.GetFileNameWithoutExtension(info.Process), StringComparison.OrdinalIgnoreCase));
    public bool TitleMatches(ForegroundInfo info) => Enabled && Split(Titles).Any(t => info.Title.Contains(t, StringComparison.OrdinalIgnoreCase));
    public static string[] Split(string? value) => (value ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public static class RuleMatcher
{
    static readonly HashSet<string> Hosts = new(StringComparer.OrdinalIgnoreCase) { "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "WindowsTerminal", "powershell", "pwsh", "cmd", "conhost", "ApplicationFrameHost" };
    public static AppRule? Match(IEnumerable<AppRule> rules, ForegroundInfo info)
    {
        // App identity wins over document names. Browsers/terminals are hosts: their active title wins.
        if (!Hosts.Contains(Path.GetFileNameWithoutExtension(info.Process)))
        {
            var native = rules.FirstOrDefault(r => r.ProcessMatches(info));
            if (native != null) return native;
        }
        return rules.FirstOrDefault(r => r.TitleMatches(info)) ?? rules.FirstOrDefault(r => r.ProcessMatches(info));
    }
}

public sealed record ForegroundInfo(string Process, string Title, bool Fullscreen = false);

public sealed class ReactionGate
{
    private string? activeId;
    private string? pendingId;
    private DateTimeOffset pendingSince;
    private readonly Dictionary<string, DateTimeOffset> last = new();
    public string? ActiveId => activeId;
    public bool Observe(string? id, DateTimeOffset now, int cooldown)
    {
        if (pendingId != id) { pendingId = id; pendingSince = now; return false; }
        if ((now - pendingSince).TotalMilliseconds < 800 || activeId == id) return false;
        activeId = id;
        if (id == null || (last.TryGetValue(id, out var time) && (now - time).TotalSeconds < cooldown)) return false;
        last[id] = now;
        return true;
    }
}

public sealed class ConfigStore
{
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    public string DirectoryPath { get; }
    public string Warning { get; private set; } = "";
    public ConfigStore(string? directory = null)
    {
        DirectoryPath = directory ?? Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(DirectoryPath);
    }
    public T Load<T>(string file, Func<T> fallback)
    {
        var path = Path.Combine(DirectoryPath, file);
        if (!File.Exists(path)) return fallback();
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) ?? fallback(); }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            Warning = "配置无法读取，已使用默认值。原文件已保留为 .invalid 备份。";
            File.Copy(path, path + ".invalid-" + DateTime.Now.ToString("yyyyMMddHHmmss"), true);
            return fallback();
        }
    }
    public void Save<T>(string file, T value)
    {
        var path = Path.Combine(DirectoryPath, file);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(value, Json));
        File.Move(path + ".tmp", path, true);
    }
    public static List<AppRule> DefaultRules() => JsonSerializer.Deserialize<List<AppRule>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "config/default-rules.json")), Json) ?? new();
    public static void ValidateRules(IEnumerable<AppRule> rules)
    {
        var ids = new HashSet<string>();
        foreach (var r in rules)
        {
            if (string.IsNullOrWhiteSpace(r.Id) || !ids.Add(r.Id)) throw new ArgumentException("规则 ID 不能为空或重复。");
            if (string.IsNullOrWhiteSpace(r.Name) || string.IsNullOrWhiteSpace(r.Lines)) throw new ArgumentException("每条规则需要名称和台词。");
            if (AppRule.Split(r.Processes).Length + AppRule.Split(r.Titles).Length == 0) throw new ArgumentException("每条规则至少需要一个进程名或标题关键词。");
            if (!Sprites.Names.ContainsKey(r.Expression)) throw new ArgumentException("未知表情：" + r.Expression);
        }
    }
}
