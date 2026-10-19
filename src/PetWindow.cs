using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace DragonPet;

public sealed class PetWindow : Window
{
    public PetSettings Settings { get; }
    public List<AppRule> Rules { get; private set; }
    public ConfigStore Store { get; }
    public QuotaService Quota { get; }
    public FocusClock Pomodoro { get; }
    readonly CompanionState companion;
    readonly Button label;
    CompanionMessage? focusNotice;
    DateTimeOffset nextQuotaPoll, evaluatedQuota;
    int evaluatedThreshold = -1;
    public string CurrentAppName { get; private set; } = "等待应用切换";
    public string CurrentExpression { get; private set; } = "default";
    public event Action? PetStateChanged;
    readonly PetImage sprite = new();
    readonly ThoughtBubble bubble = new();
    readonly Grid scene = new();
    readonly ScaleTransform scale = new(1, 1);
    readonly TranslateTransform bob = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    readonly ReactionGate gate = new();
    readonly Random random = new();
    readonly Dictionary<string, int> lineIndices = new();
    readonly Forms.NotifyIcon tray;
    readonly DuckSound sound = new();
    System.Drawing.Icon? trayIcon;
    MenuItem? topmostMenu;
    Forms.ToolStripMenuItem? topmostTray;
    SettingsWindow? settingsWindow;
    DateTimeOffset expressionUntil, bubbleUntil, lastClick, lastHover;
    DateTimeOffset suppressAppsUntil;
    Point mouseStart, windowStart;
    bool pressed, dragging, doubleClick, mirrored, hidden;
    int clickStreak, revision;
    string? observedId;
    string baseExpression = "default";

