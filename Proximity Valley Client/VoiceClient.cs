using Microsoft.Xna.Framework;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Proximity_Valley;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using System.IO;
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

        private DateTime lastVoiceDetected = DateTime.MinValue;

        // enthält für jeden anderen Spieler den aktuellen Audio‑Stream
        private readonly Dictionary<long, PlayerAudioStream> _streams = new Dictionary<long, PlayerAudioStream>();

        // enthält für jeden Spieler eine manuell einstellbare Lautstärke (0.0–1.0)
        // Du kannst das später per In‑Game‑Menü befüllen
        private readonly Dictionary<long, float> _customVolumes = new Dictionary<long, float>();

        // enthält für jeden Spieler einen manuell einstellbaren Pan‑Wert (‑1.0 links bis +1.0 rechts)
        private readonly Dictionary<long, float> _customPans = new Dictionary<long, float>();



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
                SendPacket(PacketType.Connect, modEntry.playerID, Array.Empty<byte>());
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
                    long playerId = reader.ReadInt64();
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

        private void ProcessIncomingAudio(long playerId, byte[] audioData)
        {
            // 1) kein Self‑Audio
            if (playerId == modEntry.playerID)
                return;

            // 2) nur echte Daten
            if (audioData.Length == 0)
                return;


            // hol (oder erstelle) den Stream für diesen Spieler
            if (!_streams.TryGetValue(playerId, out PlayerAudioStream stream))
            {
                // initiale Lautstärke und Pan aus der Distanzberechnung
                (float volume, float pan) = GetVolumeAndPan(Game1.player, modEntry.GetFarmerByID(playerId));

                stream = new PlayerAudioStream(
                    new WaveFormat(Config.SampleRate, Config.Bits, 1),
                    Config.OutputBufferSeconds,
                    Config.WaveOutDevice,
                    volume,
                    pan
                );
                _streams[playerId] = stream;
            }

            // füge die neuen Samples hinzu
            stream.AddSamples(audioData, 0, audioData.Length);

            //waveProvider.AddSamples(audioData, 0, audioData.Length);
            //if (waveOut.PlaybackState != PlaybackState.Playing)
            //    waveOut.Play();

            Monitor.Log($"[Voice] Received {audioData.Length} bytes from {playerId}", LogLevel.Trace);
        }

        public void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady)
                return;

            foreach (var kvp in _streams.ToList())  // ToList, damit wir nicht währenddessen ändern
            {
                long playerId = kvp.Key;
                PlayerAudioStream stream = kvp.Value;

                // Basiswerte aus Entfernung/Direktion
                (float baseVolume, float basePan) = GetVolumeAndPan(Game1.player, modEntry.GetFarmerByID(playerId) ?? Game1.player);

                // überschreibe mit manuell gesetzten Werten, falls vorhanden
                float volume = _customVolumes.ContainsKey(playerId) ? _customVolumes[playerId] : baseVolume;
                //float pan = _customPans.ContainsKey(playerId) ? _customPans[playerId] : basePan;

                stream.UpdatePanAndVolume(basePan, volume);
            }
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
                    // wenn gemuted, dann nichts senden
                    if (modEntry.isMuted) return;

                    // wenn PushToTalk aktiviert ist, dann nur senden, wenn Taste gedrückt
                    if (Config.PushToTalk && !modEntry.isPushToTalking) return;

                    OnDataAvailable(s, e);

                    if (!ShouldSendAudio(e.Buffer)) return;

                    // **roh**es PCM senden – wird in SendPacket verschlüsselt
                    byte[] audioPayload = e.Buffer.Take(e.BytesRecorded).ToArray();

                    SendPacket(PacketType.Audio, modEntry.playerID, audioPayload);
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

        public void SendPacket(PacketType packetType, long playerId, byte[] payload)
        {
            try
            {
                using var stream = new MemoryStream();
                using var writer = new BinaryWriter(stream);

                writer.Write((byte)packetType);
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

        private readonly int HangTimeMilliseconds = 250;

        private bool ShouldSendAudio(byte[] buffer)
        {
            float rms = CalculateRMS(buffer);

            if (rms > Config.InputThreshold)
            {
                lastVoiceDetected = DateTime.UtcNow;
                return true;
            }

            return (DateTime.UtcNow - lastVoiceDetected).TotalMilliseconds < HangTimeMilliseconds;
        }

        private float CalculateRMS(byte[] buffer)
        {
            int sampleCount = buffer.Length / 2;
            double sum = 0;

            for (int i = 0; i < buffer.Length; i += 2)
            {
                short sample = BitConverter.ToInt16(buffer, i);
                sum += sample * sample;
            }

            return (float)Math.Sqrt(sum / sampleCount) / short.MaxValue;
        }

        public (float volume, float pan) GetVolumeAndPan(Farmer local, Farmer remote)
        {
            if (local == null || remote == null) return (0f, 0f);

            // 1. Entfernung berechnen
            float distance = Vector2.Distance(local.Position, remote.Position);
            float maxDistance = 32f * Game1.tileSize; // Max hörbarer Bereich

            // 2. Lautstärke abnehmen mit Distanz (linear oder exponentiell)
            float volume = Math.Max(0f, 1f - (distance / maxDistance));
            volume = MathF.Pow(volume, 1.5f); // optional: sanfterer Abfall

            // 3. Richtung für Panning (Links/Rechts)
            float dx = remote.Position.X - local.Position.X;
            float pan = Math.Clamp(dx / maxDistance, -1f, 1f); // -1 = links, 1 = rechts

            return (volume, pan);
        }



        #region AES Encryption

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

        #endregion
    }
}
