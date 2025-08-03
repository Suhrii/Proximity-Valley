using DiscordRPC;
using GenericModConfigMenu; // GMCM-API
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Menus;
using StardewValley.Network;
using System.Text;
using System.Text.RegularExpressions;

namespace Proximity_Valley;

public class ModEntry : Mod
{
    private VoiceClient voiceClient;
    DiscordRpcClient discordRpcClient;
    internal ModConfig Config;

    private string currentMap = "World";
    private bool isWalkieTalkie = false;

    internal long playerID = -1; // Unique ID for the player, initialized to -1
    internal bool isPushToTalking = false;
    internal bool isMuted = false;
    internal bool devOptionsEnabled = false; // Flag for enabling developer options

    private IModHelper Helper;

    public override void Entry(IModHelper helper)
    {
        Helper = helper;

        Monitor.Log($"[Voice] Boot", LogLevel.Debug);
        Config = helper.ReadConfig<ModConfig>();

        voiceClient = new(Monitor);
        voiceClient.modEntry = this; // Set the mod entry reference in the voice client
        voiceClient.Start();

        helper.Events.GameLoop.GameLaunched += OnGameLaunched;
        helper.Events.Player.Warped += OnWarped;
        helper.Events.Display.RenderedHud += OnRenderedHud;

        helper.Events.Input.ButtonPressed += Input_ButtonPressed;
        helper.Events.Input.ButtonReleased += Input_ButtonReleased;
        helper.Events.GameLoop.UpdateTicked += voiceClient.OnUpdateTicked;
        helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
        helper.Events.GameLoop.DayStarted += GameLoop_DayStarted;

        helper.Events.Display.RenderedActiveMenu += OnRenderedActiveMenu;


        discordRpcClient = new DiscordRpcClient("1401173401498030210", autoEvents: true);
        discordRpcClient.Initialize();

        discordRpcClient.SetPresence(new RichPresence()
        {
            //Details = "Level Games Launcher",
            State = "Main Menu",
            Assets = new Assets()
            {
                LargeImageKey = "stardew_logo",
                //LargeImageText = "",
                //SmallImageKey = "stardew_logo",
                //SmallImageText = ""
            },
            Timestamps = new Timestamps()
            {
                Start = DateTime.UtcNow
            }
        });
    }



    private static readonly int[] LevelXpRequirements = new int[]
    {
        0,      // Level 0
        100,    // Level 1
        380,    // Level 2
        770,    // Level 3
        1300,   // Level 4
        2150,   // Level 5
        3300,   // Level 6
        4800,   // Level 7
        6900,   // Level 8
        10000,  // Level 9
        15000   // Level 10
    };

    private int GetXpRequiredForLevel(int level)
    {
        if (level <= 0) return 0;
        if (level >= LevelXpRequirements.Length) return LevelXpRequirements[^1];
        return LevelXpRequirements[level];
    }


    private void OnRenderedActiveMenu(object? sender, RenderedActiveMenuEventArgs e)
    {
        if (Config.ShowSkillXP)
            ShowSkillsXP(e);
        if (Config.ShowRealtionshipPoints)
            ShowRelationshipProgress();
    }

    private void ShowRelationshipProgress ()
    {
        if (Game1.activeClickableMenu is not GameMenu menu)
            return;

        if (menu.pages[menu.currentTab] is not SocialPage socialPage)
            return;

        SpriteBatch b = Game1.spriteBatch;
        Farmer player = Game1.player;

        for (int i = 0; i < socialPage.SocialEntries.Count; i++)
        {
            if (socialPage.SocialEntries[i].Friendship == null) continue;
            string tooltip = $"{socialPage.SocialEntries[i].Friendship.Points} / {(socialPage.SocialEntries[i].HeartLevel + 1) * 250}";
            b.DrawString(Game1.smallFont, tooltip, new Vector2(socialPage.characterSlots[i].bounds.X + socialPage.characterSlots[i].bounds.Width / 3, socialPage.characterSlots[i].bounds.Y + 10), Color.Black);
        }
    }

    private void ShowSkillsXP (RenderedActiveMenuEventArgs e)
    {
        if (Game1.activeClickableMenu is not GameMenu menu)
            return;

        if (menu.pages[menu.currentTab] is not SkillsPage skillsPage)
            return;

        SpriteBatch b = e.SpriteBatch;
        Farmer player = Game1.player;

        for (int i = 0; i < skillsPage.skillAreas.Count; i++)
        {
            int xp = player.experiencePoints[i];
            int level = player.GetSkillLevel(i);
            int xpForNext = GetXpRequiredForLevel(level + 1);
            string tooltip = $"XP: {xp} / {xpForNext}";

            b.DrawString(Game1.smallFont, tooltip, new Vector2(skillsPage.skillAreas[i].bounds.X, skillsPage.skillAreas[i].bounds.Y + skillsPage.skillAreas[i].bounds.Height), Color.Black);
        }
    }

