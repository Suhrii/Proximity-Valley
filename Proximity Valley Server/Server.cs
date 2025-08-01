namespace Proximity_Valley_Server;

class Program
{
    static async Task Main(string[] args)
    {
        VoiceServer server = new();
        await server.StartAsync();
    }
}