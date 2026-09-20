using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace MinecraftLauncherPerso.Services.Update;

/// <summary>
/// Vérifie et applique les mises à jour du launcher via les GitHub Releases du dépôt (publiées
/// par le workflow CI quand un tag v*.*.* est poussé, voir .github/workflows/build-windows.yml) :
/// pas de serveur de mise à jour dédié à héberger, GitHub sert directement les .exe en public.
/// </summary>
public sealed class GitHubUpdateService : IUpdateService
{
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/Omneria/ALlauncher/releases/latest";

    private readonly HttpClient _httpClient;

    public GitHubUpdateService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();

        // L'API GitHub rejette (403) toute requête sans User-Agent.
        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ALLauncher-UpdateChecker");
        }
    }

    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync(LatestReleaseApiUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            var tagName = doc.RootElement.GetProperty("tag_name").GetString();
            if (tagName is null || !TryParseVersion(tagName, out var remoteVersion))
            {
                return null;
            }

            if (remoteVersion <= GetCurrentVersion())
            {
                return null;
            }

            var asset = doc.RootElement.GetProperty("assets").EnumerateArray()
                .FirstOrDefault(a => (a.GetProperty("name").GetString() ?? "").EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

            if (asset.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var downloadUrl = asset.GetProperty("browser_download_url").GetString();
            return string.IsNullOrEmpty(downloadUrl) ? null : new UpdateInfo(remoteVersion, downloadUrl);
        }
        catch (Exception)
        {
            // Vérification best-effort : ne doit jamais empêcher de lancer le jeu.
            return null;
        }
    }

    public async Task ApplyUpdateAndRestartAsync(UpdateInfo update, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var currentExePath = Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("Impossible de déterminer le chemin de l'exécutable courant.");

        progress?.Report("Téléchargement de la mise à jour...");
        var newExePath = Path.Combine(Path.GetTempPath(), $"AL Launcher.new.{Guid.NewGuid():N}.exe");

        await using (var stream = await _httpClient.GetStreamAsync(update.DownloadUrl, cancellationToken))
        await using (var file = new FileStream(newExePath, FileMode.Create, FileAccess.Write))
        {
            await stream.CopyToAsync(file, cancellationToken);
        }

        // Le process courant verrouille son propre .exe (Windows) : impossible de l'écraser tant
        // qu'il tourne. Un script détaché attend sa fermeture (boucle sur tasklist/PID), remplace
        // le fichier, puis relance le launcher.
        var scriptPath = Path.Combine(Path.GetTempPath(), $"al-launcher-update-{Guid.NewGuid():N}.cmd");
        var script = $"""
            @echo off
            :wait
            tasklist /fi "PID eq {Environment.ProcessId}" | find "{Environment.ProcessId}" >nul
            if not errorlevel 1 (
                timeout /t 1 /nobreak >nul
                goto wait
            )
            move /y "{newExePath}" "{currentExePath}" >nul
            start "" "{currentExePath}"
            del "%~f0"
            """;
        await File.WriteAllTextAsync(scriptPath, script, cancellationToken);

        progress?.Report("Redémarrage du launcher...");
        Process.Start(new ProcessStartInfo(scriptPath)
        {
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true,
        });

        Environment.Exit(0);
    }

    private static Version GetCurrentVersion() =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    private static bool TryParseVersion(string tagName, out Version version)
    {
        var trimmed = tagName.TrimStart('v', 'V');
        if (Version.TryParse(trimmed, out var parsed))
        {
            version = parsed;
            return true;
        }

        version = new Version(0, 0, 0);
        return false;
    }
}
