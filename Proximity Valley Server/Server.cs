namespace Proximity_Valley_Server;

class Program
{
    static async Task Main(string[] args)
    {
        var server = new VoiceServer();
        await server.StartAsync();
    }
}