using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Proximity_Valley;

public class PlayerAudioStream
{
    public BufferedWaveProvider Buffer { get; }
    public VolumeSampleProvider VolumeProvider { get; }
    public PanningSampleProvider PanningProvider { get; }
    public WaveOutEvent Output { get; }

    public float userVolume = 1f;

    public PlayerAudioStream(WaveFormat format, int bufferSeconds, int device, float initialVolume, float initialPan)
    {
        Buffer = new BufferedWaveProvider(format)
        {
            DiscardOnBufferOverflow = true,
            BufferLength = format.AverageBytesPerSecond * bufferSeconds
        };

        ISampleProvider? sample = Buffer.ToSampleProvider();
        PanningProvider = new PanningSampleProvider(sample) { Pan = initialPan };
        VolumeProvider = new VolumeSampleProvider(PanningProvider) { Volume = initialVolume };

        Output = new WaveOutEvent { DeviceNumber = device };
        Output.Init(VolumeProvider);
        Output.Play();
    }

    public void AddSamples(byte[] data, int offset, int count)
    {
        Buffer.AddSamples(data, offset, count);
    }

    public void UpdatePanAndVolume(float pan, float volume)
    {
        PanningProvider.Pan = pan;
        VolumeProvider.Volume = volume * userVolume;
    }

    public void Dispose()
    {
        Output?.Dispose();
    }
}