using System;
using System.IO;
using System.Media;

// Owns the in-memory waveform for the full lifetime of asynchronous playback.
public sealed class AlertSound : IDisposable
{
    private SoundPlayer player;
    private MemoryStream stream;
    private bool disposed;

    public void Play(int volume)
    {
        Stop();
        if (disposed || volume <= 0) return;
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
        const double pulseLength = 0.36;
        const double pulseSpacing = 0.50;
        const double fadeLength = 0.018;
        double gain = Math.Max(0, Math.Min(100, volume)) / 100.0 * 0.92;
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
                double localTime = time - Math.Floor(time / pulseSpacing) * pulseSpacing;
                double value = 0;
                if (localTime < pulseLength)
                {
                    double fade = Math.Min(1.0, Math.Min(localTime / fadeLength,
                        (pulseLength - localTime) / fadeLength));
                    double envelope = 0.5 - 0.5 * Math.Cos(Math.PI * fade);
                    // Both components stay prominent through music; their summed
                    // amplitudes are bounded below full scale to avoid clipping.
                    double tone = 0.6 * Math.Sin(2 * Math.PI * 880 * localTime)
                        + 0.4 * Math.Sin(2 * Math.PI * 1320 * localTime);
                    value = gain * envelope * tone;
                }
                writer.Write((short)Math.Round(value * short.MaxValue));
            }
            writer.Flush();
            return buffer.ToArray();
        }
    }
}
