using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MinecraftLauncherPerso.Services.ModSync;

/// <summary>
/// Synchronise le pack de mods/config depuis une archive .zip unique hébergée sur le VPS
/// (ex. https://exemple.tld/modpack/Algaron-modded.zip, configurée dans settings.json),
/// qui doit contenir mods/ et config/ à sa racine (mêmes noms de dossiers qu'un .minecraft classique).
///
/// Ne retélécharge que si le fichier a changé côté serveur : une requête HEAD récupère
/// ETag/Last-Modified/Content-Length, comparés à la dernière synchro réussie (mise en cache
/// localement) — pas de re-téléchargement à chaque lancement. Si le serveur ne supporte pas
/// HEAD (config VPS basique), on retélécharge par prudence plutôt que d'échouer.
/// </summary>
public sealed class ModSyncService : IModSyncService
{
    private const string CacheFileName = "launcher-modpack-cache.json";

    private readonly HttpClient _httpClient;

    public ModSyncService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task SyncAsync(
        string modpackZipUrl,
        string gameDirectory,
        IProgress<string>? progress = null,
        IProgress<double>? downloadProgress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modpackZipUrl))
        {
            throw new InvalidOperationException(
                "ModpackZipUrl n'est pas configuré (settings.json) : impossible de synchroniser le modpack.");
        }

        progress?.Report("Vérification de la version du modpack...");

        RemoteZipMetadata? remoteMetadata;
        try
        {
            remoteMetadata = await FetchRemoteMetadataAsync(modpackZipUrl, cancellationToken);
        }
        catch (HttpRequestException)
        {
            // Serveur ne supportant peut-être pas HEAD : on retélécharge par prudence.
            remoteMetadata = null;
        }

        var cachePath = Path.Combine(gameDirectory, CacheFileName);
        var cached = LoadCache(cachePath);

        var upToDate = remoteMetadata is not null
            && cached is not null
            && cached.Matches(remoteMetadata)
            && Directory.Exists(Path.Combine(gameDirectory, "mods"));

        if (upToDate && await VerifyIntegrityAsync(modpackZipUrl, gameDirectory, progress, cancellationToken))
        {
            progress?.Report("Modpack déjà à jour.");
            // Rafraîchit SyncedAt même sans nouveau téléchargement : "dernière synchro" reflète la
            // dernière fois où on a confirmé être à jour, pas seulement le dernier vrai téléchargement.
            if (remoteMetadata is not null)
            {
                SaveCache(cachePath, remoteMetadata);
            }

            return;
        }

        await ReportChangelogAsync(modpackZipUrl, progress, cancellationToken);

        progress?.Report("Téléchargement du modpack...");
        var tempZipPath = Path.Combine(Path.GetTempPath(), $"modpack-{Guid.NewGuid():N}.zip");

        try
        {
            await DownloadAsync(modpackZipUrl, tempZipPath, remoteMetadata?.ETag, remoteMetadata?.ContentLength, progress, downloadProgress, cancellationToken);

            progress?.Report("Extraction du modpack (mods/config)...");
            Directory.CreateDirectory(gameDirectory);
            ZipFile.ExtractToDirectory(tempZipPath, gameDirectory, overwriteFiles: true);
        }
        finally
        {
            if (File.Exists(tempZipPath))
            {
                File.Delete(tempZipPath);
            }
        }

        if (remoteMetadata is not null)
        {
            SaveCache(cachePath, remoteMetadata);
        }

        progress?.Report("Modpack mis à jour.");
    }

    public DateTimeOffset? GetLastSyncedAt(string gameDirectory) =>
        LoadCache(Path.Combine(gameDirectory, CacheFileName))?.SyncedAt;

    /// <summary>
    /// Supprime le cache ETag local avant de resynchroniser : SyncAsync le traite alors forcément
    /// comme "pas à jour", même si le contenu distant n'a pas changé — seul moyen de forcer un
    /// retéléchargement complet quand c'est un fichier local qui a été corrompu/supprimé, pas le
    /// serveur qui a changé de version.
    /// </summary>
    public Task RepairAsync(
        string modpackZipUrl,
        string gameDirectory,
        IProgress<string>? progress = null,
        IProgress<double>? downloadProgress = null,
        CancellationToken cancellationToken = default)
    {
        var cachePath = Path.Combine(gameDirectory, CacheFileName);
        if (File.Exists(cachePath))
        {
            File.Delete(cachePath);
        }

        progress?.Report("Réparation : retéléchargement complet du modpack...");
        return SyncAsync(modpackZipUrl, gameDirectory, progress, downloadProgress, cancellationToken);
    }

    public async Task PrefetchAsync(
        string modpackZipUrl,
        string gameDirectory,
        IProgress<string>? progress = null,
        IProgress<double>? downloadProgress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modpackZipUrl))
        {
            return;
        }

        RemoteZipMetadata? remoteMetadata;
        try
        {
            remoteMetadata = await FetchRemoteMetadataAsync(modpackZipUrl, cancellationToken);
        }
        catch (HttpRequestException)
        {
            // Best-effort : un HEAD indisponible ici ne doit pas faire échouer quoi que ce soit,
            // SyncAsync (au clic sur Jouer) retentera de toute façon avec sa propre gestion d'échec.
            return;
        }

        var cachePath = Path.Combine(gameDirectory, CacheFileName);
        var cached = LoadCache(cachePath);
        var upToDate = remoteMetadata is not null && cached is not null && cached.Matches(remoteMetadata);
        if (upToDate)
        {
            return;
        }

        progress?.Report("Préchargement du modpack en arrière-plan...");

        // N'extrait pas et n'écrit pas le cache : seul le fichier .part partagé avec SyncAsync
        // (même chemin, dérivé du hash de l'URL) est complété ici. SyncAsync, appelé plus tard au
        // clic sur "Jouer", le retrouvera déjà téléchargé (ou partiellement) et n'aura plus qu'à le
        // déplacer/extraire, rendant ce clic quasi instantané côté téléchargement.
        await EnsurePartialDownloadedAsync(
            modpackZipUrl, remoteMetadata?.ETag, remoteMetadata?.ContentLength, progress, downloadProgress, cancellationToken);
    }

    /// <summary>
    /// Affiche le contenu d'un éventuel changelog.txt hébergé à côté du zip du modpack (même
    /// dossier), quand une mise à jour du modpack est détectée. Optionnel : si le fichier n'existe
    /// pas (404, VPS pas encore configuré pour ça) ou est inaccessible, on l'ignore silencieusement
    /// plutôt que de faire échouer la synchro pour un simple fichier de nouveautés.
    /// </summary>
    private async Task ReportChangelogAsync(string modpackZipUrl, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(modpackZipUrl, UriKind.Absolute, out var zipUri))
        {
            return;
        }

        var changelogUri = new Uri(zipUri, "changelog.txt");

        try
        {
            using var response = await _httpClient.GetAsync(changelogUri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            var content = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
            if (content.Length == 0)
            {
                return;
            }

            progress?.Report("Nouveautés du modpack :");
            foreach (var line in content.Split('\n'))
            {
                progress?.Report($"  {line.TrimEnd('\r')}");
            }
        }
        catch (HttpRequestException)
        {
            // Pas de changelog disponible : rien à afficher, ça n'empêche pas la synchro.
        }
    }

    /// <summary>
    /// Vérifie les fichiers extraits localement contre un éventuel manifest.json hébergé à côté
    /// du zip (mapping "chemin relatif" -> "sha256 hexadécimal"). Optionnel : si le manifest
    /// n'existe pas (404, VPS pas configuré pour ça), on considère l'intégrité valide (retourne
    /// true) plutôt que de forcer un retéléchargement inutile. Un fichier manquant ou dont le hash
    /// ne correspond plus (corruption, modification manuelle accidentelle) fait retourner false :
    /// SyncAsync retélécharge alors le zip complet même si le cache ETag dit "à jour", puisqu'on
    /// n'a pas d'URL de téléchargement par fichier individuel pour ne réparer que celui-là.
    /// </summary>
    private async Task<bool> VerifyIntegrityAsync(string modpackZipUrl, string gameDirectory, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(modpackZipUrl, UriKind.Absolute, out var zipUri))
        {
            return true;
        }

        var manifestUri = new Uri(zipUri, "manifest.json");
        Dictionary<string, string>? manifest;

        try
        {
            using var response = await _httpClient.GetAsync(manifestUri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return true;
            }

            manifest = JsonSerializer.Deserialize<Dictionary<string, string>>(
                await response.Content.ReadAsStreamAsync(cancellationToken));
        }
        catch (HttpRequestException)
        {
            return true;
        }
        catch (JsonException)
        {
            return true;
        }

        if (manifest is null || manifest.Count == 0)
        {
            return true;
        }

        foreach (var (relativePath, expectedHash) in manifest)
        {
            var filePath = Path.Combine(gameDirectory, relativePath);
            if (!File.Exists(filePath))
            {
                progress?.Report($"Fichier manquant détecté ({relativePath}), re-synchronisation du modpack...");
                return false;
            }

            await using var stream = File.OpenRead(filePath);
            var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                progress?.Report($"Fichier corrompu détecté ({relativePath}), re-synchronisation du modpack...");
                return false;
            }
        }

        return true;
    }

    private async Task<RemoteZipMetadata> FetchRemoteMetadataAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, url);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        return new RemoteZipMetadata
        {
            ETag = response.Headers.ETag?.Tag,
            LastModified = response.Content.Headers.LastModified,
            ContentLength = response.Content.Headers.ContentLength,
        };
    }

    /// <summary>
    /// Télécharge le zip du modpack avec reprise sur coupure (connexion VPS instable, fermeture du
    /// launcher en pleine synchro...). Auparavant une interruption forçait à retélécharger le zip
    /// entier depuis le début, potentiellement plusieurs centaines de Mo.
    ///
    /// Le fichier partiel est écrit à un chemin stable dérivé du hash de l'URL (contrairement à
    /// destinationPath qui contient un GUID par appel) pour pouvoir le retrouver à la tentative
    /// suivante, même après redémarrage du launcher. Repris via Range/If-Range (ETag) : si le
    /// serveur ignore Range (200 au lieu de 206) ou si l'ETag ne correspond plus au fichier partiel
    /// (contenu changé côté serveur entretemps), on repart de zéro plutôt que de produire un zip
    /// corrompu par concaténation de deux versions différentes.
    /// </summary>
    private async Task DownloadAsync(
        string url,
        string destinationPath,
        string? etag,
        long? expectedLength,
        IProgress<string>? progress,
        IProgress<double>? downloadProgress,
        CancellationToken cancellationToken)
    {
        var partialPath = await EnsurePartialDownloadedAsync(url, etag, expectedLength, progress, downloadProgress, cancellationToken);

        // Téléchargement complet réussi : le .part devient le zip final, prêt pour extraction. En
        // cas d'échec avant ce point (exception ci-dessus), le .part reste en place tel quel pour
        // permettre une reprise à la prochaine tentative.
        File.Move(partialPath, destinationPath, overwrite: true);
    }

    /// <summary>
    /// Cœur du téléchargement reprenable, partagé par DownloadAsync (SyncAsync, au clic sur
    /// "Jouer") et PrefetchAsync (arrière-plan) : les deux écrivent dans le même fichier .part
    /// (chemin stable dérivé du hash de l'URL), donc un préchargement en arrière-plan complété (ou
    /// partiel) profite directement à SyncAsync appelé plus tard, sans repartir de zéro.
    /// </summary>
    /// <returns>Chemin du fichier .part, entièrement téléchargé.</returns>
    private async Task<string> EnsurePartialDownloadedAsync(
        string url,
        string? etag,
        long? expectedLength,
        IProgress<string>? progress,
        IProgress<double>? downloadProgress,
        CancellationToken cancellationToken)
    {
        var partialPath = GetPartialDownloadPath(url);
        var resumeFrom = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0L;

        if (expectedLength is > 0 && resumeFrom == expectedLength.Value)
        {
            // Déjà entièrement téléchargé (ex. par un PrefetchAsync précédent) : rien à refaire.
            // Sans ce court-circuit, une requête Range exactement en fin de fichier renverrait
            // souvent 416 Range Not Satisfiable côté serveur au lieu d'un corps vide.
            downloadProgress?.Report(1.0);
            return partialPath;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (resumeFrom > 0)
        {
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(resumeFrom, null);
            if (etag is not null)
            {
                request.Headers.IfRange = new System.Net.Http.Headers.RangeConditionHeaderValue(new System.Net.Http.Headers.EntityTagHeaderValue(etag));
            }
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var isResuming = resumeFrom > 0 && response.StatusCode == System.Net.HttpStatusCode.PartialContent;
        if (resumeFrom > 0 && !isResuming)
        {
            // Serveur qui a ignoré Range (200 complet) ou contenu changé (ETag différent) : le
            // fichier partiel existant ne correspond plus à ce qu'on est en train de recevoir.
            resumeFrom = 0;
        }

        var totalBytes = isResuming
            ? (response.Content.Headers.ContentRange?.Length ?? -1L)
            : response.Content.Headers.ContentLength ?? -1L;

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using (var fileStream = new FileStream(
            partialPath,
            isResuming ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.None))
        {
            var buffer = new byte[81920];
            long totalRead = resumeFrom;
            int bytesRead;
            var lastReportedPercent = -1;

            while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                totalRead += bytesRead;

                if (totalBytes > 0)
                {
                    var percent = (int)(totalRead * 100 / totalBytes);
                    if (percent != lastReportedPercent)
                    {
                        lastReportedPercent = percent;
                        progress?.Report($"Téléchargement du modpack... {percent}%");
                        downloadProgress?.Report(totalRead / (double)totalBytes);
                    }
                }
            }
        }

        // Téléchargement complet réussi. En cas d'échec avant ce point (exception ci-dessus), le
        // .part reste en place tel quel pour permettre une reprise à la prochaine tentative (par
        // DownloadAsync ou un nouveau PrefetchAsync).
        return partialPath;
    }

    private static string GetPartialDownloadPath(string url)
    {
        var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(url)));
        return Path.Combine(Path.GetTempPath(), $"modpack-{hash}.part");
    }

    private static RemoteZipMetadata? LoadCache(string cachePath)
    {
        if (!File.Exists(cachePath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<RemoteZipMetadata>(File.ReadAllText(cachePath));
        }
        catch (JsonException)
        {
            // Cache local corrompu : on ignore, le modpack sera retéléchargé et le cache recréé.
            return null;
        }
    }

    private static void SaveCache(string cachePath, RemoteZipMetadata metadata)
    {
        var directory = Path.GetDirectoryName(cachePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Toujours l'heure de CETTE sauvegarde (pas celle éventuellement déjà présente dans
        // metadata) : SaveCache est appelée aussi bien après un vrai téléchargement qu'après une
        // simple confirmation "déjà à jour", et dans les deux cas "dernière synchro" doit avancer.
        metadata.SyncedAt = DateTimeOffset.Now;
        File.WriteAllText(cachePath, JsonSerializer.Serialize(metadata));
    }

    private sealed class RemoteZipMetadata
    {
        [JsonPropertyName("etag")]
        public string? ETag { get; set; }

        [JsonPropertyName("lastModified")]
        public DateTimeOffset? LastModified { get; set; }

        [JsonPropertyName("contentLength")]
        public long? ContentLength { get; set; }

        [JsonPropertyName("syncedAt")]
        public DateTimeOffset? SyncedAt { get; set; }

        public bool Matches(RemoteZipMetadata other)
        {
            // Si le serveur fournit un ETag, c'est le signal le plus fiable : on s'y fie seul.
            if (ETag is not null || other.ETag is not null)
            {
                return ETag == other.ETag;
            }

            return LastModified == other.LastModified && ContentLength == other.ContentLength;
        }
    }
}
