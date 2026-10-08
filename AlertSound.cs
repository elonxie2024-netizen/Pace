using System;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;

// Owns the in-memory waveform for the full lifetime of asynchronous playback.
public sealed class AlertSound : IDisposable
{
    private SoundPlayer player;
    private MemoryStream stream;
    private bool disposed;
    private bool customOpen;
    private const string CustomAlias = "PaceAlertSound";

    [DllImport("winmm.dll", CharSet = CharSet.Auto)]
    private static extern int mciSendString(string command, StringBuilder result, int resultLength, IntPtr callback);

    [DllImport("winmm.dll", CharSet = CharSet.Auto)]
    private static extern bool mciGetErrorString(int errorCode, StringBuilder errorText, int errorTextLength);

    public void Play(int volume, string customPath)
    {
        Stop();
        if (disposed || volume <= 0) return;
        if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath) && TryOpenCustom(customPath))
        {
            // Imported sounds vary wildly in mastering level, so Pace caps them
            // below the Windows MCI maximum even when the slider is at 100%.
            mciSendString("setaudio " + CustomAlias + " volume to " + Math.Max(0, Math.Min(700, volume * 7)), null, 0, IntPtr.Zero);
            if (mciSendString("play " + CustomAlias + " from 0", null, 0, IntPtr.Zero) == 0) return;
            CloseCustom();
        }
        try
        {
            stream = new MemoryStream(CreateWave(volume), false);
            player = new SoundPlayer(stream);
            player.Load();
            player.Play();
        }
        catch (Exception)
        {
            // A missing or unavailable audio device must not interrupt tracking.
            Stop();
        }
    }

    public void Stop()
    {
        CloseCustom();
        if (player != null)
        {
            try { player.Stop(); } catch (Exception) { }
            try { player.Dispose(); } catch (Exception) { }
            player = null;
        }
        if (stream != null)
        {
            stream.Dispose();
            stream = null;
        }
    }

    public void Dispose()
    {
        Stop();
        disposed = true;
    }

    public static byte[] CreateWave(int volume)
    {
        const int sampleRate = 44100;
        const int sampleCount = sampleRate * 3 / 2;
        double gain = Math.Max(0, Math.Min(100, volume)) / 100.0 * 0.34;
        double[] starts = { 0.08, 0.64 };
        double[] frequencies = { 523.25, 659.25 };
        using (MemoryStream buffer = new MemoryStream(44 + sampleCount * 2))
        using (BinaryWriter writer = new BinaryWriter(buffer))
        {
            writer.Write(new byte[] { 82, 73, 70, 70 }); // RIFF
            writer.Write(36 + sampleCount * 2);
            writer.Write(new byte[] { 87, 65, 86, 69 }); // WAVE
            writer.Write(new byte[] { 102, 109, 116, 32 }); // fmt
            writer.Write(16);
            writer.Write((short)1); // PCM
            writer.Write((short)1); // mono
            writer.Write(sampleRate);
            writer.Write(sampleRate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(new byte[] { 100, 97, 116, 97 }); // data
            writer.Write(sampleCount * 2);
            for (int i = 0; i < sampleCount; i++)
            {
                double time = i / (double)sampleRate;
                double value = 0;
                for (int note = 0; note < starts.Length; note++)
                {
                    double localTime = time - starts[note], length = 0.78;
                    if (localTime < 0 || localTime >= length) continue;
                    double attack = Math.Min(1.0, localTime / 0.055);
                    double release = Math.Min(1.0, (length - localTime) / 0.18);
                    double envelope = attack * release * Math.Exp(-localTime / 0.42);
                    double frequency = frequencies[note];
                    double tone = 0.82 * Math.Sin(2 * Math.PI * frequency * localTime)
                        + 0.13 * Math.Sin(2 * Math.PI * frequency * 2 * localTime)
                        + 0.05 * Math.Sin(2 * Math.PI * frequency * 0.5 * localTime);
                    value += gain * envelope * tone;
                }
                value = Math.Max(-0.75, Math.Min(0.75, value));
                writer.Write((short)Math.Round(value * short.MaxValue));
            }
            writer.Flush();
            return buffer.ToArray();
        }
    }

    private static string MciError(int code)
    {
        StringBuilder message = new StringBuilder(256);
        return mciGetErrorString(code, message, message.Capacity) ? message.ToString() : "Windows could not read this audio file.";
    }

    private bool TryOpenCustom(string path)
    {
        int result = mciSendString("open \"" + path + "\" alias " + CustomAlias, null, 0, IntPtr.Zero);
        customOpen = result == 0;
        return customOpen;
    }

    private void CloseCustom()
    {
        if (!customOpen) return;
        mciSendString("stop " + CustomAlias, null, 0, IntPtr.Zero);
        mciSendString("close " + CustomAlias, null, 0, IntPtr.Zero);
        customOpen = false;
    }

    public static bool TryGetDuration(string path, out double seconds, out string error)
    {
        seconds = 0; error = "";
        if (string.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using (FileStream file = File.OpenRead(path))
                using (BinaryReader reader = new BinaryReader(file))
                {
                    if (new string(reader.ReadChars(4)) != "RIFF") throw new InvalidDataException();
                    reader.ReadInt32();
                    if (new string(reader.ReadChars(4)) != "WAVE") throw new InvalidDataException();
                    int byteRate = 0, dataBytes = 0;
                    while (file.Position + 8 <= file.Length)
                    {
                        string chunk = new string(reader.ReadChars(4)); int size = reader.ReadInt32();
                        if (size < 0 || file.Position + size > file.Length) throw new InvalidDataException();
                        long next = file.Position + size + (size & 1);
                        if (chunk == "fmt " && size >= 16)
                        {
                            reader.ReadInt16(); reader.ReadInt16(); reader.ReadInt32(); byteRate = reader.ReadInt32();
                        }
                        else if (chunk == "data") dataBytes = size;
                        file.Position = Math.Min(next, file.Length);
                    }
                    if (byteRate <= 0 || dataBytes <= 0) throw new InvalidDataException();
                    seconds = dataBytes / (double)byteRate;
                    return seconds > 0;
                }
            }
            catch { error = "The WAV file is incomplete or unreadable."; return false; }
        }
        string alias = "PaceSoundCheck" + Guid.NewGuid().ToString("N");
        int result = mciSendString("open \"" + path + "\" alias " + alias, null, 0, IntPtr.Zero);
        if (result != 0) { error = MciError(result); return false; }
        try
        {
            StringBuilder length = new StringBuilder(64);
            result = mciSendString("status " + alias + " length", length, length.Capacity, IntPtr.Zero);
            int milliseconds;
            if (result != 0 || !int.TryParse(length.ToString(), out milliseconds) || milliseconds <= 0)
            { error = result == 0 ? "The audio file has no readable duration." : MciError(result); return false; }
            seconds = milliseconds / 1000.0; return true;
        }
        finally { mciSendString("close " + alias, null, 0, IntPtr.Zero); }
    }
}
