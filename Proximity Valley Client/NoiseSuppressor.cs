// NoiseSuppressor.cs
using System;
using NAudio.Dsp;

namespace Proximity_Valley
{
    /// <summary>
    /// Simpler Echtzeit-Rauschfilter: 100 Hz High-Pass + sanftes Noise-Gate mit Attack/Release
    /// Arbeitet IN-PLACE auf 16-bit PCM little-endian.
    /// </summary>
    public class NoiseSuppressor
    {
        // Tweakbar (bewusst als Felder, damit du im Lauf testen kannst)
        public float HighPassCutoffHz = 100f;   // Rumpeln/PC-Lüfter/Handling
        public float GateMinGain = 0.05f;       // -26 dB, wie stark "zu" gemacht wird
        public int AttackMs = 15;               // Gate auf -> schnell
        public int ReleaseMs = 120;             // Gate zu -> langsam, natürliches Ausklingen
        public float NoiseMargin = 1.8f;        // Schwelle = NoiseFloor * Margin + FloorMin
        public float FloorMin = 0.0035f;        // Minimaler Schwellen-Offset
        public float FloorRise = 0.01f;         // wie schnell der Floor steigt (laute Umgebung)
        public float FloorFall = 0.2f;          // wie schnell der Floor sinkt (ruhiger wird’s)

        private readonly int sampleRate;
        private readonly int channels;

        private BiQuadFilter? hpfL, hpfR;
        private float noiseFloor = 0.004f;      // Startwert; passt sich an
        private float gateGain = 1f;

        public NoiseSuppressor(int sampleRate, int channels)
        {
            this.sampleRate = Math.Max(8000, sampleRate);
            this.channels = Math.Clamp(channels, 1, 2);
            UpdateFilters();
        }

        private void UpdateFilters()
        {
            float q = 0.707f;
            hpfL = BiQuadFilter.HighPassFilter(sampleRate, HighPassCutoffHz, q);
            if (channels > 1) hpfR = BiQuadFilter.HighPassFilter(sampleRate, HighPassCutoffHz, q);
        }

        /// <summary>
        /// Bearbeitet 16-bit PCM Little-Endian
        /// </summary>
        public void ProcessInPlace(byte[] buffer, int bytesRecorded)
        {
            if (buffer == null || bytesRecorded <= 0) return;

            // 1) RMS auf dem Puffer bestimmen (float konvertiert)
            double sumSq = 0;
            int sampleCount = bytesRecorded / 2;
            for (int i = 0; i < bytesRecorded; i += 2)
            {
                short s = BitConverter.ToInt16(buffer, i);
                float f = s / 32768f;
                sumSq += (double)f * f;
            }
            float rms = (float)Math.Sqrt(sumSq / Math.Max(1, sampleCount));

            // 2) Noise-Floor adaptiv nachführen (zwei Zeiten: langsam hoch, schneller runter)
            if (rms > noiseFloor)
                noiseFloor = noiseFloor * (1f - FloorRise) + rms * FloorRise;
            else
                noiseFloor = noiseFloor * (1f - FloorFall) + rms * FloorFall;

            // 3) Gate-Ziel bestimmen
            float threshold = MathF.Max(FloorMin, noiseFloor * NoiseMargin + FloorMin);
            float targetGain = (rms >= threshold) ? 1f : GateMinGain;

            // 4) Glättung (Attack/Release in Abhängigkeit der Samples)
            float attackCoeff = (float)Math.Exp(-1.0 / (AttackMs * 0.001 * sampleRate));
            float releaseCoeff = (float)Math.Exp(-1.0 / (ReleaseMs * 0.001 * sampleRate));

            // 5) In-Place filtern + Gain anwenden
            //    Stereo wird als LRLR... erwartet (WaveIn liefert das so)
            for (int i = 0; i < bytesRecorded; i += 2)
            {
                short s = BitConverter.ToInt16(buffer, i);
                float f = s / 32768f;

                // High-Pass je Kanal
                if (channels == 1)
                {
                    f = hpfL!.Transform(f);
                }
                else
                {
                    // Kanalindex bestimmen (0 = L, 1 = R), hier mit Sample-Index arbeitend:
                    int sampleIndex = (i / 2);
                    bool isRight = (sampleIndex % 2) == 1;
                    f = (isRight ? hpfR! : hpfL!).Transform(f);
                }

                // Gain glätten
                bool opening = targetGain > gateGain;
                float coeff = opening ? attackCoeff : releaseCoeff;
                gateGain = coeff * gateGain + (1f - coeff) * targetGain;

                float o = f * gateGain;

                // zurück nach 16-bit
                o = MathF.Max(-1f, MathF.Min(1f, o));
                short so = (short)MathF.Round(o * 32767f);
                buffer[i] = (byte)(so & 0xFF);
                buffer[i + 1] = (byte)((so >> 8) & 0xFF);
            }
        }
    }
}
