using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Proximity_Valley
{
    internal class Spectating
    {

        // constructor and event subscriptions
        public Spectating(IModHelper helper)
        {
            // Subscribe to the UpdateTicked event
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked_Spectate;
            // Subscribe to the ButtonPressed event
            helper.Events.Input.ButtonPressed += OnButtonPressed_Spectate;
        }


        private int currentIndex = 0;
        private List<Character> targets = new List<Character>();
        private Character currentTarget;
        private bool isSpectating = false; // Toggle für Spectator Mode
        private void OnUpdateTicked_Spectate(object sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady || !isSpectating)
                return;

            // Liste der Ziele erstellen: alle anderen Spieler + NPCs auf allen Locations
            targets.Clear();

            foreach (var p in Game1.getOnlineFarmers())
            {
                if (p != Game1.player)
                    targets.Add(p);
            }

            foreach (var location in Game1.locations)
            {
                foreach (var character in location.characters)
                {
                    targets.Add(character);
                }
            }

            // Falls noch kein Ziel ausgewählt, erstes nehmen
            if (currentTarget == null && targets.Count > 0)
            {
                currentIndex = 0;
                currentTarget = targets[0];
            }

            // Kamera auf das aktuelle Ziel setzen
            if (currentTarget != null)
            {
                SetCameraToTarget(currentTarget);
            }

            // Eigenen Farmer unsichtbar/frozen machen
            Game1.player.freezePause = 0;
            Game1.player.CanMove = false;
        }

        private void OnButtonPressed_Spectate(object sender, ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady)
                return;

            // Spectate Mode Toggle (z.B. Enter-Taste)
            if (e.Button == SButton.Enter)
            {
                isSpectating = !isSpectating;

                if (!isSpectating)
                {
                    // Spectator Mode ausschalten: Kamera zurücksetzen und Spieler sichtbar machen
                    Game1.player.freezePause = 0;
                    Game1.player.CanMove = true;
                    Game1.currentLocation = Game1.player.currentLocation;
                }
            }

            if (!isSpectating)
                return; // nur Tasteneingaben bearbeiten, wenn Spectate Mode aktiv

            // Ziel wechseln: Pfeiltasten
            if (e.Button == SButton.Right)
            {
                if (targets.Count == 0) return;
                currentIndex = (currentIndex + 1) % targets.Count;
                currentTarget = targets[currentIndex];
            }

            if (e.Button == SButton.Left)
            {
                if (targets.Count == 0) return;
                currentIndex--;
                if (currentIndex < 0) currentIndex = targets.Count - 1;
                currentTarget = targets[currentIndex];
            }
        }

        private void SetCameraToTarget(Character target)
        {
            // Kamera-Location auf Ziel-Location setzen
            if (Game1.currentLocation != target.currentLocation)
                Game1.currentLocation = target.currentLocation;

            // Kamera zentrieren
            Game1.viewport.X = (int)(target.Position.X + target.GetBoundingBox().Width / 2 - Game1.viewport.Width / 2);
            Game1.viewport.Y = (int)(target.Position.Y + target.GetBoundingBox().Height / 2 - Game1.viewport.Height / 2);
        }
    }
}
