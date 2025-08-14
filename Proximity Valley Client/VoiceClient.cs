using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics.PackedVector;
using NAudio.Wave;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using System;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Proximity_Valley;

public class VoiceClient
{
    private readonly IMonitor Monitor;

    internal ModEntry modEntry = null!; // wird extern gesetzt

    private UdpClient udpClient;
    private WaveInEvent waveIn;

    private DateTime lastVoiceDetected = DateTime.MinValue;

    // enthält für jeden anderen Spieler den aktuellen Audio‑Stream
    internal readonly Dictionary<long, PlayerAudioStream> playerAudioStreams = new();

	public enum PacketType : byte
	{
		Audio = 0x01,
		Location = 0x02,
		Connect = 0x03,
		Disconnect = 0x04,
    }

    public VoiceClient(IMonitor monitor)
    {
        Monitor = monitor;
    }

    public void Start() => Task.Run(Init);

    private async Task Init()
    {
        try
        {
            Monitor.Log("[Voice] Initializing UDP socket...", LogLevel.Debug);
            udpClient = new UdpClient(modEntry.Config.LocalPort) { EnableBroadcast = false };

            SetupInput();

            // erste Registrierung
            SendPacket(PacketType.Connect, modEntry.playerID, Array.Empty<byte>());
            Monitor.Log("[Voice] Connecting to Voice Server", LogLevel.Debug);

            // Empfangsschleife
            Monitor.Log("[Voice] Waiting for audio packets...", LogLevel.Debug);
            while (true)
            {
                UdpReceiveResult result = await udpClient.ReceiveAsync();

                byte[] decrypted = Decrypt(result.Buffer);
                using MemoryStream stream = new(decrypted);
                using BinaryReader reader = new(stream);

                byte pType = reader.ReadByte();
                long playerId = reader.ReadInt64();
                byte extraData = reader.ReadByte();
                byte[] payload = reader.ReadBytes((int)(stream.Length - stream.Position));

                if (pType == (byte)PacketType.Audio)
                    _ = Task.Run(() => ProcessIncomingAudio(playerId, payload, extraData));
                else
                    Monitor.Log($"[Voice] Unknown packet type: {pType}", LogLevel.Trace);
            }
        }
        catch (Exception ex)
        {
            Monitor.Log($"[Voice] ERROR: {ex.Message}", LogLevel.Error);
        }
    }

    private void ProcessIncomingAudio(long playerId, byte[] audioData, byte extraData)
    {
        // 1) kein Self‑Audio
        if (playerId == modEntry.playerID)
            if (!modEntry.Config.HearSelf)
                return;

        // 2) nur echte Daten
        if (audioData.Length == 0)
            return;


        // hol (oder erstelle) den Stream für diesen Spieler
        if (!playerAudioStreams.TryGetValue(playerId, out PlayerAudioStream stream))
        {
            // initiale Lautstärke und Pan aus der Distanzberechnung
            (float volume, float pan) = GetVolumeAndPan(Game1.player, modEntry.GetFarmerByID(playerId));

            stream = new PlayerAudioStream(
                new WaveFormat(modEntry.Config.SampleRate, modEntry.Config.Bits, modEntry.Config.Channels),
                modEntry.Config.OutputBufferSeconds,
                modEntry.Config.WaveOutDevice,
                volume,
                pan
            );
            playerAudioStreams[playerId] = stream;
        }

        stream.isGlobalTalking = (extraData & 0x01) != 0; // Bit 0: Global Talk status

        // füge die neuen Samples hinzu
        stream.AddSamples(audioData, 0, audioData.Length);

        Monitor.Log($"[Voice] Received {audioData.Length} bytes from {playerId}", LogLevel.Trace);
    }

