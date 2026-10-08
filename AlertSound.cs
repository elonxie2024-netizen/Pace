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
    public static readonly string[] BuiltInSounds = { "Gentle notes", "Rain on leaves", "Ocean wash", "Morning bird", "Soft kalimba", "Sand drift" };

    [DllImport("winmm.dll", CharSet = CharSet.Auto)]
    private static extern int mciSendString(string command, StringBuilder result, int resultLength, IntPtr callback);

    [DllImport("winmm.dll", CharSet = CharSet.Auto)]
    private static extern bool mciGetErrorString(int errorCode, StringBuilder errorText, int errorTextLength);

    public void Play(int volume, string customPath, string builtInSound)
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
            stream = new MemoryStream(CreateWave(volume, builtInSound), false);
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
        return CreateWave(volume, BuiltInSounds[0]);
    }

    public static string NormalizeBuiltInSound(string value)
    {
        foreach (string sound in BuiltInSounds) if (string.Equals(sound, value, StringComparison.Ordinal)) return sound;
        return BuiltInSounds[0];
    }

    public static byte[] CreateWave(int volume, string builtInSound)
    {
        const int sampleRate = 44100;
        const int sampleCount = sampleRate * 3 / 2;
        double gain = Math.Max(0, Math.Min(100, volume)) / 100.0 * 0.34;
        string preset = NormalizeBuiltInSound(builtInSound);
        uint noiseState = 0x7f4a7c15; double slowNoise = 0, fastNoise = 0;
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
                noiseState = noiseState * 1664525u + 1013904223u;
                double noise = ((noiseState >> 8) / 8388607.5) - 1.0;
                slowNoise += 0.012 * (noise - slowNoise); fastNoise += 0.085 * (noise - fastNoise);
                double value;
                if (preset == "Rain on leaves") value = gain * 0.80 * (0.72 * fastNoise + 0.28 * slowNoise);
                else if (preset == "Ocean wash")
                {
                    double swell = 0.22 + 0.78 * (0.5 - 0.5 * Math.Cos(2 * Math.PI * time / 1.45));
                    value = gain * 1.10 * swell * (0.72 * slowNoise + 0.28 * fastNoise);
                }
                else if (preset == "Sand drift") value = gain * 0.80 * Math.Sin(Math.PI * time / 1.5) * (fastNoise - 0.55 * slowNoise);
                else if (preset == "Morning bird") value = gain * Bird(time);
                else if (preset == "Soft kalimba") value = gain * Kalimba(time);
                else value = gain * GentleNotes(time);
                value = Math.Max(-0.75, Math.Min(0.75, value));
                writer.Write((short)Math.Round(value * short.MaxValue));
            }
            writer.Flush();
            return buffer.ToArray();
        }
    }

    private static double GentleNotes(double time)
    {
        return GentleNote(time,0.08,523.25)+GentleNote(time,0.64,659.25);
    }

    private static double GentleNote(double time,double start,double frequency) {
        double local=time-start,length=0.78; if(local<0 || local>=length)return 0;
        double envelope=Math.Min(1.0,local/0.055)*Math.Min(1.0,(length-local)/0.18)*Math.Exp(-local/0.42);
        return envelope*(0.82*Math.Sin(2*Math.PI*frequency*local)+0.13*Math.Sin(4*Math.PI*frequency*local)+0.05*Math.Sin(Math.PI*frequency*local));
    }

    private static double Kalimba(double time)
    {
        return KalimbaNote(time,0.06,392.0)+KalimbaNote(time,0.47,493.88)+KalimbaNote(time,0.88,587.33);
    }

    private static double KalimbaNote(double time,double start,double frequency) {
        double local=time-start; if(local<0 || local>=0.58)return 0;
        double envelope=Math.Min(1.0,local/0.025)*Math.Exp(-local/0.19)*Math.Min(1.0,(0.58-local)/0.08);
        return 0.72*envelope*(0.88*Math.Sin(2*Math.PI*frequency*local)+0.12*Math.Sin(6*Math.PI*frequency*local));
    }

    private static double Bird(double time)
    {
        return BirdChirp(time,0.16)+BirdChirp(time,0.84);
    }
    private static double BirdChirp(double time,double start) {
        double local=time-start,length=0.34;if(local<0 || local>=length)return 0;
        double phase=2*Math.PI*(720*local+410*local*local),envelope=Math.Pow(Math.Sin(Math.PI*local/length),1.4);
        return 0.38*envelope*(0.86*Math.Sin(phase)+0.14*Math.Sin(phase*1.5));
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
