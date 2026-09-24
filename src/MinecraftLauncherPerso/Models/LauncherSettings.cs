using System.IO;
using System.Text;
using MinecraftLauncherPerso.Services.Hardware;

namespace MinecraftLauncherPerso.Models;

/// <summary>Préférences utilisateur persistées entre deux lancements (settings.json).</summary>
public sealed class LauncherSettings
{
    public string MinecraftVersion { get; set; } = "1.20.1";

    public string ForgeVersion { get; set; } = "47.3.0";

    /// <summary>
    /// URL directe de l'archive .zip du modpack (mods/ + config/ à sa racine) hébergée sur le VPS.
    /// Valeur par défaut encodée en base64 (voir <see cref="DecodeDefaultModpackUrl"/>) : le dépôt
    /// étant public, ça évite que l'IP du VPS apparaisse en clair dans le code source ou soit
    /// indexée telle quelle par un moteur de recherche/scanner, sans empêcher le launcher de
    /// fonctionner "out of the box". Peut être écrasée dans settings.json si le VPS change.
    /// </summary>
    public string ModpackZipUrl { get; set; } = DecodeDefaultModpackUrl();

    private static string DecodeDefaultModpackUrl() =>
        Encoding.UTF8.GetString(Convert.FromBase64String(
            "aHR0cDovLzE4NS4xODUuODIuMTgwL21vZHBhY2svQWxnYXJvbi1tb2RkZWQuemlw"));

    /// <summary>
    /// URL du manifest par fichier (JSON, voir ModpackManifest.cs), en alternative à
    /// <see cref="ModpackZipUrl"/> : permet des mises à jour incrémentales (ne retélécharger que
    /// les fichiers qui ont changé) au lieu de tout le zip à chaque mise à jour du pack. Pointe par
    /// défaut vers le manifest 1.20.1 réellement publié sur le VPS (voir scripts/vps/) — encodé en
    /// base64 pour la même raison que <see cref="ModpackZipUrl"/> (éviter l'IP en clair dans le
    /// dépôt public). Peut être vidée dans settings.json pour revenir au flux zip unique.
    /// </summary>
    public string ModpackManifestUrl { get; set; } = DecodeDefaultModpackManifestUrl();

    private static string DecodeDefaultModpackManifestUrl() =>
        Encoding.UTF8.GetString(Convert.FromBase64String(
            "aHR0cDovLzE4NS4xODUuODIuMTgwL21vZHBhY2svbWFuaWZlc3QuanNvbg=="));

    /// <summary>
    /// URL d'un fichier texte optionnel (maintenance.txt sur le VPS) : son contenu, s'il existe et
    /// n'est pas vide, s'affiche en bandeau sur le dashboard. Vide par défaut = bannière jamais
    /// affichée, comportement identique à un VPS qui ne sert pas ce fichier.
    /// </summary>
    public string MaintenanceMessageUrl { get; set; } = "";

    /// <summary>
    /// "Application (client) ID" de l'app Azure AD enregistrée pour ce launcher (identifie
    /// l'application, pas les comptes joueurs — un seul ID est partagé par tout le groupe).
    /// Nécessaire pour l'authentification Microsoft/Xbox Live directe.
    /// </summary>
    public string MicrosoftClientId { get; set; } = "88cfb0e1-8c0a-46e4-abf9-9bf73b40eaf7";

    /// <summary>
    /// Nom affiché dans l'écran multijoueur pour l'unique serveur de la liste (voir <see
    /// cref="ServerHost"/>).
    /// </summary>
    public string ServerName { get; set; } = "Astral Nexus";

    /// <summary>
    /// Adresse (IP ou nom d'hôte) du serveur Minecraft Astral Nexus. Le launcher y rejoint
    /// directement le joueur au lancement (arguments --server/--port, comme un "quick play") et
    /// réécrit servers.dat à chaque démarrage pour qu'il n'y ait que ce serveur dans la liste
    /// multijoueur — un joueur qui en ajouterait un autre manuellement le retrouve retiré au
    /// lancement suivant.
    /// Nom de domaine DuckDNS plutôt que l'IP brute (qui est aussi celle du VPN) : contrairement
    /// à <see cref="ModpackZipUrl"/>, pas besoin d'encodage base64 ici, un nom de domaine n'a rien
    /// à cacher en lui-même. Peut être écrasée dans settings.json (si le serveur change d'adresse).
    /// </summary>
    public string ServerHost { get; set; } = "astralnexusmc.duckdns.org";

    public int ServerPort { get; set; } = 25565;

    public int MinRamMb { get; set; } = 2048;

    /// <summary>
    /// Valeur par défaut calculée à partir de la RAM totale de la machine (voir <see
    /// cref="RecommendMaxRamMb"/>) plutôt qu'une valeur fixe identique pour tout le monde — un
    /// joueur avec 8 Go de RAM système n'a pas les mêmes marges qu'un joueur avec 32 Go. Reste
    /// librement modifiable ensuite dans les Paramètres ou settings.json.
    /// </summary>
    public int MaxRamMb { get; set; } = RecommendMaxRamMb();

    private static int RecommendMaxRamMb()
    {
        var totalMb = SystemInfo.GetTotalPhysicalMemoryMb();
        if (totalMb is null)
        {
            // RAM système indéterminable (API Windows indisponible) : repli sur l'ancienne valeur fixe.
            return 6144;
        }

        var recommended = totalMb.Value switch
        {
            < 8192 => 3072,
            < 12288 => 4096,
            < 16384 => 6144,
            _ => 8192,
        };

        // Jamais plus de la moitié de la RAM totale, pour laisser de la marge à l'OS et au reste.
        return Math.Min(recommended, totalMb.Value / 2);
    }

    /// <summary>
    /// Dossier .minecraft utilisé par CE launcher pour le pack modé — volontairement distinct du
    /// .minecraft du launcher officiel (qui, lui, n'est utilisé que pour lire la session active).
    /// </summary>
    public string GameDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MinecraftLauncherPerso", "game");

    /// <summary>
    /// Résolution de la fenêtre du jeu (MLaunchOption.ScreenWidth/Height). 0 = valeur par défaut
    /// de Minecraft (pas d'argument --width/--height passé, laisse le jeu décider).
    /// </summary>
    public int ScreenWidth { get; set; } = 0;

    public int ScreenHeight { get; set; } = 0;

    /// <summary>
    /// Taille de la fenêtre du launcher (pas celle du jeu, voir <see cref="ScreenWidth"/> ci-dessus)
    /// mémorisée entre deux lancements depuis que la fenêtre est redimensionnable (v1.8.0,
    /// WindowChrome) — sans ça, elle revenait systématiquement à sa taille par défaut au redémarrage
    /// malgré un redimensionnement manuel.
    /// </summary>
    public double LauncherWindowWidth { get; set; } = 1240;

    public double LauncherWindowHeight { get; set; } = 600;

    /// <summary>Mode compact (v1.8.0, bouton "⤢" de la barre de titre) mémorisé entre deux lancements.</summary>
    public bool IsCompactMode { get; set; }

    /// <summary>
    /// Autorise ou non DesktopNotificationService (bulle Windows native) à s'afficher, ex. quand le
    /// serveur repasse en ligne — activé par défaut, mais certains joueurs préfèrent un launcher
    /// entièrement silencieux.
    /// </summary>
    public bool DesktopNotificationsEnabled { get; set; } = true;
}