    public void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady)
            return;

        foreach (KeyValuePair<long, PlayerAudioStream> kvp in playerAudioStreams.ToList())  // ToList, damit wir nicht währenddessen ändern
        {
            long playerId = kvp.Key;
            PlayerAudioStream stream = kvp.Value;

            // Basiswerte aus Entfernung/Direktion
            var remote = modEntry.GetFarmerByID(playerId);
            if (remote == null)
            {
                // Spieler nicht gefunden -> komplett stumm schalten
                stream.UpdatePanAndVolume(0, 0);
                continue;
            }
            (float volume, float pan) = GetVolumeAndPan(Game1.player, remote, stream.isGlobalTalking);

            stream.UpdatePanAndVolume(pan, volume * modEntry.Config.OutputVolume);
        }
    }

    private void SetupInput()
    {
        waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(modEntry.Config.SampleRate, modEntry.Config.Bits, modEntry.Config.Channels),
            BufferMilliseconds = modEntry.Config.BufferMilliseconds,
            DeviceNumber = modEntry.Config.WaveInDevice
        };

        waveIn.DataAvailable += (s, e) =>
        {
            try
            {
                // wenn gemuted, dann nichts senden
                if (modEntry.isMuted) return;

                // wenn PushToTalk aktiviert ist, dann nur senden, wenn Taste gedrückt
                if (modEntry.Config.PushToTalk && !modEntry.isPushToTalking) return;

                byte[] buffer = BoostAudio(e.Buffer, e.BytesRecorded);

                CalculateMicVolume(buffer, e.BytesRecorded);

                if (!ShouldSendAudio(buffer)) return;

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

    private byte[] BoostAudio(byte[] buffer, int bytes)
    {
        // Boost direkt am Mikrofon
        for (int i = 0; i < bytes; i += 2)
        {
            short sample = BitConverter.ToInt16(buffer, i);
            sample = (short)Math.Clamp(sample * modEntry.Config.InputVolume, short.MinValue, short.MaxValue);
            byte[] boosted = BitConverter.GetBytes(sample);
            buffer[i] = boosted[0];
            buffer[i + 1] = boosted[1];
        }

        return buffer;
    }

    internal float micVolumeLevel = 0;
    private void CalculateMicVolume (byte[] buffer, int bytes)
    {
        short[] sampleBuffer = new short[bytes / 2];
        Buffer.BlockCopy(buffer, 0, sampleBuffer, 0, bytes);

        double sumSquares = 0;
        foreach (short sample in sampleBuffer)
            sumSquares += sample * sample;

        micVolumeLevel = (float)Math.Sqrt(sumSquares / sampleBuffer.Length) / short.MaxValue;
    }

    public void Stop()
    {
        waveIn?.StopRecording();
        waveIn?.Dispose();
        udpClient?.Close();
        Monitor.Log("[Voice] Client stopped", LogLevel.Debug);
    }

    public void SendPacket(PacketType packetType, long playerId, byte[] payload)
    {
        try
        {
            using MemoryStream stream = new();
            using BinaryWriter writer = new(stream);

            writer.Write((byte)packetType);
            writer.Write(playerId);
            byte extraData = 0;
            extraData |= (byte)(modEntry.isGlobalTalking ? 0x01 : 0x00); // Bit 0: Global Talk status
            writer.Write(extraData);
            writer.Write(payload);

            byte[] fullPacket = Encrypt(stream.ToArray());
            udpClient.Send(fullPacket, fullPacket.Length, modEntry.Config.ServerAddress, modEntry.Config.ServerPort);

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

        if (rms > modEntry.Config.InputThreshold)
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

    public (float volume, float pan) GetVolumeAndPan(Farmer local, Farmer remote, bool talkingGlobal = false)
    {
        if (local == null || remote == null) return (0f, 0f);

        // 1. Entfernung berechnen
        float distance = Vector2.Distance(local.Position, remote.Position);
        float maxDistance = 32f * Game1.tileSize; // Max hörbarer Bereich

        // 2. Lautstärke abnehmen mit Distanz (linear oder exponentiell)
        //float volume = Math.Max(0f, 1f - (distance / maxDistance));
        //volume = MathF.Pow(volume, 1.5f); // optional: sanfterer Abfall
        float volume = (float)VolumeFallOffFuntion(distance / Game1.tileSize, 1.1f, 45, 12, 0.1f);
        if (talkingGlobal) volume = 1;

        // 3. Richtung für Panning (Links/Rechts)
        float dx = remote.Position.X - local.Position.X;
        float pan = Math.Clamp(dx / maxDistance, -1f, 1f); // -1 = links, 1 = rechts

        return (volume, pan);
    }

    public static double VolumeFallOffFuntion(double x, double kappa, double mu, double sigma, double beta)
    {
        double exponent = (x - mu) / sigma;
        double denominator = kappa * (1 + Math.Exp(exponent));
        return 1 / denominator + beta;
    }



    #region AES Encryption

    private Aes CreateAes()
    {
        Aes aes = Aes.Create();
        aes.Key = Encoding.UTF8.GetBytes(modEntry.Config.EncryptionKey);
        aes.IV = Encoding.UTF8.GetBytes(modEntry.Config.EncryptionIV);
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        return aes;
    }

    private byte[] Encrypt(byte[] data)
    {
        using Aes aes = CreateAes();
        using ICryptoTransform enc = aes.CreateEncryptor();
        return enc.TransformFinalBlock(data, 0, data.Length);
    }

    private byte[] Decrypt(byte[] data)
    {
        using Aes aes = CreateAes();
        using ICryptoTransform dec = aes.CreateDecryptor();
        return dec.TransformFinalBlock(data, 0, data.Length);
    }

    #endregion
}