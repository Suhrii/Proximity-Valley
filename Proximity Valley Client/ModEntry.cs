using GenericModConfigMenu; // GMCM-API
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.BellsAndWhistles;
using System;
using System.Text;

namespace ProximityValley
{
    public class ModEntry : Mod
    {
        private VoiceClient voiceClient;
        private ModConfig Config;

        private string currentMap = "World";
        private bool isWalkieTalkie = false;

        internal int playerID = -1; // Unique ID for the player, initialized to -1
        internal bool isPushToTalking = false;
        internal bool isMuted = false;

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
            helper.Events.GameLoop.ReturnedToTitle += GameLoop_ReturnedToTitle;

            helper.Events.Input.ButtonPressed += Input_ButtonPressed;
            helper.Events.Input.ButtonReleased += Input_ButtonReleased;

        }

        private void GameLoop_ReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
        {
            voiceClient.SendPacket((byte)VoiceClient.PacketType.Disconnect, playerID, Array.Empty<byte>());
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
        }

        private void OnRenderedHud(object sender, RenderedHudEventArgs e)
        {
            var b = e.SpriteBatch;
            // Mic Volume/Talking Indicator
            if (voiceClient.micVolumeLevel > Config.InputThreshold)
            {
                int height = (int)(100 * voiceClient.micVolumeLevel);
                b.Draw(Game1.staminaRect, new Rectangle(20, b.GraphicsDevice.Viewport.Height - 20 - height, 20, height), Color.LimeGreen);
                if (!isMuted)
                    SpriteText.drawString(b, $"Talking {(isWalkieTalkie ? "Global" : "")}", 80, b.GraphicsDevice.Viewport.Height - 80);
                else
                    SpriteText.drawString(b, $"Muted", 80, b.GraphicsDevice.Viewport.Height - 80);
            }

            // Players Indicator
            int index = 0;
            foreach (var farmer in Game1.getOnlineFarmers())
            {
                if (farmer.currentLocation != Game1.player.currentLocation)
                    continue;

                SpriteText.drawString(b, $"{farmer.displayName}", 50, b.GraphicsDevice.Viewport.Height - 200 - (index * 50));

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


            playerID = Game1.player.UniqueMultiplayerID.GetHashCode();


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
            }

        }

        private void OnWarped(object sender, WarpedEventArgs e)
        {
            if (e.IsLocalPlayer)
            {
                string map = e.NewLocation.Name.Contains("Farm") ? "Farm" :
                                e.NewLocation.Name.Contains("Mine") ? "Mine" :
                                e.NewLocation.Name;
                currentMap = map;
                voiceClient.SendPacket((byte)VoiceClient.PacketType.Location, playerID, Encoding.UTF8.GetBytes(map));
            }
        }

    }

}