namespace Proximity_Valley_Server;

class Program
{
    static async Task Main(string[] args)
    {
        bool hearSelf = false;

        foreach (string arg in args)
        {
            if (arg.StartsWith("--hearSelf"))
            {
                hearSelf = true;
            }
        }

        VoiceServer server = new(hearSelf);
        await server.StartAsync();
    }
}