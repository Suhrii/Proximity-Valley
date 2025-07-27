using GenericModConfigMenu; // GMCM-API
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.BellsAndWhistles;
using System.Text;

namespace ProximityValley
{
    public class ModEntry : Mod
    {
        private VoiceClient voiceClient;
        private ModConfig Config;

        private string currentMap = "World";
        private bool isWalkieTalkie = false;

        internal long playerID = -1; // Unique ID for the player, initialized to -1
        internal bool isPushToTalking = false;
        internal bool isMuted = false;
        internal bool devOptionsEnabled = false; // Flag for enabling developer options

        internal static ModEntry Instance;
        private IModHelper Helper;

        public override void Entry(IModHelper helper)
        {
            Instance = this;
            Helper = helper;

            Monitor.Log($"[Voice] Boot", LogLevel.Debug);
            Config = helper.ReadConfig<ModConfig>();

            voiceClient = new VoiceClient(Monitor, helper);
            voiceClient.modEntry = this; // Set the mod entry reference in the voice client
            voiceClient.Start();

            helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            helper.Events.Player.Warped += OnWarped;
            helper.Events.Display.RenderedHud += OnRenderedHud;

            helper.Events.Input.ButtonPressed += Input_ButtonPressed;
            helper.Events.Input.ButtonReleased += Input_ButtonReleased;

        }

        private void Input_ButtonReleased(object? sender, ButtonReleasedEventArgs e)
        {
            if (e.Button == Config.GlobalTalkButton)
            {
                isWalkieTalkie = false;
                voiceClient.SendPacket((byte)VoiceClient.PacketType.Location, playerID, Encoding.UTF8.GetBytes(currentMap));
            }
            else if (e.Button == Config.PushToTalkButton)
            {
                isPushToTalking = false;
            }
        }

        private void Input_ButtonPressed(object? sender, ButtonPressedEventArgs e)
        {
            if (e.Button == Config.GlobalTalkButton)
            {
                isWalkieTalkie = true;
                voiceClient.SendPacket((byte)VoiceClient.PacketType.Location, playerID, Encoding.UTF8.GetBytes("World"));
            }
            else if (e.Button == Config.PushToTalkButton)
            {
                isPushToTalking = true;
            }
            else if (e.Button == Config.ToggleMute)
            {
                isMuted = !isMuted;
            }
            else if (e.Button == Config.ToggleDevOptions)
            {
                devOptionsEnabled = !devOptionsEnabled;
            }
        }

        private void OnRenderedHud(object sender, RenderedHudEventArgs e)
        {
            var b = e.SpriteBatch;
            // Mic Volume/Talking Indicator
            if (voiceClient.micVolumeLevel > Config.InputThreshold)
            {
                int height = Math.Clamp((int)(200 * voiceClient.micVolumeLevel), 0, 100);

                b.Draw(Game1.staminaRect, new Rectangle(20, b.GraphicsDevice.Viewport.Height - 120, 20, 100), Color.White);
                b.Draw(Game1.staminaRect, new Rectangle(20, b.GraphicsDevice.Viewport.Height - 20 - height, 20, height), Color.LimeGreen);

                if (isMuted)
                    SpriteText.drawString(b, $"Muted", 80, b.GraphicsDevice.Viewport.Height - 80, drawBGScroll: 1);
                else if (Config.PushToTalk && !isPushToTalking)
                    SpriteText.drawString(b, $"Muted (Press '{Config.PushToTalkButton}' to talk)", 80, b.GraphicsDevice.Viewport.Height - 80, drawBGScroll: 1);
                else
                    SpriteText.drawString(b, $"Talking {(isWalkieTalkie ? "Global" : "")}", 80, b.GraphicsDevice.Viewport.Height - 80, drawBGScroll: 1);
            }

            // Players Indicator
            int index = 0;
            foreach (var farmer in Game1.getOnlineFarmers())
            {
                if (farmer.currentLocation != Game1.player.currentLocation)
                    continue;

                // Positionsvergleich
                string arrow = "";
                if (farmer != Game1.player)
                {
                    var myPos = Game1.player.Position;
                    var otherPos = farmer.Position;
                    float dx = otherPos.X - myPos.X;
                    float dy = otherPos.Y - myPos.Y;

                    if (Math.Abs(dx) > Math.Abs(dy))
                    {
                        if (dx > 0)
                            arrow = ">";
                        else if (dx < 0)
                            arrow = "<";
                    }
                    else
                    {
                        if (dy > 0)
                            arrow = "v";
                        else if (dy < 0)
                            arrow = "^";
                    }

                    if (dx + dy < Game1.tileSize)
                        arrow = "$";
                }

                string devExtraPrefix = devOptionsEnabled ? $"{farmer.Position} " : "";
                string devExtraSuffix = devOptionsEnabled ? $" - {farmer.currentLocation.Name}" : "";

                SpriteText.drawString(b, $"{devExtraPrefix}{farmer.displayName} {arrow}{devExtraSuffix}", 50, b.GraphicsDevice.Viewport.Height - 200 - (index * 50), drawBGScroll: 0);

                index++;
            }

        }


