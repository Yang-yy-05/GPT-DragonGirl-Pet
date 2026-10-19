using System;
using System.Buffers.Binary;
using System.IO;
using System.Media;
using System.Text;

namespace DragonPet;

// Plays the original sound extracted from the user's video, with PCM volume control.
public sealed class DuckSound : IDisposable
{
    SoundPlayer? player;
    MemoryStream? stream;
    byte[]? original;
    int loadedVolume = -1;
    long lastPlayed = long.MinValue;
    public string? LastError { get; private set; }
    public static string SoundPath => Path.Combine(AppContext.BaseDirectory, "assets", "rubber-duck.wav");
    public bool Prepare(int volume)
    {
        volume = Math.Clamp(volume, 0, 100);
        if (volume == 0) { Stop(); LastError = null; return true; }
        try
        {
            if (player == null || loadedVolume != volume)
            {
                player?.Stop(); player?.Dispose(); stream?.Dispose();
                player = null; stream = null; loadedVolume = -1;
                original ??= File.ReadAllBytes(SoundPath);
                stream = new MemoryStream(WithVolume(original, volume)); player = new SoundPlayer(stream); player.Load(); loadedVolume = volume;
            }
            LastError = null; return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or InvalidDataException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { LastError = "音效暂不可用，请检查 assets/rubber-duck.wav 文件和系统音频输出。"; return false; }
    }
    public void Play(int volume)
    {
        volume = Math.Clamp(volume, 0, 100);
        if (volume == 0) { Stop(); LastError = null; return; }
        long now = Environment.TickCount64;
        if (lastPlayed != long.MinValue && now - lastPlayed < 70) return;
        // Prepared at startup / volume changes, so a press normally does no file I/O or conversion.
        if (!Prepare(volume)) return;
        try { player!.Play(); lastPlayed = now; LastError = null; }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        { LastError = "音效暂不可用，请检查系统音频输出。"; }
    }
    public void Stop() => player?.Stop();
    public static byte[] LoadWave(int volume = 100) => WithVolume(File.ReadAllBytes(SoundPath), volume);

    internal static (int Offset, int Length) PcmData(byte[] wav)
    {
        if (wav.Length < 44 || Encoding.ASCII.GetString(wav, 0, 4) != "RIFF" || Encoding.ASCII.GetString(wav, 8, 4) != "WAVE" ||
            BinaryPrimitives.ReadUInt32LittleEndian(wav.AsSpan(4, 4)) + 8L != wav.Length)
            throw new InvalidDataException("Invalid WAV container.");
        int dataOffset = 0, dataLength = 0, blockAlign = 0;
        for (int offset = 12; offset < wav.Length;)
        {
            if (wav.Length - offset < 8) throw new InvalidDataException("Truncated WAV chunk.");
            string id = Encoding.ASCII.GetString(wav, offset, 4);
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(wav.AsSpan(offset + 4, 4));
            long next = offset + 8L + length + (length & 1);
            if (next > wav.Length) throw new InvalidDataException("Truncated WAV data.");
            if (id == "fmt ")
            {
                if (length < 16) throw new InvalidDataException("Missing WAV format.");
                var format = wav.AsSpan(offset + 8, 16);
                int channels = BinaryPrimitives.ReadUInt16LittleEndian(format.Slice(2, 2));
                blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(format.Slice(12, 2));
                if (BinaryPrimitives.ReadUInt16LittleEndian(format) != 1 || channels is < 1 or > 2 ||
                    BinaryPrimitives.ReadUInt32LittleEndian(format.Slice(4, 4)) == 0 ||
                    BinaryPrimitives.ReadUInt16LittleEndian(format.Slice(14, 2)) != 16 || blockAlign != channels * 2)
                    throw new InvalidDataException("Expected 16-bit PCM WAV.");
            }
            else if (id == "data") { dataOffset = offset + 8; dataLength = (int)length; }
            offset = (int)next;
        }
        if (blockAlign == 0 || dataOffset == 0 || dataLength == 0 || dataLength % blockAlign != 0)
            throw new InvalidDataException("Missing or incomplete PCM samples.");
        return (dataOffset, dataLength);
    }

    internal static byte[] WithVolume(byte[] original, int volume)
    {
        var data = PcmData(original);
        var result = (byte[])original.Clone();
        volume = Math.Clamp(volume, 0, 100);
        if (volume == 100) return result;
        for (int offset = data.Offset; offset < data.Offset + data.Length; offset += 2)
        {
            var sample = result.AsSpan(offset, 2);
            BinaryPrimitives.WriteInt16LittleEndian(sample, (short)(BinaryPrimitives.ReadInt16LittleEndian(sample) * volume / 100));
        }
        return result;
    }
    public void Dispose() { player?.Stop(); player?.Dispose(); stream?.Dispose(); }
}
