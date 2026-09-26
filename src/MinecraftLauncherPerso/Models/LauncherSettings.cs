using System.IO;
using System.Text;
using MinecraftLauncherPerso.Services.Hardware;

namespace MinecraftLauncherPerso.Models;

/// <summary>Préférences utilisateur persistées entre deux lancements (settings.json).</summary>
public sealed class LauncherSettings
{
    public string MinecraftVersion { get; set; } = "1.20.1";

    /// <summary>
    /// Doit suivre la version de Forge du serveur : un mod qui exige un Forge plus récent que celui
    /// du client refuse de se charger chez le joueur. Un settings.json qui porte encore un ancien
    /// défaut est migré par SettingsManager (MigrateForgeVersion).
    /// </summary>
    public string ForgeVersion { get; set; } = DefaultForgeVersion;

    internal const string DefaultForgeVersion = "47.4.23";

    /// <summary>Anciens défauts de <see cref="ForgeVersion"/> pour Minecraft 1.20.1, reconnus pour
    /// la migration (47.3.0 : v1.9.0 à v1.11.1).</summary>
    internal static readonly string[] LegacyDefaultForgeVersions = ["47.3.0"];

    /// <summary>
    /// Base HTTPS du dossier modpack sur le VPS (v1.11.0) : nom de domaine DuckDNS (le même que
    /// <see cref="ServerHost"/>, qui pointe sur le VPS) en https://, plutôt que l'IP brute en
    /// http:// encodée en base64 des versions précédentes. Le base64 ne protégeait rien (décodable
    /// en une ligne, IP de toute façon visible dans servers.dat) ; le vrai enjeu était le HTTP en
    /// clair : mods (.jar exécutés avec les droits du joueur) et manifest (leurs empreintes)
    /// transitaient par le même canal non chiffré. Aucun repli en HTTP : le VPS sert TLS (Caddy,
    /// voir scripts/vps/), et un intermédiaire qui bloquerait le port 443 ne doit pas pouvoir
    /// forcer le launcher à télécharger des mods en clair.
    /// </summary>
    private const string VpsModpackBaseUrl = "https://astralnexusmc.duckdns.org/modpack/";

    /// <summary>
    /// URL directe de l'archive .zip du modpack (mods/ + config/ à sa racine) hébergée sur le VPS.
    /// Peut être écrasée dans settings.json si le VPS change.
    /// </summary>
    public string ModpackZipUrl { get; set; } = VpsModpackBaseUrl + "Algaron-modded.zip";

    /// <summary>
    /// URL du manifest par fichier (JSON, voir ModpackManifest.cs), en alternative à
    /// <see cref="ModpackZipUrl"/> : permet des mises à jour incrémentales (ne retélécharger que
    /// les fichiers qui ont changé) au lieu de tout le zip à chaque mise à jour du pack. Pointe par
    /// défaut vers le manifest 1.20.1 réellement publié sur le VPS (voir scripts/vps/). Peut être
    /// vidée dans settings.json pour revenir au flux zip unique.
    /// </summary>
    public string ModpackManifestUrl { get; set; } = VpsModpackBaseUrl + "manifest.json";

    /// <summary>
    /// URL d'un fichier texte optionnel (maintenance.txt sur le VPS) : son contenu, s'il existe et
    /// n'est pas vide, s'affiche en bandeau sur le dashboard. Pointe par défaut vers
    /// maintenance.txt à côté du modpack sur le VPS : absence ou fichier vide = bannière jamais
    /// affichée, donc publier/supprimer ce fichier sur le VPS suffit à l'afficher/masquer, sans
    /// jamais retoucher settings.json.
    /// </summary>
    public string MaintenanceMessageUrl { get; set; } = VpsModpackBaseUrl + "maintenance.txt";

    /// <summary>
    /// Anciennes valeurs par défaut (http:// + IP brute, encodées en base64 jusqu'en v1.10.0) des
    /// trois URL ci-dessus, pour que SettingsManager puisse reconnaître un settings.json qui les
    /// porte encore et le faire basculer sur les nouvelles (voir MigrateLegacyVpsUrls). Restent
    /// encodées : elles contiennent l'IP, et la raison de ne pas l'écrire en clair n'a pas changé.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> LegacyVpsUrlDefaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [DecodeBase64("aHR0cDovLzE4NS4xODUuODIuMTgwL21vZHBhY2svQWxnYXJvbi1tb2RkZWQuemlw")] = VpsModpackBaseUrl + "Algaron-modded.zip",
        [DecodeBase64("aHR0cDovLzE4NS4xODUuODIuMTgwL21vZHBhY2svbWFuaWZlc3QuanNvbg==")] = VpsModpackBaseUrl + "manifest.json",
        [DecodeBase64("aHR0cDovLzE4NS4xODUuODIuMTgwL21vZHBhY2svbWFpbnRlbmFuY2UudHh0")] = VpsModpackBaseUrl + "maintenance.txt",
    };

    private static string DecodeBase64(string value) => Encoding.UTF8.GetString(Convert.FromBase64String(value));

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
    public string ServerHost { get; set; } = DefaultServerHost;

    /// <summary>
    /// Port du serveur Minecraft : 25566 depuis v1.11.0 (le serveur Astral Nexus a quitté le port
    /// standard 25565). Un settings.json qui porte encore l'ancien port avec l'hôte par défaut est
    /// migré par SettingsManager (MigrateServerPort).
    /// </summary>
    public int ServerPort { get; set; } = DefaultServerPort;

    internal const int DefaultServerPort = 25566;

    /// <summary>Ancien port par défaut (jusqu'en v1.10.0), reconnu pour la migration.</summary>
    internal const int LegacyDefaultServerPort = 25565;

    /// <summary>Hôte par défaut, exposé pour que les migrations ne touchent qu'une configuration
    /// qui pointe encore sur le serveur Astral Nexus (jamais un serveur personnalisé).</summary>
    internal const string DefaultServerHost = "astralnexusmc.duckdns.org";

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
