using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DragonPet;

public static class AssetBuilder
{
    public static void Create(string directory)
    {
        Directory.CreateDirectory(directory);
        var frames = new List<(int Size, byte[] Png)>();
        // Package the existing character as standard Windows icon sizes; no new character artwork.
        foreach (int size in new[] { 16, 24, 32, 48, 64, 128, 256 })
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen()) dc.DrawImage(Sprites.Get("default"), new Rect(0, 0, size, size));
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
            var rendered = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32); rendered.Render(visual);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(rendered));
            using var memory = new MemoryStream(); encoder.Save(memory); var png = memory.ToArray(); frames.Add((size, png));
            if (size == 256) File.WriteAllBytes(Path.Combine(directory, "gpt-dragon.png"), png);
        }
        using (var output = new BinaryWriter(File.Create(Path.Combine(directory, "gpt-dragon.ico"))))
        {
            output.Write((ushort)0); output.Write((ushort)1); output.Write((ushort)frames.Count);
            int offset = 6 + frames.Count * 16;
            foreach (var frame in frames)
            {
                output.Write((byte)(frame.Size == 256 ? 0 : frame.Size)); output.Write((byte)(frame.Size == 256 ? 0 : frame.Size)); output.Write((byte)0); output.Write((byte)0);
                output.Write((ushort)1); output.Write((ushort)32); output.Write(frame.Png.Length); output.Write(offset); offset += frame.Png.Length;
            }
            foreach (var frame in frames) output.Write(frame.Png);
        }
    }
}
