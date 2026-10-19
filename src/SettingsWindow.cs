using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace DragonPet;

public static class Ui
{
    public static FontFamily PetFont { get; } = File.Exists(Path.Combine(AppContext.BaseDirectory, "assets", "fonts", "ZCOOLKuaiLe-Regular.ttf"))
        ? new FontFamily(new Uri(AppContext.BaseDirectory, UriKind.Absolute), "./assets/fonts/#ZCOOL KuaiLe")
        : new FontFamily("YouYuan, Microsoft YaHei UI");
    public static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
    public static TextBlock Text(string text, double size = 14, string color = "#3F3653", bool bold = false) => new() { Text = text, FontSize = size, Foreground = Brush(color), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 9), LineHeight = size * 1.5 };
    public static StackPanel Stack() => new() { Margin = new(0, 0, 0, 10) };
    public static Border Card(UIElement child, string background = "#FFFFFF") => new() { Child = child, Background = Brush(background), CornerRadius = new(16), Padding = new(20), BorderBrush = Brush("#EAE4F2"), BorderThickness = new(1), Margin = new(0, 0, 0, 15) };
    public static Button Button(string text, Action click, bool primary = false)
    {
        var b = new Button { Content = text, Padding = new(15, 9, 15, 9), Margin = new(0, 3, 8, 3), Background = Brush(primary ? "#7960AA" : "#F0EBF8"), Foreground = Brush(primary ? "#FFFFFF" : "#635079"), BorderThickness = new(0), Cursor = System.Windows.Input.Cursors.Hand, FontSize = 13 };
        b.Click += (_, _) => click(); return b;
    }
    public static CheckBox Check(string text, bool value, Action<bool> change)
    {
        var c = new CheckBox { Content = text, IsChecked = value, Foreground = Brush("#4A405A"), Margin = new(0, 8, 0, 8), FontSize = 13 };
        c.Checked += (_, _) => change(true); c.Unchecked += (_, _) => change(false); return c;
    }
    public static TextBox Input(string value, bool multiline = false) => new() { Text = value, Padding = new(10, 8, 10, 8), Margin = new(0, 0, 0, 12), BorderBrush = Brush("#DFD6E9"), BorderThickness = new(1), Background = Brush("#FFFEFF"), FontSize = 13, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, MinHeight = multiline ? 65 : 35, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
}

public sealed class SettingsWindow : Window
{
    readonly PetWindow pet;
    readonly ContentControl content = new();
    readonly ScrollViewer pageScroll = new();
    readonly TextBlock status = Ui.Text("", 11, "#8B7C9E");
    readonly Dictionary<string, Button> nav = new();
    readonly DispatcherTimer statusTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    string currentPage = "home";
    TextBlock? quotaText;
    StackPanel? quotaBars;
    bool updating;
    CheckBox? topmostToggle;
    bool syncingTopmost;
    TextBlock? focusText;
    Button? focusAction, focusReset;