        private void OnGameLaunched(object sender, EventArgs e)
        {
            Monitor.Log($"[Voice] Startet", LogLevel.Debug);

            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                try
                {
                    voiceClient.SendPacket((byte)VoiceClient.PacketType.Disconnect, playerID, Array.Empty<byte>());
                }
                catch
                {
                    // Ignoriere Fehler beim Shutdown
                }
            };


            playerID = Game1.player.UniqueMultiplayerID;


            // GMCM - Integration
            var gmcm = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (gmcm != null)
            {
                gmcm.Register(
                    mod: this.ModManifest,
                    reset: () => Config = new ModConfig(),
                    save: () => Helper.WriteConfig(Config)
                );

                gmcm.AddTextOption(
                    mod: this.ModManifest,
                    name: () => "Server Address",
                    tooltip: () => "Die IP oder der Hostname des Voice-Servers",
                    getValue: () => Config.ServerAddress,
                    setValue: value => Config.ServerAddress = value
                );

                gmcm.AddNumberOption(
                    mod: this.ModManifest,
                    name: () => "Server Port",
                    tooltip: () => "Port, auf dem der Voice-Server lauscht",
                    getValue: () => Config.ServerPort,
                    setValue: value => Config.ServerPort = value
                );

                gmcm.AddNumberOption(
                    mod: this.ModManifest,
                    name: () => "Local Port",
                    tooltip: () => "Lokaler UDP-Port für Empfang",
                    getValue: () => Config.LocalPort,
                    setValue: value => Config.LocalPort = value
                );

                gmcm.AddNumberOption(
                    mod: this.ModManifest,
                    name: () => "Sample Rate",
                    tooltip: () => "Abtastrate für Audio (Hz)",
                    getValue: () => Config.SampleRate,
                    setValue: value => Config.SampleRate = value
                );

                gmcm.AddNumberOption(
                    mod: this.ModManifest,
                    name: () => "Bits Per Sample",
                    tooltip: () => "Bits pro Abtastwert",
                    getValue: () => Config.Bits,
                    setValue: value => Config.Bits = value
                );

                gmcm.AddNumberOption(
                    mod: this.ModManifest,
                    name: () => "Channels",
                    tooltip: () => "Anzahl der Audiokanäle",
                    getValue: () => Config.Channels,
                    setValue: value => Config.Channels = value
                );

                gmcm.AddNumberOption(
                    mod: this.ModManifest,
                    name: () => "Buffer ms",
                    tooltip: () => "Puffergröße am Input (ms)",
                    getValue: () => Config.BufferMilliseconds,
                    setValue: value => Config.BufferMilliseconds = value
                );

                gmcm.AddNumberOption(
                    mod: this.ModManifest,
                    name: () => "Output Buffer s",
                    tooltip: () => "Pufferlänge am Output (Sekunden)",
                    getValue: () => Config.OutputBufferSeconds,
                    setValue: value => Config.OutputBufferSeconds = value
                );

                gmcm.AddNumberOption(
                    mod: this.ModManifest,
                    name: () => "WaveIn Device",
                    tooltip: () => "ID des Aufnahmegeräts",
                    getValue: () => Config.WaveInDevice,
                    setValue: value => Config.WaveInDevice = value
                );

                gmcm.AddNumberOption(
                    mod: this.ModManifest,
                    name: () => "WaveOut Device",
                    tooltip: () => "ID des Wiedergabegeräts",
                    getValue: () => Config.WaveOutDevice,
                    setValue: value => Config.WaveOutDevice = value
                );

                gmcm.AddNumberOption(
                    mod: this.ModManifest,
                    name: () => "Input Volume",
                    tooltip: () => "Lautstärke des Mikrofons (recommended 0-5)",
                    getValue: () => Config.InputVolume,
                    setValue: value => Config.InputVolume = value,
                    min: 0,
                    max: 10
                );
                gmcm.AddNumberOption(
                    mod: this.ModManifest,
                    name: () => "Output Volume",
                    tooltip: () => "Lautstärke der Wiedergabe (recommended 0-5)",
                    getValue: () => Config.OutputVolume,
                    setValue: value => Config.OutputVolume = value,
                    min: 0,
                    max: 10
                );
                gmcm.AddNumberOption(
                    mod: this.ModManifest,
                    name: () => "Input Threshold",
                    tooltip: () => "Schwelle für Mikrofonaktivierung (0.0-1.0)",
                    getValue: () => (int)(Config.InputThreshold * 100),
                    setValue: value => Config.InputThreshold = value / 100f,
                    min: 0,
                    max: 100
                );
                gmcm.AddNumberOption(
                    mod: this.ModManifest,
                    name: () => "Hang Time (ms)",
                    tooltip: () => "Zeit, die das Mikrofon nach unterschreitung des Thresholds an bleibt (ms)",
                    getValue: () => Config.HangTimeMilliseconds,
                    setValue: value => Config.HangTimeMilliseconds = value,
                    min: 0,
                    max: 1000
                );
                gmcm.AddBoolOption(
                    mod: this.ModManifest,
                    name: () => "Push to Talk",
                    tooltip: () => "Aktiviere Push-to-Talk (Taste gedrückt halten zum Sprechen)",
                    getValue: () => Config.PushToTalk,
                    setValue: value => Config.PushToTalk = value
                );

                gmcm.AddKeybind(
                    mod: this.ModManifest,
                    name: () => "Push to Talk",
                    tooltip: () => "Taste für Push-to-Talk",
                    getValue: () => Config.PushToTalkButton,
                    setValue: value => Config.PushToTalkButton = value
                );
                gmcm.AddKeybind(
                    mod: this.ModManifest,
                    name: () => "Global Talk",
                    tooltip: () => "Taste für globales Sprechen (Walkie-Talkie)",
                    getValue: () => Config.GlobalTalkButton,
                    setValue: value => Config.GlobalTalkButton = value
                );
                gmcm.AddKeybind(
                    mod: this.ModManifest,
                    name: () => "Toggle Mute",
                    tooltip: () => "Taste zum Stummschalten/Entstummen",
                    getValue: () => Config.ToggleMute,
                    setValue: value => Config.ToggleMute = value
                );
            }

        }

        private void OnWarped(object sender, WarpedEventArgs e)
        {
            if (e.IsLocalPlayer)
            {
                // Eventuell noch mehr ausnahmen einbauen damit Häuser nicht zählen als andere Map
                string map = e.NewLocation.Name.Contains("Farm") ? "Farm" :
                                e.NewLocation.Name.Contains("Mine") ? "Mine" :
                                e.NewLocation.Name;
                currentMap = map;
                voiceClient.SendPacket((byte)VoiceClient.PacketType.Location, playerID, Encoding.UTF8.GetBytes(map));
            }
        }

    }

}