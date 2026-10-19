using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;

namespace DragonPet;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--prepare-assets") { AssetBuilder.Create(args[1]); return 0; }
        if (args.Length >= 2 && args[0] == "--create-shortcut")
        {
            try { DesktopIntegration.CreateShortcut(args[1]); return 0; }
            catch (Exception ex) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "shortcut-error.txt"), ex.Message); return 2; }
        }
        if (args.Contains("--self-test")) return SelfTests.Run();
        if (args.Contains("--quota-check"))
        {
            var store = new ConfigStore();
            var settings = store.Load("settings.json", () => new PetSettings());
            using var quota = new QuotaService(settings);
            var result = quota.Refresh(true).GetAwaiter().GetResult();
            store.Save("quota-check.json", result);
            return result.Windows.Count > 0 ? 0 : 1;
        }
        using var mutex = new Mutex(true, "Local\\GPTDragonPet-1", out bool first);
        if (!first) { if (!args.Contains("--autostart")) MessageBox.Show("小龙已经在运行啦。右键桌宠或托盘图标可以打开设置。", "GPT 小龙桌宠"); return 0; }
        try
        {
            var store = new ConfigStore();
            var settings = store.Load("settings.json", () => new PetSettings()); settings.Normalize();
            var rules = store.Load("rules.json", ConfigStore.DefaultRules);
            try { ConfigStore.ValidateRules(rules); } catch (ArgumentException) { rules = ConfigStore.DefaultRules(); }
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.DispatcherUnhandledException += (_, e) =>
            {
                File.AppendAllText(Path.Combine(store.DirectoryPath, "errors.log"), $"{DateTimeOffset.Now:O} {e.Exception.GetType().Name}: {e.Exception.Message}\n{e.Exception.StackTrace}\n");
                MessageBox.Show("小龙遇到了异常，详情已保存在 data/errors.log。\n" + e.Exception.Message, "GPT 小龙桌宠");
                e.Handled = true; app.Shutdown(1);
            };
            var pet = new PetWindow(settings, rules, store, args.Contains("--inspect-window"));
            app.MainWindow = pet; pet.Show();
            if (args.Contains("--settings")) pet.OpenSettings();
            return app.Run();
        }
        catch (Exception e) { MessageBox.Show("启动失败：" + e.Message, "GPT 小龙桌宠"); return 1; }
    }
}
