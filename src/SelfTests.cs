using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;

namespace DragonPet;

public static class SelfTests
{
    public static int Run()
    {
        var results = new List<string>(); int failures = 0;
        void Test(string name, Action test)
        {
            try { test(); results.Add("PASS " + name); }
            catch (Exception ex) { failures++; results.Add("FAIL " + name + ": " + ex.Message); }
        }
        void Assert(bool condition, string detail = "Assertion failed") { if (!condition) throw new Exception(detail); }
        var rules = ConfigStore.DefaultRules();
        CompanionTests.Run(Test);
        Test("Default rules validate", () => ConfigStore.ValidateRules(rules));
        string? Match(string process, string title) => RuleMatcher.Match(rules, new(process, title))?.Id;
        Test("Native Codex", () => Assert(Match("Codex.exe", "chat") == "codex"));
        Test("ChatGPT browser", () => Assert(Match("msedge", "ChatGPT - Microsoft Edge") == "chatgpt"));
        Test("Bilibili browser", () => Assert(Match("chrome", "视频 - 哔哩哔哩_bilibili") == "bilibili"));
        Test("Harness precedes DeepSeek", () => Assert(Match("chrome", "DeepSeek Harness") == "deepseek-harness"));
        Test("DSH native", () => Assert(Match("dsh", "terminal") == "deepseek-harness"));
        Test("DeepSeek web", () => Assert(Match("chrome", "DeepSeek") == "deepseek"));
        Test("Steam exact process", () => Assert(Match("STEAM.EXE", "Library") == "steam"));
        Test("Substring process does not match", () => Assert(Match("notsteam", "untitled") == null));
        Test("VS Code", () => Assert(Match("Code", "main.cs") == "vscode"));
        Test("Document titles do not hijack native identity", () => Assert(Match("Code", "ChatGPT.md - Visual Studio Code") == "vscode"));
        Test("ChatGPT discussion title does not hijack identity", () => Assert(Match("ChatGPT", "DeepSeek Harness discussion") == "chatgpt"));
        Test("Terminal hosted Harness recognized", () => Assert(Match("WindowsTerminal", "DeepSeek Harness") == "deepseek-harness"));
        Test("Generic terminal falls back", () => Assert(Match("pwsh", "PowerShell") == "terminal"));
        Test("Office", () => Assert(Match("EXCEL", "Book1") == "office"));
        Test("Unknown app", () => Assert(Match("custom", "untitled") == null));
        Test("No generic Java game false positive", () => Assert(Match("javaw", "work") == null));
        Test("Disabled rules", () => Assert(!new AppRule { Enabled = false, Processes = "steam" }.Matches(new("steam", ""))));
        Test("Empty title fragments never match everything", () => Assert(!new AppRule { Titles = " | " }.Matches(new("x", "anything"))));
        Test("Title matching is literal, not regex", () => Assert(new AppRule { Titles = "[test]" }.Matches(new("x", "x [test] y"))));
        Test("Required phrases exact", () => Assert(rules.First(r => r.Id == "bilibili").Lines == "你所热爱的就是你的生活" && rules.First(r => r.Id == "deepseek-harness").Lines == "好模型"));
        var epoch = DateTimeOffset.UtcNow;
        Test("Debounce and no repeated foreground events", () => {
            var gate = new ReactionGate(); Assert(!gate.Observe("a", epoch, 90)); Assert(!gate.Observe("a", epoch.AddMilliseconds(400), 90)); Assert(gate.Observe("a", epoch.AddSeconds(1), 90)); Assert(!gate.Observe("a", epoch.AddSeconds(100), 90));
        });
        Test("Cooldown across app changes", () => {
            var gate = new ReactionGate(); gate.Observe("a", epoch, 90); gate.Observe("a", epoch.AddSeconds(1), 90); gate.Observe("b", epoch.AddSeconds(2), 90); Assert(gate.Observe("b", epoch.AddSeconds(3), 90)); gate.Observe("a", epoch.AddSeconds(4), 90); Assert(!gate.Observe("a", epoch.AddSeconds(5), 90));
        });
        Test("Returning through unknown app after cooldown", () => {
            var gate = new ReactionGate(); gate.Observe("a", epoch, 90); gate.Observe("a", epoch.AddSeconds(1), 90); gate.Observe(null, epoch.AddSeconds(2), 90); gate.Observe(null, epoch.AddSeconds(3), 90); gate.Observe("a", epoch.AddSeconds(100), 90); Assert(gate.Observe("a", epoch.AddSeconds(101), 90));
        });
        Test("Quick alt-tab does not trigger", () => {
            var gate = new ReactionGate(); gate.Observe("a", epoch, 90); gate.Observe("b", epoch.AddMilliseconds(300), 90); Assert(!gate.Observe("a", epoch.AddMilliseconds(500), 90));
        });
        Test("Quota remaining and real window lengths", () => {
            var w = QuotaService.Parse("{\"rateLimits\":{\"limitId\":\"codex\",\"primary\":{\"usedPercent\":25,\"windowDurationMins\":300,\"resetsAt\":2000000000},\"secondary\":{\"usedPercent\":40,\"windowDurationMins\":10080}}}");
            Assert(w.Count == 2 && w[0].Remaining == 75 && w[0].Label == "5小时" && w[1].Label == "1周");
        });
        Test("Multi-bucket overrides legacy", () => {
            var w = QuotaService.Parse("{\"result\":{\"rateLimits\":{\"primary\":{\"usedPercent\":90}},\"rateLimitsByLimitId\":{\"codex\":{\"primary\":{\"usedPercent\":20}},\"fast\":{\"primary\":{\"usedPercent\":30}}}}}"); Assert(w.Count == 2 && w[0].Remaining == 80 && w[1].Remaining == 70);
        });
        Test("Empty map falls back", () => Assert(QuotaService.Parse("{\"rateLimitsByLimitId\":{},\"rateLimits\":{\"primary\":{\"usedPercent\":1}}}").Count == 1));
        Test("Unknown usage is not zero", () => Assert(QuotaService.Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":null},\"secondary\":null}}").Count == 0));
        Test("Wrong usage type ignored", () => Assert(QuotaService.Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":\"oops\"}}}").Count == 0));
        Test("Zero percent is preserved", () => Assert(QuotaService.Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":100}}}")[0].Remaining == 0));
        Test("Percent clamped", () => Assert(QuotaService.Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":105},\"secondary\":{\"usedPercent\":-2}}}").Select(w => w.Remaining).SequenceEqual(new double[] { 0, 100 })));
        Test("Nullable metadata safe", () => Assert(QuotaService.Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":30,\"windowDurationMins\":null,\"resetsAt\":null}}}")[0].ResetsAt == null));
        Test("Expired reset never fabricates full quota", () => {
            var q = new QuotaSnapshot(new() { new("Codex", "5小时", 0, epoch.AddSeconds(-1).ToUnixTimeSeconds()) }, epoch); Assert(q.Format(epoch).Contains("等待刷新") && !q.Format(epoch).Contains("100%"));
        });
        Test("Cached failure clearly marked", () => Assert(new QuotaSnapshot(new() { new("Codex", "5小时", 75, null) }, epoch, "offline").Format(epoch).Contains("上次结果")));
        Test("Old data clearly marked", () => Assert(new QuotaSnapshot(new() { new("Codex", "5小时", 75, null) }, epoch.AddMinutes(-6)).IsStale(epoch)));
        Test("Missing short window is explicit", () => Assert(new QuotaSnapshot(new() { new("Codex", "1周", 91, null, 10080) }, epoch).Format(epoch).Contains("短周期窗口：接口未返回")));
        Test("Settings bounds", () => { var s = new PetSettings { Size = 9999, BubbleSeconds = -2, CooldownSeconds = 1, Left = double.NaN }; s.Normalize(); Assert(s.Size == 400 && s.BubbleSeconds == 3 && s.CooldownSeconds == 15 && s.Left == null); });
        Test("Old settings receive default bubble typography", () => {
            var s = JsonSerializer.Deserialize<PetSettings>("{\"Size\":220}")!; s.Normalize(); Assert(s.BubbleFont == BubbleFonts.DefaultId && s.BubbleFontSize == 16.5);
        });
        Test("Bubble typography bounds and unavailable-font fallback", () => {
            var s = new PetSettings { BubbleFont = " ", BubbleFontSize = double.NaN }; s.Normalize(); Assert(s.BubbleFont == BubbleFonts.DefaultId && s.BubbleFontSize == 16.5);
            s.BubbleFontSize = 99; s.Normalize(); Assert(s.BubbleFontSize == 28); s.BubbleFontSize = 1; s.Normalize(); Assert(s.BubbleFontSize == 12);
            Assert(ReferenceEquals(BubbleFonts.Resolve("missing-font-self-test"), Ui.PetFont));
            Assert(BubbleFonts.Available.Any(f => f.Id == "Microsoft YaHei UI") && BubbleFonts.Resolve("Microsoft YaHei UI").Source == "Microsoft YaHei UI");
        });
        Test("Position clamped on negative-coordinate monitor", () => Assert(Native.ClampPosition(new(-2100, -50), new(340, 440), new(-1920, 0, 1920, 1040)) == new Point(-1920, 0)));
        Test("Position clamped after monitor removal", () => Assert(Native.ClampPosition(new(3000, 3000), new(340, 440), new(0, 0, 1920, 1040)) == new Point(1580, 600)));
        Test("All artwork loads and background corners transparent", () => { foreach (var key in Sprites.Names.Keys) { var b = Sprites.Get(key); var pixel = new byte[4]; b.CopyPixels(new Int32Rect(0, 0, 1, 1), pixel, 4, 0); Assert(pixel[3] == 0, key); } });
        Test("White hair remains opaque", () => { var b = Sprites.Get("default"); var pixel = new byte[4]; b.CopyPixels(new Int32Rect(b.PixelWidth / 2, b.PixelHeight / 4, 1, 1), pixel, 4, 0); Assert(pixel[3] == 255); });
        Test("Rules reject duplicate IDs", () => { try { ConfigStore.ValidateRules(new[] { rules[0], rules[0] }); throw new Exception("did not reject"); } catch (ArgumentException) { } });
        Test("Rules reject unknown expression", () => { try { ConfigStore.ValidateRules(new[] { new AppRule { Id = "test", Name = "test", Processes = "x", Lines = "x", Expression = "missing" } }); throw new Exception("did not reject"); } catch (ArgumentException) { } });
        Test("Atomic settings persistence and malformed-file recovery", () => {
            var directory = Path.Combine(AppContext.BaseDirectory, "data", "selftest-" + Guid.NewGuid().ToString("N"));
            var store = new ConfigStore(directory); store.Save("settings.json", new PetSettings { Size = 333, BubbleFont = "Microsoft YaHei UI", BubbleFontSize = 23.5 });
            var saved = store.Load("settings.json", () => new PetSettings()); Assert(saved.Size == 333 && saved.BubbleFont == "Microsoft YaHei UI" && saved.BubbleFontSize == 23.5);
            File.WriteAllText(Path.Combine(directory, "settings.json"), "broken"); Assert(store.Load("settings.json", () => new PetSettings()).Size == 245 && store.Warning.Length > 0 && Directory.GetFiles(directory, "*.invalid-*").Length == 1);
        });
        Test("Click audio migration and volume bounds", () => {
            var s = JsonSerializer.Deserialize<PetSettings>("{\"Size\":245}")!; Assert(s.ClickSound && s.SoundVolume == 55); s.SoundVolume = 999; s.Normalize(); Assert(s.SoundVolume == 100); s.SoundVolume = -5; s.Normalize(); Assert(s.SoundVolume == 0);
        });
        Test("Extracted video sound is unchanged at full volume", () => {
            var original = File.ReadAllBytes(DuckSound.SoundPath); var wav = DuckSound.LoadWave(100); var data = DuckSound.PcmData(wav);
            Assert(wav.SequenceEqual(original) && BitConverter.ToInt32(wav, 24) == 48000 && data.Length == 45312);
            Assert(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(wav.AsSpan(data.Offset, data.Length))).Equals("cf382ab348633f3ed832fcbcbd19d871c5ec2898d89316250b2f62dbe7cbb764", StringComparison.OrdinalIgnoreCase));
        });
        Test("Video sound loads in Windows audio player", () => {
            using var stream = new MemoryStream(DuckSound.LoadWave(55)); using var player = new System.Media.SoundPlayer(stream); player.Load(); Assert(player.IsLoadCompleted);
        });
        Test("Click sound onset is within 5 ms", () => {
            var wav = DuckSound.LoadWave(100); var data = DuckSound.PcmData(wav); int first = -1;
            for (int i = 0; i < data.Length / 2; i++) if (Math.Abs((int)BitConverter.ToInt16(wav, data.Offset + i * 2)) >= 1000) { first = i; break; }
            Assert(first >= 0 && first < 240, "Unexpected leading silence before the original sound.");
        });
        Test("Audio can be preloaded before interaction and after volume changes", () => {
            using var sound = new DuckSound(); Assert(sound.Prepare(55) && sound.Prepare(55)); Assert(sound.Prepare(100) && sound.Prepare(0) && sound.Prepare(55)); Assert(sound.LastError == null);
        });
        Test("Zero volume WAV is silent and retains format", () => {
            var wav = DuckSound.LoadWave(0); var data = DuckSound.PcmData(wav); Assert(wav.Skip(data.Offset).Take(data.Length).All(x => x == 0));
        });
        Test("Volume only attenuates samples without changing timing", () => {
            var original = DuckSound.LoadWave(100); var quiet = DuckSound.LoadWave(50); var data = DuckSound.PcmData(original);
            Assert(original.Length == quiet.Length && original.Take(data.Offset).SequenceEqual(quiet.Take(data.Offset)));
            for (int offset = data.Offset; offset < data.Offset + data.Length; offset += 2)
                Assert(Math.Abs(BitConverter.ToInt16(quiet, offset) * 2 - BitConverter.ToInt16(original, offset)) <= 1);
        });
        Test("Truncated sound file is rejected", () => {
            var wav = DuckSound.LoadWave(); try { DuckSound.WithVolume(wav.Take(wav.Length - 1).ToArray(), 55); throw new Exception("did not reject"); } catch (InvalidDataException) { }
        });
        Test("Native topmost can be enabled and disabled", () => {
            var window = new Window { ShowInTaskbar = false, ShowActivated = false, Width = 100, Height = 100 };
            try { new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle(); Native.SetAlwaysOnTop(window, true); Assert(Native.IsAlwaysOnTop(window)); Native.SetAlwaysOnTop(window, false); Assert(!Native.IsAlwaysOnTop(window)); } finally { window.Close(); }
        });
        Test("Oval bubble keeps short and multiline text inside its outline", () => {
            var face = new System.Windows.Media.Typeface(Ui.PetFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            Assert(face.TryGetGlyphTypeface(out var glyph) && glyph.FontUri.LocalPath.EndsWith("ZCOOLKuaiLe-Regular.ttf", StringComparison.OrdinalIgnoreCase), "Bundled pet font did not load.");
            var quotaExample = new QuotaSnapshot(new() { new("Codex", "5小时", 75, epoch.AddHours(2).ToUnixTimeSeconds(), 300), new("Codex", "1周", 91, epoch.AddDays(6).AddHours(16).ToUnixTimeSeconds(), 10080) }, epoch).Format(epoch, compact: true);
            var examples = new[] { ("short", "好模型", "DeepSeek Harness"), ("sentence", "你所热爱的就是你的生活", "哔哩哔哩"), ("quota", "以下为 Codex 订阅额度：\n" + quotaExample, "额度排版示例"), ("long", string.Join("\n", Enumerable.Repeat("长消息可以滚动查看，不再截断文字。", 14)), "长消息排版示例") };
            var directory = Path.Combine(AppContext.BaseDirectory, "data", "bubble-previews"); Directory.CreateDirectory(directory);
            foreach (double fontSize in new[] { 12, 16.5, 28 })
            foreach (var (name, text, caption) in examples)
            {
                var bubble = new ThoughtBubble();
                bubble.SetTypography(BubbleFonts.DefaultId, fontSize);
                bubble.SetMessage(text, caption, details: name == "quota"); bubble.LimitHeight(390);
                var contentSize = bubble.MeasureMessage();
                var host = new System.Windows.Controls.Border { Child = bubble, Padding = new Thickness(16), Background = Ui.Brush("#EEEAF4") };
                host.Measure(new Size(contentSize.Width + 32, double.PositiveInfinity)); host.Arrange(new Rect(new Point(), host.DesiredSize)); host.UpdateLayout();
                double x = bubble.Child.RenderSize.Width / (bubble.ActualWidth - 4), y = bubble.Child.RenderSize.Height / (bubble.ActualHeight - ThoughtBubble.TailHeight - 4);
                Assert(x * x + y * y < 1 && bubble.ActualHeight <= 390, name + " text extends outside ellipse");
                var preview = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(host.ActualWidth), (int)Math.Ceiling(host.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32); preview.Render(host);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(preview));
                string suffix = fontSize == 16.5 ? "" : "-" + fontSize;
                using var output = File.Create(Path.Combine(directory, name + suffix + ".png")); encoder.Save(output);
            }
        });
        Test("Windows icon contains seven sizes and is readable", () => {
            var bytes = File.ReadAllBytes(DesktopIntegration.IconPath); Assert(BitConverter.ToUInt16(bytes, 2) == 1 && BitConverter.ToUInt16(bytes, 4) == 7); using var icon = new System.Drawing.Icon(DesktopIntegration.IconPath, 32, 32); Assert(icon.Width == 32 && icon.Height == 32);
        });
        Test("Shortcut and isolated startup on-off round-trip", () => {
            var directory = Path.Combine(AppContext.BaseDirectory, "data", "shortcut-test-" + Guid.NewGuid().ToString("N")); var path = Path.Combine(directory, DesktopIntegration.ShortcutName);
            DesktopIntegration.CreateShortcut(path); var link = DesktopIntegration.ReadShortcut(path)!.Value; Assert(link.Target.Equals(DesktopIntegration.Executable, StringComparison.OrdinalIgnoreCase) && link.Arguments == "" && link.Icon.Contains("gpt-dragon.ico"));
            Assert(!DesktopIntegration.StartupEnabled(path)); DesktopIntegration.SetStartup(true, path); Assert(DesktopIntegration.StartupEnabled(path)); DesktopIntegration.SetStartup(false, path); Assert(!File.Exists(path)); DesktopIntegration.SetStartup(false, path);
        });
        results.Add($"{results.Count - failures}/{results.Count} passed");
        var report = Path.Combine(AppContext.BaseDirectory, "test-results.txt"); File.WriteAllLines(report, results);
        return failures == 0 ? 0 : 1;
    }
}
