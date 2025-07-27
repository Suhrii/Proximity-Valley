using NAudio.Wave;
using StardewModdingAPI;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace ProximityValley
{
    public class VoiceClient
    {
        private readonly IMonitor Monitor;
        private readonly IModHelper Helper;
        private readonly ModConfig Config;

        internal ModEntry modEntry = null!; // wird extern gesetzt

        private UdpClient udpClient;
        private BufferedWaveProvider waveProvider;
        private WaveOutEvent waveOut;
        private WaveInEvent waveIn;

        public enum PacketType : byte
        {
            Audio = 0x01,
            Location = 0x02,
            Connect = 0x03,
            Disconnect = 0x04,
        }

        public VoiceClient(IMonitor monitor, IModHelper helper)
        {
            Monitor = monitor;
            Helper = helper;
            Config = Helper.ReadConfig<ModConfig>();
        }

        public void Start() => Task.Run(Init);

        private async Task Init()
        {
            try
            {
                Monitor.Log("[Voice] Initializing UDP socket...", LogLevel.Debug);
                udpClient = new UdpClient(Config.LocalPort) { EnableBroadcast = false };

                SetupOutput();
                SetupInput();

                // erste Registrierung
                SendPacket((byte)PacketType.Connect, modEntry.playerID.GetHashCode(), Array.Empty<byte>());
                Monitor.Log("[Voice] Connecting to Voice Server", LogLevel.Debug);

                // Empfangsschleife
                Monitor.Log("[Voice] Waiting for audio packets...", LogLevel.Debug);
                while (true)
                {
                    var result = await udpClient.ReceiveAsync();

                    byte[] decrypted = Decrypt(result.Buffer);
                    using var stream = new MemoryStream(decrypted);
                    using var reader = new BinaryReader(stream);

                    byte pType = reader.ReadByte();
                    int playerId = reader.ReadInt32();
                    byte[] payload = reader.ReadBytes((int)(stream.Length - stream.Position));

                    if (pType == (byte)PacketType.Audio)
                        ProcessIncomingAudio(playerId, payload);
                    else
                        Monitor.Log($"[Voice] Unknown packet type: {pType}", LogLevel.Trace);
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"[Voice] ERROR: {ex.Message}", LogLevel.Error);
            }
        }

        private void ProcessIncomingAudio(int playerId, byte[] audioData)
        {
            // 1) kein Self‑Audio
            if (playerId == modEntry.playerID.GetHashCode())
                return;

            // 2) nur echte Daten
            if (audioData.Length == 0)
                return;

            waveProvider.AddSamples(audioData, 0, audioData.Length);
            if (waveOut.PlaybackState != PlaybackState.Playing)
                waveOut.Play();

            Monitor.Log($"[Voice] Received {audioData.Length} bytes from {playerId}", LogLevel.Trace);
        }

        private void SetupInput()
        {
            waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(Config.SampleRate, Config.Bits, Config.Channels),
                BufferMilliseconds = Config.BufferMilliseconds,
                DeviceNumber = Config.WaveInDevice
            };

            waveIn.DataAvailable += (s, e) =>
            {
                try
                {
                    OnDataAvailable(s, e);

                    // **roh**es PCM senden – wird in SendPacket verschlüsselt
                    byte[] audioPayload = e.Buffer.Take(e.BytesRecorded).ToArray();

                    bool shouldSend =
                        micVolumeLevel > Config.InputThreshold && // Mikrofonpegel über Schwelle
                        (!Config.PushToTalk || (Config.PushToTalk && modEntry.isPushToTalking)) // Push-to-Talk aktiv und Taste gedrückt
                        && !modEntry.isMuted; // nicht stummgeschaltet

                    if (shouldSend)
                        SendPacket((byte)PacketType.Audio, modEntry.playerID.GetHashCode(), audioPayload);
                }
                catch (Exception ex)
                {
                    Monitor.Log($"[Voice] Audio Send ERROR: {ex.Message}", LogLevel.Warn);
                }
            };

            waveIn.StartRecording();
            Monitor.Log("[Voice] Microphone input started", LogLevel.Debug);
        }

        private void SetupOutput()
        {
            var format = new WaveFormat(Config.SampleRate, Config.Bits, Config.Channels);

            waveProvider = new BufferedWaveProvider(format)
            {
                DiscardOnBufferOverflow = true,
                BufferLength = format.AverageBytesPerSecond * Config.OutputBufferSeconds
            };

            var volumeProvider = new VolumeWaveProvider16(waveProvider)
            {
                Volume = Config.OutputVolume
            };

            waveOut = new WaveOutEvent { DeviceNumber = Config.WaveOutDevice };
            waveOut.Init(volumeProvider);
            waveOut.Play();

            Monitor.Log("[Voice] Audio output initialized", LogLevel.Debug);
        }

        public void Stop()
        {
            waveIn?.StopRecording();
            waveIn?.Dispose();
            waveOut?.Stop();
            waveOut?.Dispose();
            udpClient?.Close();
            Monitor.Log("[Voice] Client stopped", LogLevel.Debug);
        }

        internal float micVolumeLevel = 0;
        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            short[] sampleBuffer = new short[e.BytesRecorded / 2];
            Buffer.BlockCopy(e.Buffer, 0, sampleBuffer, 0, e.BytesRecorded);

            double sumSquares = 0;
            foreach (short sample in sampleBuffer)
                sumSquares += sample * sample;

            micVolumeLevel = (float)Math.Sqrt(sumSquares / sampleBuffer.Length) / short.MaxValue;
        }

        public void SendPacket(byte packetType, int playerId, byte[] payload)
        {
            try
            {
                using var stream = new MemoryStream();
                using var writer = new BinaryWriter(stream);

                writer.Write(packetType);
                writer.Write(playerId);
                writer.Write(payload);

                byte[] fullPacket = Encrypt(stream.ToArray());
                udpClient.Send(fullPacket, fullPacket.Length, Config.ServerAddress, Config.ServerPort);

                Monitor.Log($"[Voice] Sent {fullPacket.Length} bytes (type={packetType})", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor.Log($"[Voice] SendPacket ERROR: {ex.Message}", LogLevel.Warn);
            }
        }

        private Aes CreateAes()
        {
            var aes = Aes.Create();
            aes.Key = Encoding.UTF8.GetBytes(Config.EncryptionKey);
            aes.IV = Encoding.UTF8.GetBytes(Config.EncryptionIV);
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            return aes;
        }

        private byte[] Encrypt(byte[] data)
        {
            using var aes = CreateAes();
            using var enc = aes.CreateEncryptor();
            return enc.TransformFinalBlock(data, 0, data.Length);
        }

        private byte[] Decrypt(byte[] data)
        {
            using var aes = CreateAes();
            using var dec = aes.CreateDecryptor();
            return dec.TransformFinalBlock(data, 0, data.Length);
        }
    }
}
