using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace Proximity_Valley
{
    internal class WorldMapExtension
    {
        private readonly Texture2D npcIcon;
        private readonly IMonitor Monitor;

        public WorldMapExtension(IModHelper helper, IMonitor monitor)
        {
            this.Monitor = monitor;

            // Wichtig: Für Menüs RenderedActiveMenu statt RenderedHud
            helper.Events.Display.RenderedActiveMenu += OnRenderedActiveMenu;

            // Einfaches 1x1 rotes Pixel als Icon
            npcIcon = new Texture2D(Game1.graphics.GraphicsDevice, 1, 1);
            npcIcon.SetData(new[] { Color.Red });
        }

        private void OnRenderedActiveMenu(object sender, RenderedActiveMenuEventArgs e)
        {
            if (!Context.IsWorldReady)
                return;

            if (Game1.activeClickableMenu is GameMenu menu && menu.currentTab == GameMenu.mapTab)
            {
                DrawNPCsOnMap(e.SpriteBatch, menu);
            }
        }
        private Vector2 GetMiniMapPosition(NPC npc, int mapX, int mapY, int mapWidth, int mapHeight)
        {
            string loc = npc.currentLocation?.Name ?? "";

            // feste Koordinaten für bestimmte Locations (Pixel auf der Map-Grafik)
            Dictionary<string, Vector2> locationMap = new Dictionary<string, Vector2>
            {
                ["FarmHouse"] = new Vector2(175, 270),
                ["Farm"] = new Vector2(175, 270),
                ["Town"] = new Vector2(420, 300),
                ["Saloon"] = new Vector2(420, 300),
                ["ManorHouse"] = new Vector2(460, 360),
                ["SeedShop"] = new Vector2(400, 280),
                ["Hospital"] = new Vector2(440, 260),
                ["Mountain"] = new Vector2(650, 140),
                ["Mine"] = new Vector2(670, 120),
                ["Beach"] = new Vector2(420, 500),
                ["Forest"] = new Vector2(300, 450),
            };

            // Fallback: Mitte der Map
            Vector2 basePos = locationMap.ContainsKey(loc) ? locationMap[loc] : new Vector2(400, 300);

            // Skalierung: map.png ist ca. 800x600 Pixel groß
            float scaleX = mapWidth / 800f;
            float scaleY = mapHeight / 600f;

            // Tiles in Pixel umrechnen und addieren
            Vector2 offset = new Vector2(
                npc.Position.X / Game1.tileSize * 4, // Faktor 4 px pro Tile (grobe Annäherung)
                npc.Position.Y / Game1.tileSize * 4
            );

            return new Vector2(
                mapX + (basePos.X + offset.X) * scaleX,
                mapY + (basePos.Y + offset.Y) * scaleY
            );
        }


        private void DrawNPCsOnMap(SpriteBatch spriteBatch, GameMenu menu)
        {
            // Bereich der Map im Menü (ungefähr)
            int mapX = menu.xPositionOnScreen + 32;
            int mapY = menu.yPositionOnScreen + 64;
            int mapWidth = menu.width - 64;
            int mapHeight = menu.height - 128;

            // Test: Fester Punkt in der Mitte der Map
            spriteBatch.Draw(npcIcon, new Rectangle(mapX + mapWidth / 2, mapY + mapHeight / 2, 20, 20), Color.Red);

            // Jetzt alle NPCs durchgehen
            foreach (var location in Game1.locations)
            {
                foreach (var npc in location.characters)
                {
                    //Vector2 miniMapPos = GetMiniMapPosition(npc.Position, mapX, mapY, mapWidth, mapHeight); 
                    Vector2 miniMapPos = GetMiniMapPosition(npc, mapX, mapY, mapWidth, mapHeight);
                    this.Monitor.Log($"NPC {npc.Name} at {npc.Position} mapped to minimap position {miniMapPos}", LogLevel.Trace);

                    // Icon zeichnen
                    spriteBatch.Draw(npcIcon, new Rectangle((int)miniMapPos.X, (int)miniMapPos.Y, 16, 16), Color.Red);

                    // Namen drüber schreiben
                    spriteBatch.DrawString(
                        Game1.smallFont,
                        npc.Name,
                        new Vector2(miniMapPos.X, miniMapPos.Y - 20),
                        Color.White
                    );
                }
            }
        }

        private Vector2 GetMiniMapPosition(Vector2 npcPosition, int mapX, int mapY, int mapWidth, int mapHeight)
        {
            // Sehr einfache Skalierung: Karte ist ca. 256x256 Tiles groß
            float scaleX = mapWidth / 256f;
            float scaleY = mapHeight / 256f;

            return new Vector2(
                mapX + npcPosition.X * scaleX / Game1.tileSize,
                mapY + npcPosition.Y * scaleY / Game1.tileSize
            );
        }
    }
}
