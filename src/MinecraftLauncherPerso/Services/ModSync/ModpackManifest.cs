using System.Text.Json.Serialization;

namespace MinecraftLauncherPerso.Services.ModSync;

/// <summary>
/// Manifest par fichier (mapping "chemin relatif" -> URL + hash), en alternative au zip unique
/// (voir ModSyncService.SyncAsync). Permet des mises à jour incrémentales : seuls les fichiers dont
/// le hash a changé sont retéléchargés, au lieu de tout le pack à chaque mise à jour. Optionnel :
/// si LauncherSettings.ModpackManifestUrl n'est pas configuré, le launcher reste sur l'ancien flux
/// zip unique (ModpackZipUrl), backward-compatible avec un VPS qui n'a pas encore ce manifest.
/// </summary>
public sealed class ModpackManifest
{
    [JsonPropertyName("files")]
    public Dictionary<string, ModpackManifestFile> Files { get; set; } = new();
}

public sealed class ModpackManifestFile
{
    /// <summary>URL absolue de téléchargement de ce fichier (pas relative au manifest : le VPS peut
    /// héberger les fichiers ailleurs, ex. un CDN, sans devoir réécrire le manifest à chaque déplacement).</summary>
    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = "";

    /// <summary>Optionnel, utilisé uniquement pour dimensionner la barre de progression globale ;
    /// un fichier sans taille connue est simplement ignoré dans ce calcul.</summary>
    [JsonPropertyName("size")]
    public long? Size { get; set; }
}
