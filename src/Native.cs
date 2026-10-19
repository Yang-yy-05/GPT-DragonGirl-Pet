using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace DragonPet;

public static class Native
{
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int size);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint Size, Time; }

    public static ForegroundInfo? Foreground()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == Environment.ProcessId) return null;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            var title = new StringBuilder(1024);
            GetWindowText(hwnd, title, title.Capacity);
            var screen = Forms.Screen.FromHandle(hwnd).Bounds;
            GetWindowRect(hwnd, out var rect);
            bool fullscreen = rect.Left <= screen.Left && rect.Top <= screen.Top && rect.Right >= screen.Right && rect.Bottom >= screen.Bottom
                && !string.Equals(process.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase);
            return new(process.ProcessName, title.ToString(), fullscreen);
        }
        catch (Exception ex) when (ex is ArgumentException or System.ComponentModel.Win32Exception or InvalidOperationException) { return null; }
    }
    public static void MakeToolWindow(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var style = GetWindowLongPtr(hwnd, -20).ToInt64();
        SetWindowLongPtr(hwnd, -20, new IntPtr(style | 0x80 | 0x08000000)); // TOOLWINDOW | NOACTIVATE
    }
    public static bool IsAlwaysOnTop(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        return handle != IntPtr.Zero && (GetWindowLongPtr(handle, -20).ToInt64() & 0x8) != 0;
    }
    public static void SetAlwaysOnTop(Window window, bool enabled)
    {
        window.Topmost = enabled;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero) SetWindowPos(handle, new IntPtr(enabled ? -1 : -2), 0, 0, 0, 0, 0x213); // no resize, move, activation, owner-order change
    }
    public static Point Cursor(Window window)
    {
        GetCursorPos(out var p);
        var source = PresentationSource.FromVisual(window);
        return source?.CompositionTarget?.TransformFromDevice.Transform(new Point(p.X, p.Y)) ?? new Point(p.X, p.Y);
    }
    public static double IdleSeconds()
    {
        var input = new LASTINPUTINFO { Size = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        return GetLastInputInfo(ref input) ? unchecked((uint)Environment.TickCount - input.Time) / 1000.0 : 0;
    }
    public static Rect WorkArea(Window window)
    {
        var s = Forms.Screen.FromHandle(new WindowInteropHelper(window).Handle).WorkingArea;
        var matrix = PresentationSource.FromVisual(window)?.CompositionTarget?.TransformFromDevice;
        return matrix.HasValue ? new Rect(matrix.Value.Transform(new Point(s.Left, s.Top)), matrix.Value.Transform(new Point(s.Right, s.Bottom))) : new Rect(s.X, s.Y, s.Width, s.Height);
    }
    public static Point ClampPosition(Point point, Size size, Rect area) => new(
        Math.Clamp(point.X, area.Left, Math.Max(area.Left, area.Right - size.Width)),
        Math.Clamp(point.Y, area.Top, Math.Max(area.Top, area.Bottom - size.Height)));
}
