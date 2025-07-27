// VoiceServer.cs
using Newtonsoft.Json;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace ProximityValleyServer
{
    public class ServerConfig
    {
        public int ListenPort { get; set; } = 5000;
        public string EncryptionKey { get; set; } = "Gmj%8k%65xmpCjvzLG8FkUYz3FPyfTnv";
        public string EncryptionIV { get; set; } = "LmiEy!AF3bTT$Pwk";
    }

    class VoiceServer
    {
        private readonly UdpClient udpServer;
        private readonly Dictionary<IPEndPoint, string> clientMap = [];
        private readonly ServerConfig config;

        enum PacketType : byte
        {
            Audio = 0x01,
            Location = 0x02,
            Connect = 0x03,
            Disconnect = 0x04,
        }

        public VoiceServer()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            if (File.Exists(path))
                config = JsonConvert.DeserializeObject<ServerConfig>(File.ReadAllText(path)) ?? new ServerConfig();
            else
                config = new ServerConfig();

            udpServer = new UdpClient(config.ListenPort);
            Console.WriteLine($"{DateTime.Now}: [Server] UDP voice server started on port {config.ListenPort}");
        }

        public async Task StartAsync()
        {
            Console.WriteLine($"{DateTime.Now}: [Server] Voice server running...");

            while (true)
            {
                try
                {
                    var result = await udpServer.ReceiveAsync();
                    var sender = result.RemoteEndPoint;
                    var data = Decrypt(result.Buffer);

                    _ = Task.Run(() => HandlePackages(data, sender));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{DateTime.Now}: [Server] Receive ERROR: {ex}");
                }
            }
        }

        private async Task HandlePackages(byte[] data, IPEndPoint sender)
        {
            try
            {
                using var stream = new MemoryStream(data);
                using var reader = new BinaryReader(stream);

                byte packetType = reader.ReadByte();
                long playerId = reader.ReadInt64();
                byte[] payload = reader.ReadBytes((int)(stream.Length - stream.Position));

                switch (packetType)
                {
                    case (byte)PacketType.Audio:
                        if (!clientMap.TryGetValue(sender, out string? senderMap))
                            return;

                        List<IPEndPoint> targets;
                        lock (clientMap)
                        {
                            targets = [ .. clientMap
                                .Where(kvp => (kvp.Value == senderMap || senderMap == "World")
                                               && kvp.Key != sender)
                                .Select(kvp => kvp.Key) ];
                        }

                        if (targets.Count == 0) return;

                        using (var forwardStream = new MemoryStream())
                        {
                            using var writer = new BinaryWriter(forwardStream);
                            writer.Write(packetType);
                            writer.Write(playerId);
                            writer.Write(payload);

                            byte[] forwardData = Encrypt(forwardStream.ToArray());

                            foreach (var target in targets)
                            {
                                try
                                {
                                    await udpServer.SendAsync(forwardData, forwardData.Length, target);
                                }
                                catch (Exception sendEx)
                                {
                                    Console.WriteLine($"{DateTime.Now}: [Server] Send error to {target}: {sendEx.Message}");
                                }
                            }
                        }
                        break;

                    case (byte)PacketType.Location:
                        string mapName = Encoding.UTF8.GetString(payload);
                        lock (clientMap)
                            clientMap[sender] = mapName;
                        Console.WriteLine($"{DateTime.Now}: [Server] Updated map of {sender} ({playerId}) to '{mapName}'");
                        break;

                    case (byte)PacketType.Connect:
                        lock (clientMap)
                            clientMap[sender] = "World";
                        Console.WriteLine($"{DateTime.Now}: [Server] Connected {sender} ({playerId})");
                        break;

                    case (byte)PacketType.Disconnect:
                        lock (clientMap)
                            clientMap.Remove(sender);
                        Console.WriteLine($"{DateTime.Now}: [Server] Disconnected {sender} ({playerId})");
                        break;

                    default:
                        Console.WriteLine($"{DateTime.Now}: [Server] Unknown packet type: {packetType}");
                        break;
                }
            }
            catch (Exception innerEx)
            {
                Console.WriteLine($"{DateTime.Now}: [Server] Task ERROR: {innerEx}");
            }
        }

        public void Stop()
        {
            udpServer.Close();
            Console.WriteLine($"{DateTime.Now}: [Server] UDP server stopped.");
        }

        /* ---------- AES helpers ---------- */
        private Aes CreateAes()
        {
            var aes = Aes.Create();
            aes.Key = Encoding.UTF8.GetBytes(config.EncryptionKey);
            aes.IV = Encoding.UTF8.GetBytes(config.EncryptionIV);
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
