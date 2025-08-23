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
        private Texture2D npcIcon;

        public WorldMapExtension(IModHelper helper)
        {
            helper.Events.Display.RenderedHud += OnRenderedHud;
            npcIcon = new Texture2D(Game1.graphics.GraphicsDevice, 1, 1);
            npcIcon.SetData(new[] { Color.Red }); // Einfach ein rotes Pixel als Icon
        }

        private void OnRenderedHud(object sender, RenderedHudEventArgs e)
        {
            if (!Context.IsWorldReady)
                return;

            // Prüfen, ob die World Map gerade offen ist
            if (Game1.activeClickableMenu is GameMenu menu && menu.currentTab == GameMenu.mapTab)
            {
                SpriteBatch spriteBatch = e.SpriteBatch;
                DrawNPCsOnMap(spriteBatch, menu);
            }
        }

        private void DrawNPCsOnMap(SpriteBatch spriteBatch, GameMenu menu)
        {
            // Map-Tab Bereich approximieren
            int mapX = menu.xPositionOnScreen + 32;
            int mapY = menu.yPositionOnScreen + 64;
            int mapWidth = menu.width - 64;
            int mapHeight = menu.height - 128;

            foreach (var location in Game1.locations)
            {
                foreach (var npc in location.characters)
                {
                    Vector2 miniMapPos = GetMiniMapPosition(npc.Position, mapX, mapY, mapWidth, mapHeight, location);
                    ModEntry.Instance.Monitor.Log($"NPC {npc.Name} at {npc.Position} mapped to minimap position {miniMapPos}", LogLevel.Info);

                    // Icon zeichnen
                    spriteBatch.Draw(npcIcon, new Rectangle((int)miniMapPos.X, (int)miniMapPos.Y, 40, 40), Color.Red);
                    spriteBatch.DrawString(
                        Game1.dialogueFont,
                        npc.Name,
                        new Vector2(miniMapPos.X + 5, miniMapPos.Y + 5),
                        Color.White
                    );
                }
            }
        }

        private Vector2 GetMiniMapPosition(Vector2 npcPosition, int mapX, int mapY, int mapWidth, int mapHeight, GameLocation location)
        {
            // Sehr einfache Skalierung: Karte ist 256x256 Tiles
            float scaleX = mapWidth / 256f;
            float scaleY = mapHeight / 256f;

            Vector2 miniMapPos = new Vector2(
                mapX + npcPosition.X * scaleX / Game1.tileSize,
                mapY + npcPosition.Y * scaleY / Game1.tileSize
            );

            return miniMapPos;
        }
    }
}