    public SettingsWindow(PetWindow pet)
    {
        this.pet = pet; Title = "GPT 小龙 · 陪伴设置"; Width = 940; Height = 760; MinWidth = 820; MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; Background = Ui.Brush("#F8F6FB"); FontFamily = new("Microsoft YaHei UI");
        if (File.Exists(DesktopIntegration.IconPath)) Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(DesktopIntegration.IconPath));
        Resources.Add(typeof(Button), (Style)XamlReader.Parse("<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><Border x:Name='b' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' Background='{TemplateBinding Background}' CornerRadius='9' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Opacity' Value='0.8'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter TargetName='b' Property='Opacity' Value='0.45'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>"));
        var root = new Grid(); root.ColumnDefinitions.Add(new() { Width = new(210) }); root.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); Content = root;
        var side = new DockPanel { Background = Ui.Brush("#EEE8F6"), LastChildFill = true };
        var brand = Ui.Stack(); brand.Margin = new(24, 30, 20, 22); brand.Children.Add(Ui.Text("✦  GPT 小龙", 24, "#594570", true)); brand.Children.Add(Ui.Text("桌面上的一点陪伴", 12, "#927FA5")); DockPanel.SetDock(brand, Dock.Top); side.Children.Add(brand);
        var foot = Ui.Stack(); foot.Margin = new(24, 12, 20, 22); foot.Children.Add(Ui.Text("LOCAL COMPANION", 10, "#9B8CA9", true)); foot.Children.Add(status); foot.Children.Add(Ui.Text("v1.2  ·  窗口识别仅在本机运行", 10, "#9B8CA9")); DockPanel.SetDock(foot, Dock.Bottom); side.Children.Add(foot);
        var navigation = Ui.Stack(); navigation.Margin = new(16, 0, 12, 0);
        foreach (var (id, title) in new[] { ("home", "✧   陪伴与互动"), ("typography", "◌   气泡与字体"), ("focus", "◴   专注陪伴"), ("quota", "◷   额度与提醒"), ("moments", "☼   时间彩蛋"), ("apps", "▤   应用与台词"), ("faces", "♡   表情收藏") })
        {
            var button = Ui.Button(title, () => Navigate(id)); button.HorizontalContentAlignment = HorizontalAlignment.Left; button.Padding = new(15, 14, 15, 14); button.Margin = new(0, 3, 0, 5); nav[id] = button; navigation.Children.Add(button);
        }
        side.Children.Add(new ScrollViewer { Content = navigation, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }); root.Children.Add(side);
        pageScroll.Content = content; pageScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; pageScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled; pageScroll.Padding = new(30, 28, 28, 20);
        Grid.SetColumn(pageScroll, 1); root.Children.Add(pageScroll);
        Navigate("home");
        statusTimer.Tick += (_, _) => { status.Text = pet.CurrentAppName + "\n" + Sprites.Names[pet.CurrentExpression]; if (currentPage == "quota") RefreshQuotaView(); if (currentPage == "focus") RefreshFocusView(); };
        statusTimer.Start(); Closed += (_, _) => statusTimer.Stop();
        void Status()
        {
            status.Text = pet.CurrentAppName + "\n" + Sprites.Names[pet.CurrentExpression];
            if (currentPage == "focus") RefreshFocusView();
            if (topmostToggle != null) { syncingTopmost = true; topmostToggle.IsChecked = pet.Settings.AlwaysOnTop; syncingTopmost = false; }
        }
        pet.PetStateChanged += Status; Closed += (_, _) => pet.PetStateChanged -= Status;
    }
    StackPanel Page(string title, string description)
    {
        var p = Ui.Stack(); p.Children.Add(Ui.Text(title, 26, "#3E3153", true)); p.Children.Add(Ui.Text(description, 13, "#9587A2")); return p;
    }
    void Navigate(string id)
    {
        currentPage = id;
        focusText = null; focusAction = null; focusReset = null;
        foreach (var pair in nav) { pair.Value.Background = Ui.Brush(pair.Key == id ? "#FFFFFF" : "#EEE8F6"); pair.Value.FontWeight = pair.Key == id ? FontWeights.SemiBold : FontWeights.Normal; }
        content.Content = id switch { "typography" => TypographyPage(), "focus" => FocusPage(), "moments" => MomentsPage(), "apps" => AppsPage(), "quota" => QuotaPage(), "faces" => FacesPage(), _ => HomePage() };
        pageScroll.ScrollToTop();
    }
    void Save() { pet.ApplySettings(); }
    StackPanel HomePage()
    {
        var page = Page("今天，也陪在你身边。", "轻轻一点就有回应，忙起来的时候就安静陪伴。");
        var startup = Ui.Stack(); startup.Children.Add(Ui.Text("启动与窗口", 16, bold: true));
        var startupStatus = Ui.Text("开启后，下次登录 Windows 时小龙会自动出现。", 11, "#9587A2");
        bool startupEnabled = false, changingStartup = false;
        try { startupEnabled = DesktopIntegration.StartupEnabled(); } catch (Exception) { startupStatus.Text = "暂时无法读取自启动状态，可以重新尝试切换。"; }
        CheckBox? startupToggle = null;
        startupToggle = Ui.Check("开机自启动（登录 Windows 后）", startupEnabled, enabled =>
        {
            if (changingStartup) return;
            try { DesktopIntegration.SetStartup(enabled); startupStatus.Text = enabled ? "已开启，下次登录 Windows 自动启动。" : "已关闭开机自启动。"; }
            catch (Exception ex) { changingStartup = true; startupToggle!.IsChecked = !enabled; changingStartup = false; startupStatus.Text = "设置未生效：" + ex.Message; }
        });
        startup.Children.Add(startupToggle); startup.Children.Add(startupStatus);
        topmostToggle = Ui.Check("始终置顶于其他窗口", pet.Settings.AlwaysOnTop, v => { if (!syncingTopmost) pet.SetAlwaysOnTop(v); }); startup.Children.Add(topmostToggle);
        startup.Children.Add(Ui.Check("全屏时自动隐身，退出全屏后恢复", pet.Settings.FullscreenQuiet, v => { pet.Settings.FullscreenQuiet = v; Save(); }));
        startup.Children.Add(Ui.Text("想在全屏应用前也显示小龙，可关闭「全屏时自动隐身」。右键和托盘菜单也能切换置顶。", 11, "#9587A2"));
        var shortcutStatus = Ui.Text("", 11, "#8B7A9C");
        startup.Children.Add(Ui.Button("创建 / 更新桌面快捷方式", () => { try { DesktopIntegration.CreateShortcut(DesktopIntegration.DesktopLink); shortcutStatus.Text = "桌面快捷方式已就绪，图标是你的小龙。"; } catch (Exception ex) { shortcutStatus.Text = "创建失败：" + ex.Message; } }));
        startup.Children.Add(shortcutStatus); page.Children.Add(Ui.Card(startup));
        var hero = new Grid(); hero.ColumnDefinitions.Add(new() { Width = new(150) }); hero.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var avatar = new Image { Source = Sprites.Get("default"), Width = 140, Height = 140 }; hero.Children.Add(avatar);
        var greeting = Ui.Stack(); greeting.VerticalAlignment = VerticalAlignment.Center; greeting.Margin = new(18, 10, 0, 0); greeting.Children.Add(Ui.Text("白发小龙，正式入住", 19, "#625078", true)); greeting.Children.Add(Ui.Text("点击摸摸 · 双击开玩 · 拖动搬家\n滚轮缩放 · 右键打开快捷菜单", 13, "#8C7C9E")); greeting.Children.Add(Ui.Button("把小龙带回屏幕", pet.ResetPosition)); Grid.SetColumn(greeting, 1); hero.Children.Add(greeting); page.Children.Add(Ui.Card(hero, "#F1ECF8"));
        var audio = Ui.Stack(); audio.Children.Add(Ui.Text("小黄鸭点击音效", 16, bold: true));
        audio.Children.Add(Ui.Check("点击时发出小黄鸭「吱」声", pet.Settings.ClickSound, v => { pet.Settings.ClickSound = v; Save(); }));
        var volumeLabel = Ui.Text($"音量  {pet.Settings.SoundVolume}%", 12, "#8B7A9C"); audio.Children.Add(volumeLabel);
        var volume = new Slider { Minimum = 0, Maximum = 100, Value = pet.Settings.SoundVolume, TickFrequency = 5, IsSnapToTickEnabled = true, Margin = new(0, 0, 0, 10) };
        volume.ValueChanged += (_, _) => { pet.Settings.SoundVolume = (int)volume.Value; volumeLabel.Text = $"音量  {pet.Settings.SoundVolume}%"; Save(); }; audio.Children.Add(volume);
        var soundStatus = Ui.Text("按下桌宠时立即发声，拖动中不重复；音量设为 0 即为静音。", 11, "#9587A2");
        audio.Children.Add(Ui.Button("试听小黄鸭", () => { soundStatus.Text = pet.PreviewSound() ?? (pet.Settings.SoundVolume == 0 ? "音量为 0，当前静音。" : "已试听。点击音效开关不会影响手动试听。 "); })); audio.Children.Add(soundStatus); page.Children.Add(Ui.Card(audio));
        var appearance = Ui.Stack(); appearance.Children.Add(Ui.Text("你的陪伴方式", 16, bold: true));
        void Slider(string title, int value, int min, int max, Action<int> set)
        {
            var label = Ui.Text($"{title}  {value}", 12, "#8B7A9C"); appearance.Children.Add(label);
            var slider = new Slider { Minimum = min, Maximum = max, Value = value, TickFrequency = title.StartsWith("桌宠") ? 10 : 1, IsSnapToTickEnabled = true, Margin = new(0, 0, 0, 14) };
            slider.ValueChanged += (_, _) => { label.Text = $"{title}  {(int)slider.Value}"; set((int)slider.Value); Save(); }; appearance.Children.Add(slider);
        }
        Slider("桌宠大小（像素）", pet.Settings.Size, 150, 400, v => pet.Settings.Size = v);
        Slider("气泡停留（秒）", pet.Settings.BubbleSeconds, 3, 30, v => pet.Settings.BubbleSeconds = v);
        appearance.Children.Add(Ui.Check("根据前台应用弹出台词", pet.Settings.WatchApps, v => { pet.Settings.WatchApps = v; Save(); }));
        appearance.Children.Add(Ui.Check("显示对话气泡", pet.Settings.Bubbles, v => { pet.Settings.Bubbles = v; Save(); }));
        appearance.Children.Add(Ui.Check("靠近屏幕边缘时吸附", pet.Settings.SnapToEdge, v => { pet.Settings.SnapToEdge = v; Save(); }));
        appearance.Children.Add(Ui.Check("呼吸与按压动画", pet.Settings.Motion, v => { pet.Settings.Motion = v; Save(); }));
        page.Children.Add(Ui.Card(appearance));
        page.Children.Add(Ui.Text("设置自动保存。仅匹配当前窗口的进程名与标题，不读取聊天内容，不保存窗口历史。网页识别依赖浏览器标签页标题。", 12, "#9587A2"));
        return page;
    }
    StackPanel TypographyPage()
    {
        var page = Page("让小龙用你喜欢的字。", "字体和字号立即生效、自动保存。标题与说明文字随正文按比例缩放。");
        var controls = Ui.Stack(); controls.Children.Add(Ui.Text("气泡字体", 14, bold: true));
        var fonts = BubbleFonts.Available;
        var picker = new ComboBox
        {
            ItemsSource = fonts, DisplayMemberPath = "Label", SelectedValuePath = "Id", IsTextSearchEnabled = true,
            SelectedValue = fonts.Any(f => f.Id == pet.Settings.BubbleFont) ? pet.Settings.BubbleFont : BubbleFonts.DefaultId,
            Padding = new(10), FontSize = 14, MaxDropDownHeight = 300, Margin = new(0, 0, 0, 10)
        };
        System.Windows.Automation.AutomationProperties.SetName(picker, "气泡字体");
        controls.Children.Add(picker);
        controls.Children.Add(Ui.Text("可选内置字体和本机已安装字体。列表获得焦点后，可键入字体名称的开头快速定位。", 11, "#9587A2"));
        var sizeLabel = Ui.Text("", 14, bold: true); controls.Children.Add(sizeLabel);
        var size = new Slider
        {
            Minimum = 12, Maximum = 28, Value = pet.Settings.BubbleFontSize, TickFrequency = .5,
            SmallChange = .5, LargeChange = 2, IsSnapToTickEnabled = true, Margin = new(0, 8, 0, 10)
        };
        System.Windows.Automation.AutomationProperties.SetName(size, "气泡字号"); controls.Children.Add(size);
        controls.Children.Add(Ui.Text("12–28，支持每次调整 0.5。气泡会重新排版，过长的消息可以滚动查看。", 11, "#9587A2"));
        page.Children.Add(Ui.Card(controls));

        var preview = new ThoughtBubble { HorizontalAlignment = HorizontalAlignment.Center, Margin = new(0, 8, 0, 0) };
        var samples = new ComboBox { ItemsSource = new[] { "日常台词", "额度信息" }, SelectedIndex = 0, Padding = new(8), Margin = new(0, 0, 0, 8) };
        System.Windows.Automation.AutomationProperties.SetName(samples, "字体预览内容");
        var previewCard = Ui.Stack(); previewCard.Children.Add(Ui.Text("实时预览", 16, bold: true)); previewCard.Children.Add(samples); previewCard.Children.Add(preview);
        page.Children.Add(Ui.Card(previewCard, "#F4F0F9"));
        bool syncing = false;
        void RefreshPreview()
        {
            sizeLabel.Text = $"正文字号  {pet.Settings.BubbleFontSize:0.#}";
            preview.SetTypography(pet.Settings.BubbleFont, pet.Settings.BubbleFontSize);
            bool details = samples.SelectedIndex == 1;
            preview.SetMessage(details ? "Codex · 5小时  剩余 75%\n2小时后重置\n更新于 12:34" : "今天也要元气满满呀！\nAa 0123456789", details ? "额度排版示例" : "小龙的悄悄话", details);
            preview.LimitHeight(370);
        }
        picker.SelectionChanged += (_, _) =>
        {
            if (syncing || picker.SelectedItem is not BubbleFontOption selected) return;
            pet.Settings.BubbleFont = selected.Id; Save(); RefreshPreview();
        };
        size.ValueChanged += (_, _) => { if (syncing) return; pet.Settings.BubbleFontSize = size.Value; Save(); RefreshPreview(); };
        samples.SelectionChanged += (_, _) => RefreshPreview();
        var actions = new WrapPanel(); actions.Children.Add(Ui.Button("在桌宠上预览", pet.PreviewTypography, true));
        actions.Children.Add(Ui.Button("恢复默认字体与字号", () =>
        {
            syncing = true;
            pet.Settings.BubbleFont = BubbleFonts.DefaultId; pet.Settings.BubbleFontSize = BubbleFonts.DefaultSize;
            picker.SelectedValue = BubbleFonts.DefaultId; size.Value = BubbleFonts.DefaultSize;
            syncing = false; Save(); RefreshPreview();
        }));
        page.Children.Add(actions); RefreshPreview(); return page;
    }
    StackPanel AppsPage()
    {
        var page = Page("懂一点你的日常。", "独立应用优先匹配进程；浏览器与终端优先匹配当前标题。");
        var working = JsonSerializer.Deserialize<List<AppRule>>(JsonSerializer.Serialize(pet.Rules))!;
        var picker = new ComboBox { ItemsSource = working, DisplayMemberPath = "Name", Margin = new(0, 0, 0, 15), Padding = new(10), FontSize = 14 };
        page.Children.Add(picker); var editor = Ui.Stack(); page.Children.Add(Ui.Card(editor));
        var message = Ui.Text("", 12, "#7B609E"); page.Children.Add(message);
        void Edit(AppRule rule)
        {
            editor.Children.Clear();
            editor.Children.Add(Ui.Text("应用规则", 16, bold: true));
            void Field(string label, string text, Action<string> set, bool multi = false)
            { editor.Children.Add(Ui.Text(label, 12, "#9587A2")); var box = Ui.Input(text, multi); box.TextChanged += (_, _) => set(box.Text); editor.Children.Add(box); }
            Field("应用名称", rule.Name, v => rule.Name = v);
            Field("进程名（精确匹配，用 | 分隔，不含路径）", rule.Processes, v => rule.Processes = v);
            Field("标题关键词（包含匹配，用 | 分隔）", rule.Titles, v => rule.Titles = v);
            Field("台词（多句用 | 分隔，按顺序轮换）", rule.Lines, v => rule.Lines = v, true);
            var faces = new ComboBox { ItemsSource = Sprites.Names, DisplayMemberPath = "Value", SelectedValuePath = "Key", SelectedValue = rule.Expression, Padding = new(9), Margin = new(0, 2, 0, 8) };
            faces.SelectionChanged += (_, _) => rule.Expression = faces.SelectedValue as string ?? "default"; editor.Children.Add(faces);
            editor.Children.Add(Ui.Check("启用这条规则", rule.Enabled, v => rule.Enabled = v));
            editor.Children.Add(Ui.Check("显示额度看板", rule.ShowQuota, v => rule.ShowQuota = v));
            var actions = new WrapPanel();
            actions.Children.Add(Ui.Button("保存规则", () => { try { pet.UpdateRules(working); picker.Items.Refresh(); message.Text = "已保存，下一次切换到这个应用时生效。"; } catch (Exception ex) { message.Text = ex.Message; } }, true));
            actions.Children.Add(Ui.Button("预览台词与表情", () => pet.PreviewRule(rule))); editor.Children.Add(actions);
            editor.Children.Add(Ui.Text("编辑后请保存。预览不会打开对应应用。", 11, "#9C90A9"));
        }
        picker.SelectionChanged += (_, _) => { if (picker.SelectedItem is AppRule r) Edit(r); };
        picker.SelectedIndex = 0;
        var more = new WrapPanel();
        more.Children.Add(Ui.Button("＋ 添加应用", () => { var r = new AppRule { Id = "custom-" + Guid.NewGuid().ToString("N")[..8], Name = "我的应用", Lines = "我来陪你啦。", Expression = "peek" }; working.Add(r); picker.Items.Refresh(); picker.SelectedItem = r; }));
        more.Children.Add(Ui.Button("移除所选规则", () => { if (picker.SelectedItem is AppRule r) { working.Remove(r); picker.Items.Refresh(); picker.SelectedIndex = working.Count > 0 ? 0 : -1; editor.Children.Clear(); if (picker.SelectedItem is AppRule next) Edit(next); message.Text = "已从编辑列表移除，点击下方按钮保存。"; } }));
        more.Children.Add(Ui.Button("保存全部规则", () => { try { pet.UpdateRules(working); message.Text = "全部规则已保存。"; } catch (Exception ex) { message.Text = ex.Message; } })); page.Children.Add(more);
        var tester = Ui.Stack(); tester.Children.Add(Ui.Text("试试能否识别", 16, bold: true));
        var process = Ui.Input("msedge"); var title = Ui.Input("哔哩哔哩 - Microsoft Edge"); tester.Children.Add(process); tester.Children.Add(title);
        var result = Ui.Text("填写进程名和标题，可以测试当前编辑中的规则。", 12, "#9587A2");
        tester.Children.Add(Ui.Button("测试匹配", () => { var r = RuleMatcher.Match(working, new(process.Text, title.Text)); result.Text = r == null ? "暂无命中，可添加关键词。" : "命中：" + r.Name + " → " + Sprites.Names.GetValueOrDefault(r.Expression, "日常"); })); tester.Children.Add(result); page.Children.Add(Ui.Card(tester));
        var cooldown = Ui.Stack(); cooldown.Children.Add(Ui.Text("同一应用提示冷却（秒）", 12, "#9587A2")); var cd = Ui.Input(pet.Settings.CooldownSeconds.ToString()); cooldown.Children.Add(cd); cooldown.Children.Add(Ui.Button("保存冷却时间", () => { if (int.TryParse(cd.Text, out int seconds)) { pet.Settings.CooldownSeconds = Math.Clamp(seconds, 15, 1800); Save(); cd.Text = pet.Settings.CooldownSeconds.ToString(); } })); page.Children.Add(Ui.Card(cooldown));
        return page;
    }
    StackPanel QuotaPage()
    {
        var page = Page("每一点额度，都心里有数。", "只显示真实查询结果，并标明额度所属产品与更新时间。");
        var data = Ui.Stack(); data.Children.Add(Ui.Text("Codex 订阅额度", 18, bold: true));
        quotaText = Ui.Text("尚未查询", 13, "#86749C"); quotaBars = Ui.Stack(); data.Children.Add(quotaBars); data.Children.Add(quotaText);
        var refresh = Ui.Button("刷新官方额度", async () => { if (updating) return; updating = true; quotaText.Text = "正在连接本机 Codex，请稍等……"; try { await pet.Quota.Refresh(true); RefreshQuotaView(); } finally { updating = false; } }, true); data.Children.Add(refresh);
        data.Children.Add(Ui.Check("启用 Codex 额度查询", pet.Settings.QuotaEnabled, v => { pet.Settings.QuotaEnabled = v; Save(); }));
        data.Children.Add(Ui.Text("使用本机 Codex 的已有登录。打开 ChatGPT 或 Codex 时显示这里的订阅额度；不发送对话、不消耗重置机会。", 12, "#9587A2")); page.Children.Add(Ui.Card(data));
        var alerts = Ui.Stack(); alerts.Children.Add(Ui.Text("额度提醒", 17, bold: true));
        alerts.Children.Add(Ui.Check("开启低额度提醒（短周期 / 周额度分别判断）", pet.Settings.QuotaAlertsEnabled, v => { pet.Settings.QuotaAlertsEnabled = v; Save(); }));
        alerts.Children.Add(Ui.Text("剩余额度 ≤ 以下百分比时提醒（1–99%）", 13));
        var threshold = Ui.Input(pet.Settings.QuotaAlertPercent.ToString()); threshold.Width = 110; threshold.HorizontalAlignment = HorizontalAlignment.Left; alerts.Children.Add(threshold);
        var feedback = Ui.Text("当前提醒线：" + pet.Settings.QuotaAlertPercent + "%", 12, "#9587A2");
        alerts.Children.Add(Ui.Button("保存提醒阈值", () => {
            if (!int.TryParse(threshold.Text, out int value) || value < 1 || value > 99) { feedback.Text = "请输入 1–99 之间的整数。"; return; }
            pet.Settings.QuotaAlertPercent = value; Save(); feedback.Text = "已保存：剩余额度 ≤ " + value + "% 时提醒。";
        })); alerts.Children.Add(feedback);
        alerts.Children.Add(Ui.Check("额度回升到提醒线以上时，也告诉我", pet.Settings.QuotaRecoveryEnabled, v => { pet.Settings.QuotaRecoveryEnabled = v; Save(); }));
        alerts.Children.Add(Ui.Text("开启后每 3 分钟后台查询，需要上方的额度查询开关。只有官方接口返回的新结果才触发；同一次低额度状态不会反复提醒，重启也记得。关闭气泡、隐藏或全屏静默时不打扰。", 12, "#9587A2")); page.Children.Add(Ui.Card(alerts));
        var setup = Ui.Stack(); setup.Children.Add(Ui.Text("找不到 Codex？", 16, bold: true)); setup.Children.Add(Ui.Text("默认自动查找。如果你使用独立安装，可在这里选择 codex.exe。", 12, "#9587A2")); var path = Ui.Input(pet.Settings.CodexPath); setup.Children.Add(path);
        var actions = new WrapPanel(); actions.Children.Add(Ui.Button("选择文件", () => { var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Codex 程序|codex.exe|程序|*.exe" }; if (dialog.ShowDialog(this) == true) path.Text = dialog.FileName; })); actions.Children.Add(Ui.Button("保存路径", () => { pet.Settings.CodexPath = path.Text.Trim(); pet.SaveSettings(); })); setup.Children.Add(actions); page.Children.Add(Ui.Card(setup));
        RefreshQuotaView(); return page;
    }
    void RefreshQuotaView()
    {
        if (quotaText == null || quotaBars == null) return;
        var snapshot = pet.Quota.Current;
        quotaText.Text = snapshot.Format(DateTimeOffset.UtcNow, 20);
        if (snapshot.Error.Length > 0 && snapshot.Windows.Count > 0) quotaText.Text += "\n" + snapshot.Error;
        quotaBars.Children.Clear();
        foreach (var w in snapshot.Windows)
        {
            bool expired = w.ResetsAt.HasValue && w.ResetsAt <= DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (expired) continue;
            quotaBars.Children.Add(Ui.Text($"{w.Bucket} · {w.Label}    {w.Remaining:0.#}% 可用", 13));
            quotaBars.Children.Add(new ProgressBar { Value = w.Remaining, Maximum = 100, Height = 7, Foreground = Ui.Brush(w.Remaining < 10 ? "#D78E9D" : "#A28BC3"), Background = Ui.Brush("#EDE6F4"), BorderThickness = new(0), Margin = new(0, 0, 0, 15), Opacity = snapshot.IsStale(DateTimeOffset.UtcNow) ? .45 : 1 });
        }
    }
    StackPanel FacesPage()
    {
        var page = Page("小龙的表情收藏。", "日常心情、早晚问候与节日新衣，点一下就能在桌面预览。");
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3 };
        var hints = new Dictionary<string, string> { ["default"] = "日常待机", ["surprised"] = "连续戳戳 / 拖动 / 低额度", ["reading"] = "专注 / 编程 / 查看额度", ["smug"] = "点击 / B 站 / 好模型", ["peek"] = "鼠标靠近 / 聊天 / 音乐", ["gaming"] = "双击 / 游戏启动器", ["morning"] = "06:00–11:00 · 揉揉眼睛", ["sleepy"] = "23:00–05:00 · 困困啦", ["festive"] = "元旦 / 春节 · 新年装", ["christmas"] = "12 月 24–26 日 · 圣诞装" };
        foreach (var pair in Sprites.Names)
        {
            var card = Ui.Stack(); card.Children.Add(new Image { Source = Sprites.Get(pair.Key), Width = 142, Height = 142, Margin = new(0, 5, 0, 12) }); card.Children.Add(Ui.Text(pair.Value, 13, "#66517E", true)); card.Children.Add(Ui.Text(hints.GetValueOrDefault(pair.Key, "小龙的新心情"), 11, "#A092AC"));
            var b = Ui.Button("", () => pet.PreviewExpression(pair.Key)); b.Content = card; b.Background = Brushes.White; b.Padding = new(10); b.Margin = new(0, 0, 12, 12); grid.Children.Add(b);
        }
        page.Children.Add(grid); page.Children.Add(Ui.Card(Ui.Text("表情在互动结束后回到当前场景。原有白底 / 黑底图片在显示时去底，新生成的 PNG 保留原生透明背景。全部素材保存在“图片”文件夹。", 12, "#9587A2"), "#F0EBF7")); return page;
    }
    StackPanel FocusPage()
    {
        var page = Page("一起，把眼前这一点做好。", "小龙安静读书，你专心做事；休息时再抬头说一句辛苦啦。");
        var session = Ui.Stack(); session.Children.Add(Ui.Check("开启专注陪伴", pet.Settings.FocusEnabled, v => { pet.Settings.FocusEnabled = v; Save(); RefreshFocusView(); }));
        focusText = Ui.Text("", 22, "#7960AA", true); session.Children.Add(focusText);
        var actions = new WrapPanel(); focusAction = Ui.Button("开始专注", () => { pet.ToggleFocus(); RefreshFocusView(); }, true); actions.Children.Add(focusAction);
        focusReset = Ui.Button("结束本轮", () => { pet.ResetFocus(); RefreshFocusView(); }); actions.Children.Add(focusReset); session.Children.Add(actions);
        session.Children.Add(Ui.Text("专注期间暂停自动应用台词和时间问候，点击仍可互动。完成后自动进入休息，下一轮由你手动开始。", 12, "#9587A2")); page.Children.Add(Ui.Card(session));
        var durations = Ui.Stack(); durations.Children.Add(Ui.Text("你的番茄钟节奏", 17, bold: true));
        durations.Children.Add(Ui.Text("专注时长 · 1–180 分钟", 13)); var work = Ui.Input(pet.Settings.FocusMinutes.ToString()); durations.Children.Add(work);
        durations.Children.Add(Ui.Text("休息时长 · 1–60 分钟", 13)); var rest = Ui.Input(pet.Settings.BreakMinutes.ToString()); durations.Children.Add(rest);
        var feedback = Ui.Text("修改时长从下一阶段开始生效，不会重置正在计时的阶段。", 12, "#9587A2");
        durations.Children.Add(Ui.Button("保存番茄钟时长", () => {
            if (!int.TryParse(work.Text, out int w) || w < 1 || w > 180 || !int.TryParse(rest.Text, out int r) || r < 1 || r > 60) { feedback.Text = "请填写范围内的整数：专注 1–180，休息 1–60 分钟。"; return; }
            pet.Settings.FocusMinutes = w; pet.Settings.BreakMinutes = r; Save(); feedback.Text = $"已保存：专注 {w} 分钟 / 休息 {r} 分钟，从下一阶段生效。";
        })); durations.Children.Add(feedback); page.Children.Add(Ui.Card(durations));
        page.Children.Add(Ui.Card(Ui.Text("桌宠脚下会显示倒计时，右键菜单也能开始或暂停。正常退出会保存并暂停本轮，下次启动后可继续；关闭专注开关会结束本轮。电脑睡眠期间时间继续流逝，醒来后只进入下一阶段，不补跑多轮。", 12, "#9587A2"), "#F0EBF7"));
        RefreshFocusView(); return page;
    }
    void RefreshFocusView()
    {
        if (focusText == null || focusAction == null || focusReset == null) return;
        focusText.Text = (pet.Settings.FocusEnabled ? pet.Pomodoro.Status(DateTimeOffset.UtcNow) : "专注陪伴已关闭") + $"\n累计完成 {pet.Pomodoro.State.Completed} 轮";
        focusAction.IsEnabled = pet.Settings.FocusEnabled;
        focusAction.Content = pet.Pomodoro.Running ? "暂停计时" : pet.Pomodoro.State.Phase is FocusPhase.Focus or FocusPhase.Break ? "继续计时" : "开始专注";
        focusReset.IsEnabled = pet.Pomodoro.State.Phase != FocusPhase.Idle;
    }
    StackPanel MomentsPage()
    {
        var page = Page("把时间，过成小小的惊喜。", "按电脑本地时间触发，每段问候只说一次；重启也不会重复问好。");
        var options = Ui.Stack(); options.Children.Add(Ui.Check("开启时间彩蛋", pet.Settings.TimeEggsEnabled, v => { pet.Settings.TimeEggsEnabled = v; Save(); }));
        options.Children.Add(Ui.Check("早安问候 · 06:00–11:00，揉揉眼睛醒过来", pet.Settings.MorningEnabled, v => { pet.Settings.MorningEnabled = v; Save(); }));
        options.Children.Add(Ui.Check("深夜困困 · 23:00–次日 05:00，轻轻提醒休息", pet.Settings.NightEnabled, v => { pet.Settings.NightEnabled = v; Save(); }));
        options.Children.Add(Ui.Check("节日装扮 · 元旦、春节、圣诞", pet.Settings.HolidayEnabled, v => { pet.Settings.HolidayEnabled = v; Save(); }));
        options.Children.Add(Ui.Text("节日期间待机会穿上新衣；点击互动后会换回来。春节按农历正月初一到初七，圣诞为 12 月 24–26 日，元旦为 1 月 1 日。节日问候每个节日一次。", 12, "#9587A2")); page.Children.Add(Ui.Card(options));
        var previews = Ui.Stack(); previews.Children.Add(Ui.Text("现在就看看新表情", 17, bold: true)); var buttons = new WrapPanel();
        foreach (var (key, title) in new[] { ("morning", "早安 · 揉眼睛"), ("sleepy", "深夜 · 困困"), ("festive", "新年装"), ("christmas", "圣诞装") }) buttons.Children.Add(Ui.Button(title, () => pet.PreviewExpression(key)));
        previews.Children.Add(buttons); previews.Children.Add(Ui.Text("预览不会消耗当天的问候。专注、隐藏、全屏静默或关闭气泡时暂不问候；恢复后仅在对应时段内触发。", 12, "#9587A2")); page.Children.Add(Ui.Card(previews)); return page;
    }
}
