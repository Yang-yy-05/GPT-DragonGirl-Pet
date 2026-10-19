using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DragonPet;

public enum FocusPhase { Idle, Focus, Break, Ready }
public sealed class FocusState
{
    public FocusPhase Phase { get; set; }
    public double SecondsLeft { get; set; }
    public DateTimeOffset? Deadline { get; set; }
    public int Completed { get; set; }
}

public sealed class FocusClock
{
    public FocusState State { get; }
    public bool Running => State.Deadline.HasValue;
    public bool Concentrating => Running && State.Phase == FocusPhase.Focus;
    public FocusClock(FocusState? state = null) { State = state ?? new(); }
    public TimeSpan Remaining(DateTimeOffset now) => TimeSpan.FromSeconds(Math.Max(0, State.Deadline.HasValue ? (State.Deadline.Value - now).TotalSeconds : State.SecondsLeft));
    public void Start(DateTimeOffset now, int minutes)
    {
        State.Phase = FocusPhase.Focus; State.SecondsLeft = Math.Clamp(minutes, 1, 180) * 60; State.Deadline = now.AddSeconds(State.SecondsLeft);
    }
    public void Pause(DateTimeOffset now) { State.SecondsLeft = Remaining(now).TotalSeconds; State.Deadline = null; }
    public void Resume(DateTimeOffset now)
    {
        if (!Running && State.Phase is FocusPhase.Focus or FocusPhase.Break) State.Deadline = now.AddSeconds(State.SecondsLeft);
    }
    public void Reset() { State.Phase = FocusPhase.Idle; State.SecondsLeft = 0; State.Deadline = null; }
    // At most one transition per tick, even after a long sleep. Rest begins when the app resumes.
    public FocusPhase? Tick(DateTimeOffset now, int breakMinutes)
    {
        if (!Running || Remaining(now) > TimeSpan.Zero) return null;
        if (State.Phase == FocusPhase.Focus)
        {
            State.Completed++; State.Phase = FocusPhase.Break; State.SecondsLeft = Math.Clamp(breakMinutes, 1, 60) * 60;
            State.Deadline = now.AddSeconds(State.SecondsLeft);
        }
        else { State.Phase = FocusPhase.Ready; State.SecondsLeft = 0; State.Deadline = null; }
        return State.Phase;
    }
    public string Status(DateTimeOffset now)
    {
        if (State.Phase == FocusPhase.Idle) return "等待开始";
        if (State.Phase == FocusPhase.Ready) return "休息结束 · 等你开启下一轮";
        int seconds = (int)Math.Ceiling(Remaining(now).TotalSeconds);
        return $"{(State.Phase == FocusPhase.Focus ? "专注" : "休息")} {seconds / 60:00}:{seconds % 60:00}{(Running ? "" : " · 已暂停")}";
    }
}

public sealed record CompanionMessage(string Text, string Expression, string Caption);
public sealed class QuotaAlertMemory
{
    public DateTimeOffset LastFetched { get; set; }
    public bool LowReported { get; set; }
    public double Remaining { get; set; }
    public long? Reset { get; set; }
    public int Threshold { get; set; }
}
public sealed class CompanionState
{
    public FocusState Focus { get; set; } = new();
    public Dictionary<string, QuotaAlertMemory> Quota { get; set; } = new();
    public Dictionary<string, string> Moments { get; set; } = new();
}