    private void GameLoop_DayStarted(object? sender, DayStartedEventArgs e)
    {
        UpdateDiscordRichPresence();
    }

    private void UpdateDiscordRichPresence()
    {
        discordRpcClient.UpdateState(Regex.Replace(Game1.player.currentLocation.Name, @"(\B[A-Z])", " $1"));
        discordRpcClient.UpdateDetails($"{Game1.season}, Day {Game1.dayOfMonth} - Year {Game1.year}");
        discordRpcClient.UpdateLargeAsset("stardew_logo",
            $"Farming: {Game1.player.FarmingLevel}\r\n" +
            $"Mining: {Game1.player.MiningLevel}\r\n" +
            $"Foraging: {Game1.player.ForagingLevel}\r\n" +
            $"Fishing: {Game1.player.FishingLevel}\r\n" +
            $"Combat: {Game1.player.CombatLevel}");
        switch (Game1.season)
        {
            case Season.Spring:
                discordRpcClient.UpdateSmallAsset("stardew_season_spring");
                break;
            case Season.Summer:
                discordRpcClient.UpdateSmallAsset("stardew_season_summer");
                break;
            case Season.Fall:
                discordRpcClient.UpdateSmallAsset("stardew_season_autumn");
                break;
            case Season.Winter:
                discordRpcClient.UpdateSmallAsset("stardew_season_winter");
                break;
            default:
                discordRpcClient.UpdateSmallAsset("stardew_logo");
                break;
        }
    }

    private void Input_ButtonReleased(object? sender, ButtonReleasedEventArgs e)
    {
        if (e.Button == Config.GlobalTalkButton)
        {
            isWalkieTalkie = false;
            voiceClient.SendPacket(VoiceClient.PacketType.Location, playerID, Encoding.UTF8.GetBytes(currentMap));
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
            voiceClient.SendPacket(VoiceClient.PacketType.Location, playerID, Encoding.UTF8.GetBytes("World"));
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
        SpriteBatch b = e.SpriteBatch;
        // Mic Volume/Talking Indicator

        int height = Math.Clamp((int)(200 * voiceClient.micVolumeLevel), 0, 100);

        b.Draw(Game1.staminaRect, new Rectangle(20, b.GraphicsDevice.Viewport.Height - 120, 20, 100), Color.White);
        b.Draw(Game1.staminaRect, new Rectangle(20, b.GraphicsDevice.Viewport.Height - 20 - height, 20, height), Color.LimeGreen);
        b.Draw(Game1.staminaRect, new Rectangle(20, b.GraphicsDevice.Viewport.Height - 20 - Math.Clamp((int)(Config.InputThreshold * 200), 0, 100), 20, 5), Color.DarkRed);

        if (isMuted)
            SpriteText.drawString(b, $"Muted", 80, b.GraphicsDevice.Viewport.Height - 80, drawBGScroll: 1);
        else if (Config.PushToTalk && !isPushToTalking)
            SpriteText.drawString(b, $"Muted (Press '{Config.PushToTalkButton}' to talk)", 80, b.GraphicsDevice.Viewport.Height - 80, drawBGScroll: 1);
        else
            SpriteText.drawString(b, $"Talking {(isWalkieTalkie ? "Global" : "")}", 80, b.GraphicsDevice.Viewport.Height - 80, drawBGScroll: 1);


        // Players Indicator
        int index = 0;
        foreach (Farmer? farmer in Game1.getOnlineFarmers())
        {
            if (farmer.currentLocation != Game1.player.currentLocation && !devOptionsEnabled)
                continue;

            // Positionsvergleich
            string arrow = "";
            if (farmer != Game1.player)
            {
                Vector2 myPos = Game1.player.Position;
                Vector2 otherPos = farmer.Position;
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

                if (Math.Abs(dx) + Math.Abs(dy) < Game1.tileSize)
                    arrow = "$";
            }

            string devExtraPrefix = devOptionsEnabled ? $"{farmer.Position} " : "";
            string devExtraSuffix = devOptionsEnabled ? $" - {farmer.currentLocation.Name}" : "";

            SpriteText.drawString(b, $"{devExtraPrefix}{farmer.displayName} {arrow}{devExtraSuffix}", 50, b.GraphicsDevice.Viewport.Height - 200 - (index * 50), drawBGScroll: 0);


            if (Config.ShowPlayerNames)
            {
                Vector2 worldPos = farmer.Position + new Vector2(32, -Game1.tileSize * 2.5f); // Mitte + über Kopf
                Vector2 screenPos = Game1.GlobalToLocal(worldPos); // korrekt: überladene Methode

                SpriteText.drawString(Game1.spriteBatch, farmer.displayName, (int)screenPos.X - SpriteText.getWidthOfString(farmer.displayName) / 2, (int)screenPos.Y, drawBGScroll: 1);
            }


            #region Per Player Volume Slider 

            int sliderX = 400;
            int sliderY = b.GraphicsDevice.Viewport.Height - 190 - (index * 50);
            int sliderWidth = 100;
            int sliderHeight = 10;

            // Volume holen oder Default setzen
            if (!voiceClient.playerAudioStreams.TryGetValue(farmer.UniqueMultiplayerID, out PlayerAudioStream stream))
                continue;

            Color barColor = Color.SkyBlue;
            Color handleColor = Color.White;

            // Hintergrund des Sliders
            b.Draw(Game1.staminaRect, new Rectangle(sliderX, sliderY, sliderWidth, sliderHeight), Color.Gray);
            // Gefüllter Bereich
            b.Draw(Game1.staminaRect, new Rectangle(sliderX, sliderY, (int)(sliderWidth * stream.VolumeProvider.Volume), sliderHeight), barColor);
            // "Griff" zeichnen
            b.Draw(Game1.staminaRect, new Rectangle(sliderX + (int)(sliderWidth * stream.VolumeProvider.Volume) - 2, sliderY - 2, 4, sliderHeight + 4), handleColor);

            #endregion


            index++;
        }

    }

    public void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady || !Game1.game1.IsActive)
            return;

