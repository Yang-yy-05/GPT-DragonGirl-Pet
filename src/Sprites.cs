using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DragonPet;

public static class Sprites
{
    public static readonly Dictionary<string, string> Names = new() {
        ["default"] = "日常 · 呆萌", ["surprised"] = "惊讶 · 哇！", ["reading"] = "读书 · 嘘", ["smug"] = "托腮 · 小傲娇", ["peek"] = "探头 · 在吗", ["gaming"] = "游戏 · 开一局",
        ["morning"] = "早安 · 揉眼睛", ["sleepy"] = "深夜 · 困困", ["festive"] = "新年 · 灯笼装", ["christmas"] = "圣诞 · 礼物装"
    };
    static readonly Dictionary<string, string> Files = new() {
        ["default"] = "初始.png", ["surprised"] = "表情/0ba40e904965c48705614f7b44323d26.jpg",
        ["reading"] = "表情/448c4d57a318ed57e87589c29c903379.jpg", ["smug"] = "表情/66441e74176f5c63e09b28a81b332418.jpg",
        ["peek"] = "表情/edba135774f2057b523f91420ad24364.jpg", ["gaming"] = "表情/f3e2eade87d35c68b5f9e94bbb81563b.jpg",
        ["morning"] = "时间彩蛋/早安揉眼睛.png", ["sleepy"] = "时间彩蛋/深夜困困.png", ["festive"] = "时间彩蛋/新年灯笼装.png", ["christmas"] = "时间彩蛋/圣诞礼物装.png"
    };
    static readonly Dictionary<string, BitmapSource> Cache = new();
    public static BitmapSource Get(string key)
    {
        if (!Files.ContainsKey(key)) key = "default";
        if (Cache.TryGetValue(key, out var cached)) return cached;
        var bitmap = new BitmapImage();
        bitmap.BeginInit(); bitmap.UriSource = new Uri(Path.Combine(AppContext.BaseDirectory, "图片", Files[key]));
        bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 640; bitmap.EndInit();
        var bgra = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        int w = bgra.PixelWidth, h = bgra.PixelHeight;
        var bytes = new byte[w * h * 4]; bgra.CopyPixels(bytes, w * 4, 0);
        // Runtime color-key rendering only; original artwork is never modified.
        if (Files[key].EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
        {
            for (int i = 0; i < bytes.Length; i += 4)
            {
                int max = Math.Max(bytes[i], Math.Max(bytes[i + 1], bytes[i + 2]));
                if (max < 48) bytes[i + 3] = (byte)Math.Clamp((max - 9) * 255 / 39, 0, 255);
            }
        }
        else if (key == "default")
        {
            // Only remove near-white pixels connected to the outside. White hair stays opaque.
            var seen = new bool[w * h]; var queue = new Queue<int>();
            void Seed(int p)
            {
                if (seen[p]) return; seen[p] = true; int i = p * 4;
                if (bytes[i] < 240 || bytes[i + 1] < 240 || bytes[i + 2] < 240) return;
                bytes[i + 3] = 0; queue.Enqueue(p);
            }
            for (int x = 0; x < w; x++) { Seed(x); Seed((h - 1) * w + x); }
            for (int y = 0; y < h; y++) { Seed(y * w); Seed(y * w + w - 1); }
            while (queue.Count > 0)
            {
                int p = queue.Dequeue(), x = p % w, y = p / w;
                if (x > 0) Seed(p - 1); if (x < w - 1) Seed(p + 1); if (y > 0) Seed(p - w); if (y < h - 1) Seed(p + w);
            }
        }
        var result = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bytes, w * 4);
        result.Freeze(); Cache[key] = result; return result;
    }
}

public sealed class PetImage : Image
{
    protected override HitTestResult? HitTestCore(PointHitTestParameters p)
    {
        if (Source is not BitmapSource b || ActualWidth <= 0 || ActualHeight <= 0) return null;
        int x = (int)(p.HitPoint.X / ActualWidth * b.PixelWidth), y = (int)(p.HitPoint.Y / ActualHeight * b.PixelHeight);
        if (x < 0 || y < 0 || x >= b.PixelWidth || y >= b.PixelHeight) return null;
        var pixel = new byte[4]; b.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return pixel[3] > 30 ? new PointHitTestResult(this, p.HitPoint) : null;
    }
}
