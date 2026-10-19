using System;
using System.IO;
using System.Runtime.InteropServices;

namespace DragonPet;

public static class DesktopIntegration
{
    public const string ShortcutName = "GPT 小龙桌宠.lnk";
    public const string ShortcutDescription = "GPT 小龙桌宠 · 桌面陪伴";
    public static string StartupLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), ShortcutName);
    public static string DesktopLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ShortcutName);
    public static string IconPath => Path.Combine(AppContext.BaseDirectory, "assets", "gpt-dragon.ico");
    public static string Executable => Path.Combine(AppContext.BaseDirectory, "GPTPet.exe");

    public static (string Target, string Arguments, string Icon, string WorkingDirectory, string Description)? ReadShortcut(string path)
    {
        if (!File.Exists(path)) return null;
        object? shell = null, link = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows 快捷方式服务不可用。"));
            link = ((dynamic)shell!).CreateShortcut(path);
            return ((string)((dynamic)link).TargetPath, (string)((dynamic)link).Arguments, (string)((dynamic)link).IconLocation, (string)((dynamic)link).WorkingDirectory, (string)((dynamic)link).Description);
        }
        finally { if (link != null) Marshal.FinalReleaseComObject(link); if (shell != null) Marshal.FinalReleaseComObject(shell); }
    }
    static bool IsOurs((string Target, string Arguments, string Icon, string WorkingDirectory, string Description) value) =>
        value.Description == ShortcutDescription && Path.GetFileName(value.Target).Equals("GPTPet.exe", StringComparison.OrdinalIgnoreCase);
    public static bool StartupEnabled(string? linkPath = null)
    {
        var existing = ReadShortcut(linkPath ?? StartupLink);
        return existing.HasValue && IsOurs(existing.Value) && existing.Value.Arguments == "--autostart"
            && Path.GetFullPath(existing.Value.Target).Equals(Path.GetFullPath(Executable), StringComparison.OrdinalIgnoreCase);
    }
    public static void SetStartup(bool enabled, string? linkPath = null)
    {
        var path = linkPath ?? StartupLink;
        if (enabled) CreateShortcut(path, "--autostart");
        else if (ReadShortcut(path) is { } existing)
        {
            if (!IsOurs(existing)) throw new InvalidOperationException("该位置存在其他快捷方式，未修改它。");
            File.Delete(path); // Delete only our exact, verified startup link, never a directory.
        }
    }
    public static void CreateShortcut(string path, string arguments = "")
    {
        if (!Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("快捷方式文件必须使用 .lnk 扩展名。");
        if (!File.Exists(Executable)) throw new FileNotFoundException("找不到 GPTPet.exe。", Executable);
        var previous = ReadShortcut(path);
        if (previous.HasValue && !IsOurs(previous.Value)) throw new InvalidOperationException("同名文件属于其他快捷方式，未覆盖它。");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        object? shell = null, link = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows 快捷方式服务不可用。"));
            link = ((dynamic)shell!).CreateShortcut(Path.GetFullPath(path));
            dynamic shortcut = link;
            shortcut.TargetPath = Executable; shortcut.WorkingDirectory = AppContext.BaseDirectory;
            shortcut.Arguments = arguments; shortcut.Description = ShortcutDescription;
            shortcut.IconLocation = (File.Exists(IconPath) ? IconPath : Executable) + ",0";
            shortcut.WindowStyle = 1; shortcut.Save();
        }
        finally { if (link != null) Marshal.FinalReleaseComObject(link); if (shell != null) Marshal.FinalReleaseComObject(shell); }
    }
}
