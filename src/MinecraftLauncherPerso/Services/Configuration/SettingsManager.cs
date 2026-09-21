using System.IO;
using System.Text.Json;
using MinecraftLauncherPerso.Models;
using MinecraftLauncherPerso.Services.Diagnostics;

namespace MinecraftLauncherPerso.Services.Configuration;

/// <summary>Charge/sauvegarde les préférences utilisateur (RAM, dossier de jeu, URL du VPS, ...)
/// dans %AppData%/MinecraftLauncherPerso/settings.json.</summary>
public sealed class SettingsManager
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _settingsFilePath;

    public SettingsManager(string? settingsFilePath = null)
    {
        _settingsFilePath = settingsFilePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MinecraftLauncherPerso", "settings.json");
    }

    /// <summary>
    /// Anciennes valeurs de <see cref="LauncherSettings.ServerHost"/> déjà écrites dans le
    /// settings.json de certains joueurs avant correction du code : une valeur par défaut fautive,
    /// une fois persistée sur une machine, n'est plus jamais remplacée par la nouvelle valeur par
    /// défaut au chargement (Load() ne fait que lire le fichier existant) — sans cette migration,
    /// corriger le typo dans le code ne corrige rien pour qui a déjà lancé le launcher une fois.
    /// </summary>
    private static readonly Dictionary<string, string> ServerHostMigrations = new()
    {
        ["astranexusmc.duckdns.org"] = "astralnexusmc.duckdns.org",
    };

    public LauncherSettings Load()
    {
        if (!File.Exists(_settingsFilePath))
        {
            return new LauncherSettings();
        }

        LauncherSettings settings;
        try
        {
            var json = File.ReadAllText(_settingsFilePath);
            settings = JsonSerializer.Deserialize<LauncherSettings>(json) ?? new LauncherSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // settings.json corrompu (JSON tronqué par une coupure en pleine écriture, fichier
            // vide...) : auparavant, JsonException remontait telle quelle jusqu'au constructeur de
            // MainWindow et plantait tout le launcher au démarrage, sans recours autre que
            // supprimer le fichier à la main. On repart des valeurs par défaut, en gardant le
            // fichier fautif à côté (plutôt que de l'écraser silencieusement) pour pouvoir
            // diagnostiquer si ça se reproduit.
            Logger.Error("SettingsManager", $"settings.json illisible, retour aux valeurs par défaut ({_settingsFilePath}).", ex);
            TryBackupCorruptFile();
            settings = new LauncherSettings();
        }

        if (ServerHostMigrations.TryGetValue(settings.ServerHost, out var correctedServerHost))
        {
            settings.ServerHost = correctedServerHost;
        }

        var normalized = Normalize(settings);
        Save(normalized);
        return normalized;
    }

    /// <summary>
    /// Filet de sécurité contre un settings.json édité à la main (ou corrompu partiellement, sans
    /// lever d'exception JSON) avec des valeurs absurdes : RAM négative/nulle, min > max, port hors
    /// plage... Auparavant rien ne revalidait ces champs après désérialisation, l'erreur ne se
    /// révélait qu'au lancement du jeu via un message JVM peu clair.
    /// </summary>
    private static LauncherSettings Normalize(LauncherSettings settings)
    {
        if (settings.MinRamMb <= 0)
        {
            settings.MinRamMb = new LauncherSettings().MinRamMb;
        }

        if (settings.MaxRamMb <= 0)
        {
            settings.MaxRamMb = new LauncherSettings().MaxRamMb;
        }

        if (settings.MinRamMb > settings.MaxRamMb)
        {
            (settings.MinRamMb, settings.MaxRamMb) = (settings.MaxRamMb, settings.MinRamMb);
        }

        if (settings.ScreenWidth < 0)
        {
            settings.ScreenWidth = 0;
        }

        if (settings.ScreenHeight < 0)
        {
            settings.ScreenHeight = 0;
        }

        if (settings.ServerPort is <= 0 or > 65535)
        {
            settings.ServerPort = new LauncherSettings().ServerPort;
        }

        return settings;
    }

    private void TryBackupCorruptFile()
    {
        try
        {
            var backupPath = $"{_settingsFilePath}.corrupt-{DateTimeOffset.Now:yyyyMMddHHmmss}";
            File.Move(_settingsFilePath, backupPath, overwrite: true);
        }
        catch (Exception)
        {
            // Best-effort : si même la sauvegarde échoue, on continue quand même avec les valeurs
            // par défaut plutôt que de bloquer le démarrage pour ça.
        }
    }

    public void Save(LauncherSettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, JsonOptions);

        // Écriture atomique : sur un fichier temporaire puis File.Move (remplacement atomique côté
        // OS), plutôt qu'un WriteAllText direct sur le fichier final. Sans ça, un crash/coupure de
        // courant pendant l'écriture laissait un settings.json tronqué, qui plantait le launcher au
        // prochain démarrage (avant la récupération ajoutée dans Load() ci-dessus) — l'écriture
        // atomique évite carrément d'arriver dans cet état.
        var tempPath = $"{_settingsFilePath}.tmp-{Guid.NewGuid():N}";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _settingsFilePath, overwrite: true);
    }
}