public static class QuotaAlerts
{
    public static List<CompanionMessage> Evaluate(QuotaSnapshot snapshot, PetSettings settings, Dictionary<string, QuotaAlertMemory> memory, DateTimeOffset now)
    {
        var messages = new List<CompanionMessage>();
        if (!settings.QuotaEnabled || !settings.QuotaAlertsEnabled || snapshot.IsStale(now) || snapshot.FetchedAt > now.AddMinutes(1)) return messages;
        var lows = new List<string>(); var recovered = new List<string>();
        foreach (var window in snapshot.Windows)
        {
            if (!double.IsFinite(window.Remaining) || window.ResetsAt <= now.ToUnixTimeSeconds()) continue;
            string key = window.Bucket + "|" + window.Label + "|" + window.DurationMins.ToString(CultureInfo.InvariantCulture);
            if (!memory.TryGetValue(key, out var previous)) memory[key] = previous = new();
            if (snapshot.FetchedAt < previous.LastFetched || (snapshot.FetchedAt == previous.LastFetched && previous.Threshold == settings.QuotaAlertPercent)) continue;
            bool low = window.Remaining <= settings.QuotaAlertPercent;
            bool wasLow = previous.LowReported;
            bool newPeriod = previous.Reset.HasValue && window.ResetsAt.HasValue && window.ResetsAt > previous.Reset;
            if (low && (!wasLow || newPeriod)) lows.Add($"{window.Bucket} · {window.Label} 剩余 {window.Remaining:0.#}%");
            // Recovery requires a new successful API observation above the threshold, never the reset clock alone.
            if (!low && wasLow && window.Remaining > previous.Remaining && snapshot.FetchedAt > previous.LastFetched && settings.QuotaRecoveryEnabled)
                recovered.Add($"{window.Bucket} · {window.Label} 剩余 {window.Remaining:0.#}%");
            previous.LowReported = low; previous.LastFetched = snapshot.FetchedAt; previous.Remaining = window.Remaining;
            previous.Reset = window.ResetsAt; previous.Threshold = settings.QuotaAlertPercent;
        }
        if (lows.Count > 0) messages.Add(new("额度快到提醒线啦！\n" + string.Join("\n", lows) + $"\n提醒线：剩余 ≤ {settings.QuotaAlertPercent}%", "surprised", "Codex · 低额度提醒"));
        if (recovered.Count > 0) messages.Add(new("额度又多起来啦！\n" + string.Join("\n", recovered), "smug", "Codex · 额度恢复"));
        return messages;
    }
}

public sealed record TimeMoment(string Slot, string Key, CompanionMessage Message);
public static class TimeEggs
{
    public static string? Holiday(DateTime local)
    {
        if (local.Month == 1 && local.Day == 1) return "newyear";
        if (local.Month == 12 && local.Day is >= 24 and <= 26) return "christmas";
        var lunar = new ChineseLunisolarCalendar();
        if (local.Date >= lunar.MinSupportedDateTime && local.Date <= lunar.MaxSupportedDateTime && lunar.GetMonth(local) == 1 && lunar.GetDayOfMonth(local) <= 7) return "spring";
        return null;
    }
    public static string? Outfit(PetSettings settings, DateTime local) => settings.TimeEggsEnabled && settings.HolidayEnabled
        ? Holiday(local) switch { "newyear" or "spring" => "festive", "christmas" => "christmas", _ => null } : null;
    public static TimeMoment? Next(PetSettings settings, Dictionary<string, string> memory, DateTime local)
    {
        if (!settings.TimeEggsEnabled) return null;
        var moments = new List<TimeMoment>();
        var holiday = settings.HolidayEnabled ? Holiday(local) : null;
        if (holiday != null) moments.Add(new("holiday", $"{local.Year}-{holiday}", new(
            holiday == "christmas" ? "叮叮当，小龙换上冬日装啦！\n愿你今天也收到一点小惊喜。" : "新年好呀！小龙穿上新衣来拜年。\n愿你想做的事，都慢慢开花。", holiday == "christmas" ? "christmas" : "festive", holiday == "christmas" ? "圣诞小彩蛋" : "新年小彩蛋")));
        if (settings.MorningEnabled && local.Hour is >= 6 and < 11) moments.Add(new("morning", local.ToString("yyyy-MM-dd"), new("唔……早安呀。\n揉揉眼睛，和你一起慢慢醒来。", "morning", "早安 · 小龙醒啦")));
        if (settings.NightEnabled && (local.Hour >= 23 || local.Hour < 5)) moments.Add(new("night", (local.Hour < 5 ? local.AddDays(-1) : local).ToString("yyyy-MM-dd"), new("哈欠……小龙的眼皮打架啦。\n忙完这一点，就一起早点休息吧。", "sleepy", "深夜 · 困困小龙")));
        return moments.FirstOrDefault(m => memory.GetValueOrDefault(m.Slot) != m.Key);
    }
}
