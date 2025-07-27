# Proximity Valley

Proximity Valley ist ein **Proximity Voice Chat** für Stardew Valley. Der Mod ermöglicht Sprachkommunikation basierend auf der Position im Spiel und nutzt einen separaten Voice-Server für die Datenübertragung.

## Funktionen

- Sprachchat nur mit Spielern, die sich auf derselben Karte befinden
- Walkie-Talkie-Modus für globales Sprechen
- Option für Push-to-Talk oder Spracherkennung 
- Anpassbare Audioeinstellungen (Abtastrate, Lautstärke, Kanäle u.v.m.)
- AES-Verschlüsselung für alle UDP-Pakete
- Integrierte Unterstützung für das **Generic Mod Config Menu**

## Installation (Client)

1. [SMAPI](https://smapi.io/) installieren.
2. Den Ordner **Proximity Valley Client** in das `Mods`-Verzeichnis von Stardew Valley kopieren.
3. Spiel starten und im Konfigurationsmenü die Serverdaten einstellen (Adresse, Port usw.).

## Installation (Server)

Der Server wird als eigenständige .NET-Anwendung geliefert und leitet die Audiodaten zwischen den Clients weiter.

1. [.NET SDK](https://dotnet.microsoft.com/) installieren (Version 6 oder neuer).
2. Im Ordner **Proximity Valley Server** folgendes ausführen:
   ```bash
   dotnet run --project "Proximity Valley Server"
   ```
3. Optional kann in `config.json` der Port und die Verschlüsselung angepasst werden.

## Konfiguration

Alle Einstellungen können über das Generic Mod Config Menu im Spiel oder direkt in `config.json` geändert werden. Die wichtigsten Optionen:

| Einstellung             | Beschreibung                              |
|-------------------------|-------------------------------------------|
| `ServerAddress`         | Adresse des Voice-Servers                 |
| `ServerPort`            | UDP-Port des Voice-Servers                |
| `LocalPort`             | Lokaler UDP-Port des Clients              |
| `SampleRate`            | Abtastrate in Hertz                       |
| `Bits`                  | Bits pro Sample                            |
| `Channels`              | Anzahl der Audiokanäle                   |
| `InputVolume`           | Verstärkung des Mikrofons (0–10)           |
| `OutputVolume`          | Lautstärke der Wiedergabe (0–10)          |
| `InputThreshold`        | Schwelle für Spracherkennung (0.0–1.0)      |
| `PushToTalk`            | Push-to-Talk aktivieren                   |
| `PushToTalkButton`      | Taste für Push-to-Talk                    |
| `GlobalTalkButton`      | Taste für globales Sprechen               |
| `ToggleMute`            | Taste zum Stummschalten                   |

## Entwicklung

Zum Kompilieren des Mods und des Servers einfach die beiden Projekte mit `dotnet build` bauen oder die zugehörige Solution öffnen.

## Lizenz

In diesem Repository ist keine Lizenzdatei enthalten.
