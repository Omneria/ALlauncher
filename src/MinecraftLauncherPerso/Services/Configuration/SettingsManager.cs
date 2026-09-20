using System.IO;
using System.Text.Json;
using MinecraftLauncherPerso.Models;

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

        var json = File.ReadAllText(_settingsFilePath);
        var settings = JsonSerializer.Deserialize<LauncherSettings>(json) ?? new LauncherSettings();

        if (ServerHostMigrations.TryGetValue(settings.ServerHost, out var correctedServerHost))
        {
            settings.ServerHost = correctedServerHost;
            Save(settings);
        }

        return settings;
    }

    public void Save(LauncherSettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_settingsFilePath, json);
    }
}