    public PetWindow(PetSettings settings, List<AppRule> rules, ConfigStore store, bool inspectWindow = false)
    {
        Settings = settings; Rules = rules; Store = store; Quota = new(settings);
        companion = store.Load("companion.json", () => new CompanionState());
        companion.Focus ??= new(); companion.Quota ??= new(); companion.Moments ??= new();
        Pomodoro = new(companion.Focus);
        if (Pomodoro.Running) Pomodoro.Pause(DateTimeOffset.UtcNow);
        Title = "GPT 小龙桌宠"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = inspectWindow;
        ShowActivated = false; Topmost = settings.AlwaysOnTop; FontFamily = new("Microsoft YaHei UI");
        if (File.Exists(DesktopIntegration.IconPath)) Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(DesktopIntegration.IconPath));
        Content = scene;
        bubble.VerticalAlignment = VerticalAlignment.Bottom; bubble.HorizontalAlignment = HorizontalAlignment.Center;
        bubble.Margin = new(5, 0, 5, Settings.Size + 38); bubble.Visibility = Visibility.Collapsed; bubble.Cursor = Cursors.Hand;
        bubble.ToolTip = "点击关闭；长消息可在文字区域滚动查看";
        bubble.MouseLeftButtonUp += (_, e) => { bubble.Visibility = Visibility.Collapsed; FitBubble(); e.Handled = true; };
        scene.Children.Add(bubble);
        sprite.Source = Sprites.Get("default"); sprite.Stretch = Stretch.Fill;
        sprite.VerticalAlignment = VerticalAlignment.Bottom; sprite.HorizontalAlignment = HorizontalAlignment.Center; sprite.Margin = new(0, 0, 0, 27);
        sprite.Cursor = Cursors.Hand; sprite.RenderTransformOrigin = new(.5, 1);
        var transforms = new TransformGroup(); transforms.Children.Add(scale); transforms.Children.Add(bob); sprite.RenderTransform = transforms;
        scene.Children.Add(sprite);
        label = new Button { Content = "✦  小龙 · 设置", FontFamily = Ui.PetFont, FontSize = 12, FontWeight = FontWeights.Normal, Foreground = Ui.Brush("#75648E"), Background = Ui.Brush("#EDFAF7FF"), BorderThickness = new(0), Padding = new(11, 3, 11, 3), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Cursor = Cursors.Hand };
        label.Click += (_, _) => OpenSettings(); scene.Children.Add(label);
        sprite.MouseLeftButtonDown += OnDown; sprite.MouseMove += OnMove; sprite.MouseLeftButtonUp += OnUp;
        sprite.LostMouseCapture += (_, _) => { if (pressed) { pressed = false; dragging = false; Bounce(); } };
        sprite.MouseEnter += (_, _) =>
        {
            if (DateTimeOffset.UtcNow > expressionUntil && (DateTimeOffset.UtcNow - lastHover).TotalSeconds > 20)
            { lastHover = DateTimeOffset.UtcNow; SetExpression("peek", 2.4); }
        };
        sprite.MouseWheel += (_, e) => { Settings.Size = Math.Clamp(Settings.Size + Math.Sign(e.Delta) * 15, 150, 400); ApplySettings(); e.Handled = true; };
        ContextMenu = MakeMenu();
        if (File.Exists(DesktopIntegration.IconPath)) trayIcon = new System.Drawing.Icon(DesktopIntegration.IconPath, 32, 32);
        tray = new Forms.NotifyIcon { Icon = trayIcon ?? System.Drawing.SystemIcons.Information, Text = "GPT 小龙桌宠 · 右键打开菜单", Visible = true };
        var menu = new Forms.ContextMenuStrip();
        void TrayItem(string text, Action action) { menu.Items.Add(text, null, (_, _) => Dispatcher.Invoke(action)); }
        TrayItem("陪伴设置", OpenSettings); TrayItem("显示 / 隐藏小龙", ToggleHidden); TrayItem("把小龙带回屏幕", ResetPosition);
        topmostTray = new Forms.ToolStripMenuItem("始终置顶于其他窗口") { Checked = Settings.AlwaysOnTop, CheckOnClick = false };
        topmostTray.Click += (_, _) => Dispatcher.Invoke(() => SetAlwaysOnTop(!Settings.AlwaysOnTop)); menu.Items.Add(topmostTray);
        TrayItem("开始 / 暂停 / 继续专注", ToggleFocus); TrayItem("结束本轮专注", ResetFocus);
        TrayItem("刷新额度", () => _ = ShowQuota(true)); TrayItem("退出", ExitPet);
        tray.ContextMenuStrip = menu; tray.DoubleClick += (_, _) => Dispatcher.Invoke(() => { hidden = false; Show(); ResetPosition(); });
        SourceInitialized += (_, _) => { if (!inspectWindow) Native.MakeToolWindow(this); };
        Loaded += (_, _) =>
        {
            ApplySettings(false);
            var area = Native.WorkArea(this);
            Left = settings.Left ?? area.Right - Width - 24; Top = settings.Top ?? area.Bottom - Height - 20; Clamp();
            Speak(store.Warning.Length > 0 ? store.Warning : "我搬来你的桌面啦。\n点点我，或者带我看看你在忙什么。", "default", "初次见面", 12);
            settings.FirstRun = false; SaveSettings(); timer.Start();
        };
        timer.Tick += (_, _) => Tick();
        Closed += (_, _) => { timer.Stop(); Pomodoro.Pause(DateTimeOffset.UtcNow); SaveCompanion(); Quota.Dispose(); sound.Dispose(); tray.Dispose(); trayIcon?.Dispose(); };
    }
    ContextMenu MakeMenu()
    {
        var menu = new ContextMenu();
        void Item(string title, Action action) { var m = new MenuItem { Header = title }; m.Click += (_, _) => action(); menu.Items.Add(m); }
        Item("✦ 陪伴设置", OpenSettings); Item("查看 / 刷新额度", () => _ = ShowQuota(true));
        Item("开始 / 暂停 / 继续专注", ToggleFocus); Item("结束本轮专注", ResetFocus);
        topmostMenu = new MenuItem { Header = "始终置顶于其他窗口", IsCheckable = true, IsChecked = Settings.AlwaysOnTop };
        topmostMenu.Click += (_, _) => SetAlwaysOnTop(topmostMenu.IsChecked); menu.Items.Add(topmostMenu);
        var expressions = new MenuItem { Header = "换个表情" };
        foreach (var pair in Sprites.Names) { var item = new MenuItem { Header = pair.Value }; item.Click += (_, _) => PreviewExpression(pair.Key); expressions.Items.Add(item); }
        menu.Items.Add(expressions);
        Item("暂停 / 恢复应用互动", () => { Settings.WatchApps = !Settings.WatchApps; SaveSettings(); Speak(Settings.WatchApps ? "应用互动已恢复。" : "好，我安静陪着你。", "reading"); });
        Item("隐藏到托盘", ToggleHidden); Item("回到屏幕右下角", ResetPosition); menu.Items.Add(new Separator()); Item("退出小龙", ExitPet);
        return menu;
    }
    void Tick()
    {
        var now = DateTimeOffset.UtcNow;
        if (Settings.FocusEnabled && Pomodoro.Tick(now, Settings.BreakMinutes) is FocusPhase phase)
        {
            focusNotice = phase == FocusPhase.Break
                ? new($"这一轮完成啦，辛苦了！\n休息 {Settings.BreakMinutes} 分钟，喝口水、活动一下吧。", "smug", "专注完成 · 休息一下")
                : new("休息结束啦！\n准备好了，就在设置里开启下一轮。", "peek", "番茄钟 · 等你回来");
            SaveCompanion(); PetStateChanged?.Invoke();
        }
        string badge = Settings.FocusEnabled && Pomodoro.State.Phase is FocusPhase.Focus or FocusPhase.Break ? Pomodoro.Status(now) + " · 设置" : "✦  小龙 · 设置";
        if (!Equals(label.Content, badge)) label.Content = badge;
        if (Settings.QuotaEnabled && Settings.QuotaAlertsEnabled && now >= nextQuotaPoll && !quotaPolling)
        { nextQuotaPoll = now.AddMinutes(3); PollQuota(); }
        if (bubble.Visibility == Visibility.Visible && now > bubbleUntil) bubble.Visibility = Visibility.Collapsed;
        string resting = Pomodoro.Concentrating ? "reading" : TimeEggs.Outfit(Settings, DateTime.Now) ?? baseExpression;
        if (!pressed && now > expressionUntil && CurrentExpression != resting) SetExpression(resting, 0);
        if (hidden) return;
        var info = Native.Foreground();
        bool quiet = Settings.FullscreenQuiet && info?.Fullscreen == true;
        Opacity = quiet ? 0 : 1;
        if (quiet || pressed) return;
        if (Settings.Bubbles && bubble.Visibility != Visibility.Visible && now >= suppressAppsUntil)
        {
            if (focusNotice != null)
            {
                var message = focusNotice; focusNotice = null;
                Notify(message); return;
            }
            if (TryQuotaAlert(now)) return;
            if (!Pomodoro.Concentrating && TimeEggs.Next(Settings, companion.Moments, DateTime.Now) is { } moment)
            {
                companion.Moments[moment.Slot] = moment.Key; SaveCompanion(); Notify(moment.Message); return;
            }
        }
        if (Pomodoro.Concentrating || now < suppressAppsUntil) return;
        if (info != null)
        {
            if (!Settings.WatchApps) return;
            var rule = RuleMatcher.Match(Rules, info);
            if (observedId != rule?.Id)
            {
                observedId = rule?.Id; revision++;
                baseExpression = rule?.Expression ?? "default";
                CurrentAppName = rule?.Name ?? "当前应用暂无规则"; PetStateChanged?.Invoke();
            }
            if (gate.Observe(rule?.Id, now, Settings.CooldownSeconds) && rule != null) React(rule);
            if (rule?.ShowQuota == true && now > expressionUntil && !quotaPolling) PollQuota();
        }
        if (Native.IdleSeconds() > 240 && now > expressionUntil && resting == "default") SetExpression("reading", 20);
    }
    void Notify(CompanionMessage message)
    {
        suppressAppsUntil = DateTimeOffset.UtcNow.AddSeconds(20);
        Speak(message.Text, message.Expression, message.Caption, 18, message.Caption.StartsWith("Codex"));
    }
    bool TryQuotaAlert(DateTimeOffset now)
    {
        if (!Settings.QuotaEnabled || !Settings.QuotaAlertsEnabled) { evaluatedThreshold = -1; return false; }
        var snapshot = Quota.Current;
        if (snapshot.IsStale(now) || (evaluatedQuota == snapshot.FetchedAt && evaluatedThreshold == Settings.QuotaAlertPercent)) return false;
        var messages = QuotaAlerts.Evaluate(snapshot, Settings, companion.Quota, now);
        evaluatedQuota = snapshot.FetchedAt; evaluatedThreshold = Settings.QuotaAlertPercent; SaveCompanion();
        if (messages.Count == 0) return false;
        Notify(new(string.Join("\n\n", messages.Select(m => m.Text)), messages[0].Expression, messages[0].Caption)); return true;
    }
    void SaveCompanion() => Store.Save("companion.json", companion);
    public void ToggleFocus()
    {
        if (!Settings.FocusEnabled) return;
        var now = DateTimeOffset.UtcNow;
        if (Pomodoro.Running) Pomodoro.Pause(now);
        else if (Pomodoro.State.Phase is FocusPhase.Focus or FocusPhase.Break) Pomodoro.Resume(now);
        else Pomodoro.Start(now, Settings.FocusMinutes);
        focusNotice = null; SaveCompanion(); PetStateChanged?.Invoke();
        suppressAppsUntil = now.AddSeconds(12);
        Speak(Pomodoro.Running ? (Pomodoro.Concentrating ? $"一起专注 {Math.Ceiling(Pomodoro.Remaining(now).TotalMinutes)} 分钟吧。\n我会安静读书，陪你做完眼前这一点。" : "继续休息，伸个懒腰吧。") : "计时暂停啦，等你准备好再继续。", Pomodoro.Concentrating ? "reading" : "peek", "专注陪伴", 10);
    }
    public void ResetFocus()
    {
        Pomodoro.Reset(); focusNotice = null; SaveCompanion(); PetStateChanged?.Invoke();
    }
    bool quotaPolling;
    async void PollQuota()
    {
        quotaPolling = true;
        try { await Quota.Refresh(); } finally { quotaPolling = false; }
    }
    void OnDown(object sender, MouseButtonEventArgs e)
    {
        if (Settings.ClickSound) sound.Play(Settings.SoundVolume);
        pressed = true; dragging = false; doubleClick = e.ClickCount == 2;
        mouseStart = Native.Cursor(this); windowStart = new(Left, Top); sprite.CaptureMouse();
        if (Settings.Motion) scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(.93, TimeSpan.FromMilliseconds(90)));
        e.Handled = true;
    }
    void OnMove(object sender, MouseEventArgs e)
    {
        if (!pressed) return;
        var cursor = Native.Cursor(this); var delta = cursor - mouseStart;
        if (!dragging && delta.Length > 5) { dragging = true; SetExpression("surprised", 60); bubble.Visibility = Visibility.Collapsed; revision++; }
        if (dragging) { Left = windowStart.X + delta.X; Top = windowStart.Y + delta.Y; }
    }
    void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (!pressed) return;
        pressed = false; sprite.ReleaseMouseCapture(); Bounce();
        if (dragging)
        {
            dragging = false; Snap(); SaveSettings(); Speak("这里就是我的新据点啦。", "peek", "搬家成功", 4);
        }
        else
        {
            var now = DateTimeOffset.UtcNow; clickStreak = (now - lastClick).TotalSeconds < 1.4 ? clickStreak + 1 : 1; lastClick = now;
            if (clickStreak >= 4) Speak("哇！慢一点，小龙快变成连点器啦！", "surprised", "戳戳 × " + clickStreak);
            else if (doubleClick) Speak("收到双倍快乐！要一起开一局吗？", "gaming", "双击互动");
            else Speak(new[] { "哼哼，被你发现我在偷看啦。", "今日份摸摸，已签收。", "别光看着我，你也很可爱。", "我在呢。慢慢来，总能做好的。" }[random.Next(4)], "smug", "摸摸小龙");
        }
        e.Handled = true;
    }
    void Bounce()
    {
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null); scale.ScaleY = 1;
        if (Settings.Motion) scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.07, 1, TimeSpan.FromMilliseconds(350)) { EasingFunction = new ElasticEase { Oscillations = 2, Springiness = 6 } });
    }
    public void SetExpression(string expression, double seconds = 7)
    {
        if (!Sprites.Names.ContainsKey(expression)) expression = "default";
        CurrentExpression = expression; sprite.Source = Sprites.Get(expression); expressionUntil = DateTimeOffset.UtcNow.AddSeconds(seconds);
        PetStateChanged?.Invoke();
    }
    public void Speak(string text, string expression = "default", string caption = "小龙来信", int? seconds = null, bool details = false)
    {
        revision++; SetExpression(expression, seconds ?? Settings.BubbleSeconds);
        bubble.SetMessage(text, caption, details);
        bubble.Visibility = Settings.Bubbles ? Visibility.Visible : Visibility.Collapsed;
        FitBubble();
        bubbleUntil = DateTimeOffset.UtcNow.AddSeconds(seconds ?? Settings.BubbleSeconds); Bounce();
    }
    public void React(AppRule rule)
    {
        baseExpression = rule.Expression;
        var lines = AppRule.Split(rule.Lines); int index = lineIndices.TryGetValue(rule.Id, out var previous) ? (previous + 1) % Math.Max(1, lines.Length) : 0;
        lineIndices[rule.Id] = index;
        if (rule.ShowQuota) _ = ShowQuota(false, rule);
        else Speak(lines.Length > 0 ? lines[index] : "我在这里陪你。", rule.Expression, rule.Name);
    }
    public async System.Threading.Tasks.Task ShowQuota(bool force, AppRule? rule = null)
    {
        string caption = rule?.Name ?? "额度看板";
        Speak("正在查询官方额度……", "reading", caption, 25);
        int ticket = revision;
        var snapshot = await Quota.Refresh(force);
        PetStateChanged?.Invoke();
        if (ticket != revision) return;
        var prefix = rule?.Id == "chatgpt" ? "以下为 Codex 订阅额度：\n" : "";
        Speak(prefix + snapshot.Format(DateTimeOffset.UtcNow, 2, compact: true), snapshot.Windows.Any(w => w.Remaining <= 10) ? "surprised" : "reading", caption + " · 官方接口", 18, details: true);
    }
    public void PreviewExpression(string key) { suppressAppsUntil = DateTimeOffset.UtcNow.AddSeconds(12); Speak(Sprites.Names[key], key, "表情预览", 10); }
    public void PreviewRule(AppRule rule) { suppressAppsUntil = DateTimeOffset.UtcNow.AddSeconds(18); React(rule); }
    public void UpdateRules(List<AppRule> rules)
    {
        ConfigStore.ValidateRules(rules); Store.Save("rules.json", rules);
        Rules = System.Text.Json.JsonSerializer.Deserialize<List<AppRule>>(System.Text.Json.JsonSerializer.Serialize(rules))!;
    }
    public void ApplySettings(bool save = true)
    {
        Settings.Normalize();
        if (!Settings.FocusEnabled && Pomodoro.State.Phase != FocusPhase.Idle) ResetFocus();
        if (!Settings.QuotaAlertsEnabled) evaluatedThreshold = -1;
        bubble.SetTypography(Settings.BubbleFont, Settings.BubbleFontSize);
        bubble.Margin = new(5, 0, 5, Settings.Size + 38);
        sprite.Width = sprite.Height = Settings.Size;
        Native.SetAlwaysOnTop(this, Settings.AlwaysOnTop);
        if (topmostMenu != null) topmostMenu.IsChecked = Settings.AlwaysOnTop;
        if (topmostTray != null) topmostTray.Checked = Settings.AlwaysOnTop;
        if (!Settings.ClickSound || Settings.SoundVolume == 0) sound.Stop();
        else sound.Prepare(Settings.SoundVolume);
        bob.BeginAnimation(TranslateTransform.YProperty, null); bob.Y = 0;
        if (Settings.Motion) bob.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -4, TimeSpan.FromSeconds(2.3)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
        if (!Settings.Bubbles) bubble.Visibility = Visibility.Collapsed;
        if (!Settings.FullscreenQuiet) Opacity = 1;
        FitBubble();
        if (save) SaveSettings(); PetStateChanged?.Invoke();
    }
    void FitBubble()
    {
        double bottom = Top + Height, center = Left + Width / 2;
        double available = Native.WorkArea(this).Height - Settings.Size - 54;
        bubble.LimitHeight(available);
        double messageHeight = bubble.Visibility == Visibility.Visible ? bubble.MeasureMessage().Height : 0;
        Width = Math.Max(380, Settings.Size + 32);
        Height = Settings.Size + 46 + Math.Max(150, Math.Ceiling(messageHeight));
        if (IsLoaded)
        {
            // Keep the pet's feet and horizontal position steady as the bubble grows.
            if (double.IsFinite(bottom)) Top = bottom - Height;
            if (double.IsFinite(center)) Left = center - Width / 2;
            Clamp();
        }
    }
    public void SetAlwaysOnTop(bool enabled) { Settings.AlwaysOnTop = enabled; ApplySettings(); }
    public string? PreviewSound() { sound.Play(Settings.SoundVolume); return sound.LastError; }
    public void PreviewTypography()
    {
        suppressAppsUntil = DateTimeOffset.UtcNow.AddSeconds(15);
        Speak("今天也要元气满满呀！\nAa 0123456789", "smug", "字体预览", 12);
    }
    public void SaveSettings() { if (IsLoaded) { Settings.Left = Left; Settings.Top = Top; } Store.Save("settings.json", Settings); }
    void Clamp()
    {
        var p = Native.ClampPosition(new(Left, Top), new(Width, Height), Native.WorkArea(this)); Left = p.X; Top = p.Y;
    }
    void Snap()
    {
        Clamp(); var area = Native.WorkArea(this);
        if (Settings.SnapToEdge)
        {
            if (Left - area.Left < 36) Left = area.Left;
            if (area.Right - Left - Width < 36) Left = area.Right - Width;
            if (Top - area.Top < 28) Top = area.Top;
            if (area.Bottom - Top - Height < 36) Top = area.Bottom - Height;
        }
        mirrored = Settings.SnapToEdge && Math.Abs(Left - area.Left) < 1;
        scale.ScaleX = mirrored ? -1 : 1;
    }
    public void ResetPosition() { hidden = false; Show(); Opacity = 1; var a = Native.WorkArea(this); Left = a.Right - Width - 20; Top = a.Bottom - Height - 16; mirrored = false; scale.ScaleX = 1; SaveSettings(); }
    void ToggleHidden() { hidden = !hidden; if (hidden) Hide(); else { Show(); Opacity = 1; } }
    public void OpenSettings()
    {
        if (settingsWindow != null) { settingsWindow.Activate(); return; }
        settingsWindow = new SettingsWindow(this); settingsWindow.Closed += (_, _) => settingsWindow = null; settingsWindow.Show(); settingsWindow.Activate();
    }
    void ExitPet() { SaveSettings(); settingsWindow?.Close(); Close(); Application.Current.Shutdown(); }
}