        if (Mouse.GetState().LeftButton == ButtonState.Pressed)
        {
            int mouseX = Game1.getMouseX();
            int mouseY = Game1.getMouseY();

            int i = 0;
            foreach (var farmer in Game1.getOnlineFarmers())
            {
                if (farmer.currentLocation != Game1.player.currentLocation)
                    continue;

                int sliderX = 400;
                int sliderY = Game1.viewport.Height - 190 - (i * 50);
                int sliderWidth = 100;
                int sliderHeight = 10;

                Rectangle sliderRect = new Rectangle(sliderX, sliderY, sliderWidth, sliderHeight);
                if (sliderRect.Contains(mouseX, mouseY))
                {
                    float newVolume = Math.Clamp((mouseX - sliderX) / (float)sliderWidth, 0f, 1f);
                    voiceClient.playerAudioStreams[farmer.UniqueMultiplayerID].VolumeProvider.Volume = newVolume;
                    break;
                }

                i++;
            }
        }
    }



    private void OnGameLaunched(object sender, EventArgs e)
    {
        Monitor.Log($"[Voice] Startet", LogLevel.Debug);

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                voiceClient.SendPacket(VoiceClient.PacketType.Disconnect, playerID, Array.Empty<byte>());
            }
            catch
            {
                // Ignoriere Fehler beim Shutdown
            }
        };


        playerID = Game1.player.UniqueMultiplayerID;


        // GMCM - Integration
        IGenericModConfigMenuApi? gmcm = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
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
            gmcm.AddKeybind(
                mod: this.ModManifest,
                name: () => "Toggle Dev Options",
                tooltip: () => "Toggle some various Dev Options on/off (mostly for debugging)",
                getValue: () => Config.ToggleDevOptions,
                setValue: value => Config.ToggleDevOptions = value
            );

            gmcm.AddBoolOption(
                mod: this.ModManifest,
                name: () => "Hear Self",
                tooltip: () => "Höre das eigene Mikrofon",
                getValue: () => Config.HearSelf,
                setValue: value => Config.HearSelf = value
            );


            gmcm.AddBoolOption(
                mod: this.ModManifest,
                name: () => "Show Player Names",
                tooltip: () => "Show Players Name above their head",
                getValue: () => Config.ShowPlayerNames,
                setValue: value => Config.ShowPlayerNames = value
            );
            gmcm.AddBoolOption(
                mod: this.ModManifest,
                name: () => "Show Skill XP",
                tooltip: () => "Show the XP in the Skill Menu",
                getValue: () => Config.ShowSkillXP,
                setValue: value => Config.ShowSkillXP = value
            );
            gmcm.AddBoolOption(
                mod: this.ModManifest,
                name: () => "Show Relationship Points",
                tooltip: () => "Show the Points in the Relationship Menu",
                getValue: () => Config.ShowRealtionshipPoints,
                setValue: value => Config.ShowRealtionshipPoints = value
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
            UpdateDiscordRichPresence();
            voiceClient.SendPacket(VoiceClient.PacketType.Location, playerID, Encoding.UTF8.GetBytes(map));
        }
    }

    public Farmer? GetFarmerByID(long id)
    {
        return Game1.getOnlineFarmers().FirstOrDefault(p => p?.UniqueMultiplayerID == id);
    }

}