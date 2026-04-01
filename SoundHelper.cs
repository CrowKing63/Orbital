using System;
using System.IO;
using System.Media;

namespace Orbital
{
    internal static class SoundHelper
    {
        /// <summary>Plays a subtle tick when the action bar pops up.</summary>
        public static void PlayPopupSound()
        {
            if (!SettingsManager.CurrentSettings.SoundEnabled) return;
            PlayAsync(frequency: 820, durationMs: 35, decayRate: 55, volume: 0.13);
        }

        /// <summary>Plays a subtle confirmation tone when an action completes.</summary>
        public static void PlayActionSound()
        {
            if (!SettingsManager.CurrentSettings.SoundEnabled) return;
            PlayAsync(frequency: 1100, durationMs: 55, decayRate: 45, volume: 0.15);
        }

        private static void PlayAsync(double frequency, int durationMs, double decayRate, double volume)
        {
            try
            {
                byte[] wav = GenerateWav(frequency, durationMs, decayRate, volume);
                // MemoryStream is kept alive by SoundPlayer until playback finishes
                var ms = new MemoryStream(wav);
                var player = new SoundPlayer(ms);
                player.Play(); // non-blocking
            }
            catch { /* non-fatal: audio failure must never crash the app */ }
        }

        /// <summary>
        /// Generates a mono 44100 Hz 16-bit PCM WAV in memory.
        /// The tone uses a sine wave with an exponential decay envelope.
        /// </summary>
        private static byte[] GenerateWav(double frequency, int durationMs, double decayRate, double volume)
        {
            const int sampleRate   = 44100;
            const int bitsPerSample = 16;
            const int channels     = 1;
            int sampleCount = sampleRate * durationMs / 1000;
            int dataSize    = sampleCount * (bitsPerSample / 8);

            using var ms = new MemoryStream(44 + dataSize);
            using var w  = new BinaryWriter(ms);

            // ── RIFF header ──
            w.Write((byte)'R'); w.Write((byte)'I'); w.Write((byte)'F'); w.Write((byte)'F');
            w.Write(36 + dataSize);                         // ChunkSize
            w.Write((byte)'W'); w.Write((byte)'A'); w.Write((byte)'V'); w.Write((byte)'E');

            // ── fmt sub-chunk ──
            w.Write((byte)'f'); w.Write((byte)'m'); w.Write((byte)'t'); w.Write((byte)' ');
            w.Write(16);                                    // SubChunk1Size (PCM)
            w.Write((short)1);                             // AudioFormat = PCM
            w.Write((short)channels);                      // NumChannels
            w.Write(sampleRate);                           // SampleRate
            w.Write(sampleRate * channels * (bitsPerSample / 8)); // ByteRate
            w.Write((short)(channels * (bitsPerSample / 8)));      // BlockAlign
            w.Write((short)bitsPerSample);                 // BitsPerSample

            // ── data sub-chunk ──
            w.Write((byte)'d'); w.Write((byte)'a'); w.Write((byte)'t'); w.Write((byte)'a');
            w.Write(dataSize);

            // ── PCM samples: sine wave with exponential decay ──
            for (int i = 0; i < sampleCount; i++)
            {
                double t        = (double)i / sampleRate;
                double envelope = Math.Exp(-t * decayRate);
                double sample   = Math.Sin(2.0 * Math.PI * frequency * t) * envelope * volume;
                short  pcm      = (short)Math.Clamp(sample * short.MaxValue, short.MinValue, short.MaxValue);
                w.Write(pcm);
            }

            return ms.ToArray();
        }
    }
}
