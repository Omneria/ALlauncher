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

    /// <summary>Réglages du pack publiés par le VPS (v1.12.0, section "pack", tirée de pack.json
    /// par generate-manifest.py). Absent sur un manifest plus ancien.</summary>
    [JsonPropertyName("pack")]
    public ModpackPackSettings? Pack { get; set; }
}

/// <summary>
/// Réglages imposés par le pack plutôt que codés en dur dans le launcher (v1.12.0) : changer de
/// version de Forge sur le serveur, ou le besoin en RAM du pack, ne demande plus de release.
/// Chaque champ est optionnel ; absent, le launcher garde sa propre valeur.
/// </summary>
public sealed class ModpackPackSettings
{
    [JsonPropertyName("forgeVersion")]
    public string? ForgeVersion { get; set; }

    [JsonPropertyName("recommendedRamMb")]
    public int? RecommendedRamMb { get; set; }

    [JsonPropertyName("minRamMb")]
    public int? MinRamMb { get; set; }
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

    /// <summary>
    /// "default" (v1.12.0) : réglage par défaut, installé seulement s'il manque — un joueur garde
    /// ce qu'il y a changé (JourneyMap, Quark, JEI...). Absent : fichier imposé, réécrit dès qu'il
    /// diffère du manifest (tous les mods, et les configs qui doivent être identiques partout).
    /// </summary>
    [JsonPropertyName("mode")]
    public string? Mode { get; set; }

    [JsonIgnore]
    public bool IsDefaultOnly => string.Equals(Mode, "default", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Une mise à jour du pack, telle que décrite dans changelog.json (v1.12.0) : lignes déjà
/// rédigées pour les joueurs par generate-manifest.py ("Ajouté : ...", "Mis à jour : ...").</summary>
public sealed class ModpackChangelogEntry
{
    [JsonPropertyName("date")]
    public DateTimeOffset Date { get; set; }

    [JsonPropertyName("lines")]
    public List<string> Lines { get; set; } = [];
}
