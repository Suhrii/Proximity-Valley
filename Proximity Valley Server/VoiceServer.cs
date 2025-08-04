using Newtonsoft.Json;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Proximity_Valley_Server
{
    public class VoiceServer
    {
        private readonly UdpClient _udpServer;
        private readonly Dictionary<IPEndPoint, string> _clientMap = [];

        // Statistik: Bytes pro Client
        private readonly Dictionary<IPEndPoint, UserPackages> _bytesReceived = [];

        // Event-Log (Connect/Disconnect/Location) für Anzeige
        private readonly List<string> _eventLogs = [];
        private readonly Lock consoleLock = new();
        private readonly string logFilePath;

        private readonly ServerConfig _config;

        enum PacketType : byte
        {
            Audio = 0x01,
            Location = 0x02,
            Connect = 0x03,
            Disconnect = 0x04,
        }

        struct UserPackages
        {
            public int bytesReceived;
            public int packagesReceived;
        }

        public VoiceServer()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            if (File.Exists(path))
                _config = JsonConvert.DeserializeObject<ServerConfig>(File.ReadAllText(path)) ?? new ServerConfig();
            else
                _config = new ServerConfig();

            _udpServer = new UdpClient(_config.ListenPort);
            Console.WriteLine($"{DateTime.Now:HH:mm:ss}: [Server] UDP voice server started on port {_config.ListenPort}");

            // Log-Datei für Connect/Disconnect/Location
            logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                DateTime.Now.ToString("yyyy-MM-dd") + ".log");

            // Hintergrund-Task: Statusanzeige oben, Event-Log unten
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    lock (consoleLock)
                    {
                        Console.Clear();
                        Console.WriteLine("=== Connected Clients (bytes received) ===");
                        foreach (var kvp in _bytesReceived)
                        {
                            Console.WriteLine($"{kvp.Key.Address}:{kvp.Key.Port} – {(kvp.Value.bytesReceived == 0 ? kvp.Value.bytesReceived : kvp.Value.bytesReceived / kvp.Value.packagesReceived)} bytes/s");
                            _bytesReceived[kvp.Key] = new();
                        }
                        Console.WriteLine();
                        Console.WriteLine("=== Event Log (last events) ===");
                        foreach (var line in _eventLogs)
                            Console.WriteLine(line);
                    }
                    await Task.Delay(1_000);
                }
            });
        }

        public async Task StartAsync()
        {
            Console.WriteLine($"{DateTime.Now:HH:mm:ss}: [Server] Voice server running...");

            while (true)
            {
                try
                {
                    UdpReceiveResult result = await _udpServer.ReceiveAsync();
                    IPEndPoint sender = result.RemoteEndPoint;
                    byte[] data = Decrypt(result.Buffer);

                    // Statistik: alle eingehenden Bytes zählen
                    lock (_bytesReceived)
                    {
                        if (!_bytesReceived.ContainsKey(sender))
                            _bytesReceived[sender] = new();

                        if (_bytesReceived.TryGetValue(sender, out UserPackages stats))
                        {
                            stats.bytesReceived += result.Buffer.Length;
                            stats.packagesReceived++;
                            _bytesReceived[sender] = stats;
                        }
                    }

                    using MemoryStream stream = new(data);
                    using BinaryReader reader = new(stream);

                    byte packetType = reader.ReadByte();
                    long playerId = reader.ReadInt64();
                    byte extraData = reader.ReadByte();
                    byte[] payload = reader.ReadBytes((int)(stream.Length - stream.Position));

                    switch (packetType)
                    {
                        case (byte)PacketType.Location:
                            string mapName = Encoding.UTF8.GetString(payload);
                            lock (_clientMap)
                                _clientMap[sender] = mapName;
                            LogEvent($"Updated map of {sender.Address}:{sender.Port} ({playerId}) to '{mapName}'");
                            break;

                        case (byte)PacketType.Connect:
                            lock (_clientMap)
                                _clientMap[sender] = "World";
                            LogEvent($"Connected {sender.Address}:{sender.Port} ({playerId})");
                            break;

                        case (byte)PacketType.Disconnect:
                            lock (_clientMap)
                                _clientMap.Remove(sender);
                            lock (_bytesReceived)
                                _bytesReceived.Remove(sender);
                            LogEvent($"Disconnected {sender.Address}:{sender.Port} ({playerId})");
                            break;

                        case (byte)PacketType.Audio:
                            // Audio-Pakete werden nicht in Event-Log geschrieben, aber weitergeleitet
                            await HandleAudioAsync(packetType, playerId, [extraData, ..payload], sender);
                            break;

                        default:
                            LogEvent($"Unknown packet type: {packetType} from {sender.Address}:{sender.Port}");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{DateTime.Now:HH:mm:ss}: [Server] Receive ERROR: {ex}");
                }
            }
        }

        private async Task HandleAudioAsync(byte packetType, long playerId, byte[] payload, IPEndPoint sender)
        {
            if (!_clientMap.TryGetValue(sender, out string? senderMap))
                return;

            List<IPEndPoint> targets;
            lock (_clientMap)
            {
                targets = _clientMap
                    .Where(kvp => (kvp.Value == senderMap || senderMap == "World")
                                  && !(kvp.Key.Address.Equals(sender.Address) && kvp.Key.Port == sender.Port))
                    .Select(kvp => kvp.Key)
                    .ToList();
            }

            if (targets.Count == 0) return;

            using MemoryStream forwardStream = new();
            using (BinaryWriter writer = new(forwardStream))
            {
                writer.Write(packetType);
                writer.Write(playerId);
                writer.Write(payload);
            }

            byte[] forwardData = Encrypt(forwardStream.ToArray());

            foreach (IPEndPoint target in targets)
            {
                try
                {
                    await _udpServer.SendAsync(forwardData, forwardData.Length, target);
                }
                catch (Exception sendEx)
                {
                    Console.WriteLine($"{DateTime.Now:HH:mm:ss}: [Server] Send error to {target}: {sendEx.Message}");
                }
            }
        }

        private void LogEvent(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            string msg = $"{timestamp}: [Server] {message}";
            lock (consoleLock)
            {
                _eventLogs.Add(msg);
                if (_eventLogs.Count > 10)
                    _eventLogs.RemoveAt(0);
            }
            File.AppendAllText(logFilePath, msg + Environment.NewLine);
        }

        public void Stop()
        {
            _udpServer.Close();
            Console.WriteLine($"{DateTime.Now:HH:mm:ss}: [Server] UDP server stopped.");
        }

        #region AES helpers
        private Aes CreateAes()
        {
            var aes = Aes.Create();
            aes.Key = Encoding.UTF8.GetBytes(_config.EncryptionKey);
            aes.IV = Encoding.UTF8.GetBytes(_config.EncryptionIV);
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
}