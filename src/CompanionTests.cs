using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DragonPet;

public static class CompanionTests
{
    static void Assert(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
    public static void Run(Action<string, Action> test)
    {
        var now = new DateTimeOffset(2026, 10, 19, 8, 0, 0, TimeSpan.FromHours(8));
        QuotaSnapshot Snapshot(double remaining, DateTimeOffset at, long? reset = null) => new(new() { new("Codex", "1周", remaining, reset, 10080) }, at);
        test("Companion settings migrate without starting a timer", () => {
            var s = JsonSerializer.Deserialize<PetSettings>("{\"Size\":220}")!;
            Assert(s.FocusEnabled && s.FocusMinutes == 25 && s.BreakMinutes == 5 && s.QuotaAlertsEnabled && s.QuotaAlertPercent == 20 && s.TimeEggsEnabled);
            Assert(!new FocusClock().Running && new FocusClock().State.Phase == FocusPhase.Idle);
        });
        test("Companion settings bounds and switches persist", () => {
            var s = new PetSettings { FocusMinutes = -1, BreakMinutes = 100, QuotaAlertPercent = 200, FocusEnabled = false, TimeEggsEnabled = false, QuotaAlertsEnabled = false };
            s.Normalize(); var copy = JsonSerializer.Deserialize<PetSettings>(JsonSerializer.Serialize(s))!;
            Assert(copy.FocusMinutes == 1 && copy.BreakMinutes == 60 && copy.QuotaAlertPercent == 99 && !copy.FocusEnabled && !copy.TimeEggsEnabled && !copy.QuotaAlertsEnabled);
        });
        test("Focus pause freezes time and resume preserves remainder", () => {
            var clock = new FocusClock(); clock.Start(now, 25); clock.Pause(now.AddMinutes(7));
            Assert(clock.Remaining(now.AddHours(2)) == TimeSpan.FromMinutes(18)); clock.Resume(now.AddHours(2));
            Assert(clock.Remaining(now.AddHours(2).AddMinutes(3)) == TimeSpan.FromMinutes(15));
        });
        test("Focus transitions exactly once and next round is manual", () => {
            var clock = new FocusClock(); clock.Start(now, 1); Assert(clock.Tick(now.AddSeconds(59), 5) == null);
            Assert(clock.Tick(now.AddMinutes(1), 5) == FocusPhase.Break && clock.State.Completed == 1);
            Assert(clock.Tick(now.AddMinutes(1), 5) == null);
            Assert(clock.Tick(now.AddMinutes(6), 5) == FocusPhase.Ready && !clock.Running);
            Assert(clock.Tick(now.AddDays(1), 5) == null && clock.State.Completed == 1);
        });
        test("Long sleep starts only one fresh break", () => {
            var clock = new FocusClock(); clock.Start(now, 25); clock.Tick(now.AddHours(4), 8);
            Assert(clock.State.Completed == 1 && clock.Remaining(now.AddHours(4)) == TimeSpan.FromMinutes(8));
        });
        test("Duration edits affect next phase without resetting current work", () => {
            var clock = new FocusClock(); clock.Start(now, 25); clock.Tick(now.AddMinutes(10), 12);
            Assert(clock.Remaining(now.AddMinutes(10)) == TimeSpan.FromMinutes(15)); clock.Tick(now.AddMinutes(25), 12);
            Assert(clock.Remaining(now.AddMinutes(25)) == TimeSpan.FromMinutes(12));
        });
        test("Focus reset clears session but retains completion count", () => {
            var clock = new FocusClock(); clock.Start(now, 1); clock.Tick(now.AddMinutes(1), 5); clock.Reset();
            Assert(!clock.Running && clock.State.Phase == FocusPhase.Idle && clock.State.Completed == 1);
        });
        test("Paused focus survives local persistence", () => {
            var clock = new FocusClock(); clock.Start(now, 25); clock.Pause(now.AddMinutes(4));
            var state = JsonSerializer.Deserialize<CompanionState>(JsonSerializer.Serialize(new CompanionState { Focus = clock.State }))!;
            var restored = new FocusClock(state.Focus); Assert(!restored.Running && restored.Remaining(now.AddDays(1)) == TimeSpan.FromMinutes(21));
        });
        test("Quota threshold boundary alerts once across repeated polls", () => {
            var memory = new Dictionary<string, QuotaAlertMemory>(); var s = new PetSettings();
            Assert(QuotaAlerts.Evaluate(Snapshot(20, now), s, memory, now).Count == 1);
            Assert(QuotaAlerts.Evaluate(Snapshot(10, now.AddMinutes(3)), s, memory, now.AddMinutes(3)).Count == 0);
        });
        test("Quota alert dedupe survives restart", () => {
            var memory = new Dictionary<string, QuotaAlertMemory>(); var s = new PetSettings(); QuotaAlerts.Evaluate(Snapshot(10, now), s, memory, now);
            var restored = JsonSerializer.Deserialize<Dictionary<string, QuotaAlertMemory>>(JsonSerializer.Serialize(memory))!;
            Assert(QuotaAlerts.Evaluate(Snapshot(9, now.AddMinutes(3)), s, restored, now.AddMinutes(3)).Count == 0);
        });
        test("Quota fresh recovery rearms a later low alert", () => {
            var memory = new Dictionary<string, QuotaAlertMemory>(); var s = new PetSettings(); QuotaAlerts.Evaluate(Snapshot(10, now), s, memory, now);
            var recovery = QuotaAlerts.Evaluate(Snapshot(70, now.AddMinutes(3)), s, memory, now.AddMinutes(3));
            Assert(recovery.Count == 1 && recovery[0].Caption.Contains("恢复"));
            Assert(QuotaAlerts.Evaluate(Snapshot(18, now.AddMinutes(6)), s, memory, now.AddMinutes(6)).Count == 1);
        });
        test("Stale failed future and missing quota never alert", () => {
            var memory = new Dictionary<string, QuotaAlertMemory>(); var s = new PetSettings();
            Assert(QuotaAlerts.Evaluate(Snapshot(1, now.AddMinutes(-6)), s, memory, now).Count == 0);
            Assert(QuotaAlerts.Evaluate(Snapshot(1, now) with { Error = "offline" }, s, memory, now).Count == 0);
            Assert(QuotaAlerts.Evaluate(Snapshot(1, now.AddHours(1)), s, memory, now).Count == 0);
            Assert(QuotaAlerts.Evaluate(new(new(), now), s, memory, now).Count == 0 && memory.Count == 0);
        });
        test("Reset time alone does not announce recovery", () => {
            var memory = new Dictionary<string, QuotaAlertMemory>(); var s = new PetSettings();
            QuotaAlerts.Evaluate(Snapshot(1, now, now.AddMinutes(1).ToUnixTimeSeconds()), s, memory, now);
            Assert(QuotaAlerts.Evaluate(Snapshot(100, now.AddMinutes(2), now.AddMinutes(1).ToUnixTimeSeconds()), s, memory, now.AddMinutes(2)).Count == 0);
        });
        test("New quota period may alert once even if first sample is already low", () => {
            var memory = new Dictionary<string, QuotaAlertMemory>(); var s = new PetSettings();
            QuotaAlerts.Evaluate(Snapshot(1, now, now.AddMinutes(1).ToUnixTimeSeconds()), s, memory, now);
            Assert(QuotaAlerts.Evaluate(Snapshot(12, now.AddMinutes(3), now.AddDays(7).ToUnixTimeSeconds()), s, memory, now.AddMinutes(3)).Count == 1);
        });
        test("Quota threshold edits reevaluate fresh cached data", () => {
            var memory = new Dictionary<string, QuotaAlertMemory>(); var s = new PetSettings(); var snapshot = Snapshot(30, now);
            Assert(QuotaAlerts.Evaluate(snapshot, s, memory, now).Count == 0); s.QuotaAlertPercent = 40;
            Assert(QuotaAlerts.Evaluate(snapshot, s, memory, now).Count == 1);
        });
        test("Quota switches disable alerts and optional recovery", () => {
            var memory = new Dictionary<string, QuotaAlertMemory>(); var s = new PetSettings { QuotaAlertsEnabled = false };
            Assert(QuotaAlerts.Evaluate(Snapshot(1, now), s, memory, now).Count == 0);
            s.QuotaAlertsEnabled = true; s.QuotaEnabled = false; Assert(QuotaAlerts.Evaluate(Snapshot(1, now), s, memory, now).Count == 0);
            s.QuotaEnabled = true; s.QuotaRecoveryEnabled = false; QuotaAlerts.Evaluate(Snapshot(1, now), s, memory, now);
            Assert(QuotaAlerts.Evaluate(Snapshot(100, now.AddMinutes(3)), s, memory, now.AddMinutes(3)).Count == 0);
        });
        test("Short and weekly quota windows are both represented", () => {
            var snapshot = Snapshot(10, now); snapshot.Windows.Add(new("Codex", "5小时", 8, null, 300));
            var result = QuotaAlerts.Evaluate(snapshot, new(), new(), now);
            Assert(result.Count == 1 && result[0].Text.Contains("1周") && result[0].Text.Contains("5小时"));
        });
        test("Morning greeting once per local date within time range", () => {
            var s = new PetSettings(); var memory = new Dictionary<string, string>(); var date = new DateTime(2026, 10, 19, 6, 0, 0);
            Assert(TimeEggs.Next(s, memory, date.AddSeconds(-1)) == null);
            var first = TimeEggs.Next(s, memory, date)!; Assert(first.Message.Expression == "morning"); memory[first.Slot] = first.Key;
            Assert(TimeEggs.Next(s, memory, date.AddHours(3)) == null); Assert(TimeEggs.Next(s, memory, date.AddDays(1)) != null);
            Assert(TimeEggs.Next(s, new(), date.AddHours(5)) == null);
        });
        test("Night greeting does not repeat after midnight or restart", () => {
            var date = new DateTime(2026, 10, 19, 23, 0, 0); var memory = new Dictionary<string, string>(); var s = new PetSettings();
            var first = TimeEggs.Next(s, memory, date)!; memory[first.Slot] = first.Key;
            memory = JsonSerializer.Deserialize<Dictionary<string, string>>(JsonSerializer.Serialize(memory))!;
            Assert(TimeEggs.Next(s, memory, date.AddHours(2)) == null); Assert(TimeEggs.Next(s, memory, date.AddDays(1)) != null);
            Assert(TimeEggs.Next(s, new(), date.AddHours(6)) == null);
        });
        test("Time egg master and individual switches", () => {
            var s = new PetSettings { MorningEnabled = false }; Assert(TimeEggs.Next(s, new(), new(2026, 10, 19, 8, 0, 0)) == null);
            s.NightEnabled = false; Assert(TimeEggs.Next(s, new(), new(2026, 10, 19, 23, 0, 0)) == null);
            s.TimeEggsEnabled = false; Assert(TimeEggs.Next(s, new(), new(2026, 12, 25)) == null && TimeEggs.Outfit(s, new(2026, 12, 25)) == null);
        });
        test("Holiday outfits use fixed dates and real lunar calendar", () => {
            Assert(TimeEggs.Holiday(new(2026, 1, 1)) == "newyear" && TimeEggs.Holiday(new(2026, 12, 25)) == "christmas");
            var calendar = new System.Globalization.ChineseLunisolarCalendar(); var spring = calendar.ToDateTime(2026, 1, 1, 12, 0, 0, 0);
            Assert(TimeEggs.Holiday(spring) == "spring" && TimeEggs.Holiday(spring.AddDays(6)) == "spring" && TimeEggs.Holiday(spring.AddDays(7)) == null);
            Assert(TimeEggs.Holiday(new(2026, 10, 19)) == null);
        });
        test("Holiday greeting once per holiday and outfit remains available", () => {
            var s = new PetSettings(); var memory = new Dictionary<string, string>(); var date = new DateTime(2026, 12, 24, 12, 0, 0);
            var first = TimeEggs.Next(s, memory, date)!; memory[first.Slot] = first.Key;
            Assert(TimeEggs.Next(s, memory, date.AddDays(1)) == null && TimeEggs.Outfit(s, date.AddDays(1)) == "christmas");
            s.HolidayEnabled = false; Assert(TimeEggs.Outfit(s, date) == null);
        });
        test("Generated PNG alpha and dark outlines are preserved exactly", () => {
            foreach (var (key, file) in new[] { ("morning", "早安揉眼睛.png"), ("sleepy", "深夜困困.png"), ("festive", "新年灯笼装.png"), ("christmas", "圣诞礼物装.png") })
            {
                var raw = new BitmapImage(); raw.BeginInit(); raw.UriSource = new Uri(Path.Combine(AppContext.BaseDirectory, "图片", "时间彩蛋", file)); raw.CacheOption = BitmapCacheOption.OnLoad; raw.DecodePixelWidth = 640; raw.EndInit();
                var converted = new FormatConvertedBitmap(raw, PixelFormats.Bgra32, null, 0); var expected = new byte[converted.PixelWidth * converted.PixelHeight * 4]; converted.CopyPixels(expected, converted.PixelWidth * 4, 0);
                var actual = new byte[expected.Length]; Sprites.Get(key).CopyPixels(actual, converted.PixelWidth * 4, 0);
                Assert(expected.SequenceEqual(actual), key + " alpha changed");
                Assert(actual[3] == 0 && Enumerable.Range(0, actual.Length / 4).Any(i => actual[i * 4 + 3] >= 250), key + " missing alpha or opaque character");
            }
        });
    }
}
