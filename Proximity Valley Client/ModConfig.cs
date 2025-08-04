// ModConfig.cs

using StardewModdingAPI;

namespace Proximity_Valley;

public class ModConfig
{
    // Netzwerk
    public string ServerAddress { get; set; } = "127.0.0.1";
    public int ServerPort { get; set; } = 5000;
    public int LocalPort { get; set; } = 6000;
    public string EncryptionKey { get; set; } = "Gmj%8k%65xmpCjvzLG8FkUYz3FPyfTnv";
    public string EncryptionIV { get; set; } = "LmiEy!AF3bTT$Pwk";

    // Audio
    public int SampleRate { get; set; } = 8000;
    public int Bits { get; set; } = 16;
    public int Channels { get; set; } = 1;
    public int BufferMilliseconds { get; set; } = 20;
    public int OutputBufferSeconds { get; set; } = 2;
    public int InputVolume { get; set; } = 1;
    public float InputThreshold { get; set; } = 0.05f;
    public int HangTimeMilliseconds { get; set; } = 250;
    public int OutputVolume{ get; set; } = 1;
    public bool PushToTalk { get; set; } = false;
    public bool HearSelf { get; set; } = false;

    // Geräte
    public int WaveInDevice { get; set; } = 0;
    public int WaveOutDevice { get; set; } = -1;

    // Keybinds
    public SButton PushToTalkButton { get; set; } = SButton.C;
    public SButton GlobalTalkButton { get; set; } = SButton.V;
    public SButton ToggleMute { get; set; } = SButton.G;
    public SButton ToggleDevOptions { get; set; } = SButton.None;

    // Other Stuff
    public bool ShowSkillXP { get; set; } = false;
    public bool ShowRealtionshipPoints { get; set; } = false;
    public bool ShowPlayerNames { get; set; } = false;
    public int PlayerNamesScroll { get; set; } = 0;
    public int PlayerNamesRenderType { get; set; } = 0;
}