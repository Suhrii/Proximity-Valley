// VoiceServer.cs

using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace Proximity_Valley_Server;

public class VoiceServer
{
    private readonly UdpClient _udpServer;
    private readonly Dictionary<IPEndPoint, string> _clientMap = [];
    private readonly ServerConfig _config;

    public VoiceServer()
    {
        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
        if (File.Exists(path))
            _config = JsonConvert.DeserializeObject<ServerConfig>(File.ReadAllText(path)) ?? new ServerConfig();
        else
            _config = new ServerConfig();

        _udpServer = new UdpClient(_config.ListenPort);
        Console.WriteLine($"{DateTime.Now}: [Server] UDP voice server started on port {_config.ListenPort}");
    }

    public async Task StartAsync()
    {
        Console.WriteLine($"{DateTime.Now}: [Server] Voice server running...");

        while (true)
        {
            try
            {
                UdpReceiveResult result = await _udpServer.ReceiveAsync();
                IPEndPoint sender = result.RemoteEndPoint;
                byte[] data = Decrypt(result.Buffer);

                using MemoryStream stream = new(data);
                using BinaryReader reader = new(stream);

                byte packetType = reader.ReadByte();
                long playerId = reader.ReadInt64();
                byte[] payload = reader.ReadBytes((int)(stream.Length - stream.Position));

                switch (packetType)
                {
                    case (byte)PacketType.Audio:
                        if (!_clientMap.TryGetValue(sender, out string? senderMap))
                            return;

                        List<IPEndPoint> targets;
                        lock (_clientMap)
                        {
                            targets = [.. _clientMap
                                .Where(kvp => (kvp.Value == senderMap || senderMap == "World")
                                                && !(kvp.Key.Address.Equals(sender.Address) && kvp.Key.Port == sender.Port))
                                .Select(kvp => kvp.Key)];
                        }

                        if (targets.Count == 0) return;

                        using (MemoryStream forwardStream = new())
                        {
                            await using BinaryWriter writer = new(forwardStream);
                            writer.Write(packetType);
                            writer.Write(playerId);
                            writer.Write(payload);

                            byte[] forwardData = Encrypt(forwardStream.ToArray());

                            foreach (IPEndPoint target in targets)
                            {
                                try
                                {
                                    await _udpServer.SendAsync(forwardData, forwardData.Length, target);
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
                        lock (_clientMap)
                            _clientMap[sender] = mapName;
                        Console.WriteLine($"{DateTime.Now}: [Server] Updated map of {sender} ({playerId}) to '{mapName}'");
                        break;

                    case (byte)PacketType.Connect:
                        lock (_clientMap)
                            _clientMap[sender] = "World";
                        Console.WriteLine($"{DateTime.Now}: [Server] Connected {sender} ({playerId})");
                        break;

                    case (byte)PacketType.Disconnect:
                        lock (_clientMap)
                            _clientMap.Remove(sender);
                        Console.WriteLine($"{DateTime.Now}: [Server] Disconnected {sender} ({playerId})");
                        break;

                    default:
                        Console.WriteLine($"{DateTime.Now}: [Server] Unknown packet type: {packetType}");
                        break;
                }

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
            using MemoryStream stream = new(data);
            using BinaryReader reader = new(stream);

            byte packetType = reader.ReadByte();
            long playerId = reader.ReadInt64();
            byte[] payload = reader.ReadBytes((int)(stream.Length - stream.Position));

            switch (packetType)
            {
                case (byte)PacketType.Audio:
                    if (!_clientMap.TryGetValue(sender, out string? senderMap))
                        return;

                    List<IPEndPoint> targets;
                    lock (_clientMap)
                    {
                        targets = [ .. _clientMap
                                       .Where(kvp => (kvp.Value == senderMap || senderMap == "World") && !Equals(kvp.Key, sender))
                                       .Select(kvp => kvp.Key) ];
                    }

                    if (targets.Count == 0) return;

                    using (MemoryStream forwardStream = new())
                    {
                        await using BinaryWriter writer = new(forwardStream);
                        writer.Write(packetType);
                        writer.Write(playerId);
                        writer.Write(payload);

                        byte[] forwardData = Encrypt(forwardStream.ToArray());

                        foreach (IPEndPoint target in targets)
                        {
                            try
                            {
                                await _udpServer.SendAsync(forwardData, forwardData.Length, target);
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
                    lock (_clientMap)
                        _clientMap[sender] = mapName;
                    Console.WriteLine($"{DateTime.Now}: [Server] Updated map of {sender} ({playerId}) to '{mapName}'");
                    break;

                case (byte)PacketType.Connect:
                    lock (_clientMap)
                        _clientMap[sender] = "World";
                    Console.WriteLine($"{DateTime.Now}: [Server] Connected {sender} ({playerId})");
                    break;

                case (byte)PacketType.Disconnect:
                    lock (_clientMap)
                        _clientMap.Remove(sender);
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
        _udpServer.Close();
        Console.WriteLine($"{DateTime.Now}: [Server] UDP server stopped.");
    }

    /* ---------- AES helpers ---------- */
    private Aes CreateAes()
    {
        Aes aes = Aes.Create();
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
}