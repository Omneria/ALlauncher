using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using MinecraftLauncherPerso.Services.Diagnostics;

namespace MinecraftLauncherPerso.Services.ModSync;

/// <summary>
/// Synchronise le pack de mods/config depuis le VPS, en mode zip unique (historique) ou manifest
/// par fichier (incrémental, voir ModpackManifest.cs) selon qu'un ModpackManifestUrl est configuré.
///
/// Mode zip : ne retélécharge que si le fichier a changé côté serveur (HEAD, ETag/Last-Modified/
/// Content-Length comparés à la dernière synchro réussie, mise en cache localement).
/// Mode manifest : chaque fichier est comparé par hash au contenu local ; seuls ceux qui diffèrent
/// sont retéléchargés, pas de HEAD/ETag global nécessaire.
/// </summary>
public sealed class ModSyncService : IModSyncService
{
    private const string CacheFileName = "launcher-modpack-cache.json";
    private const string ManifestCacheFileName = "launcher-modpack-manifest-cache.json";
    private const string RollbackModsDirName = ".rollback-mods";
    private const string RollbackConfigDirName = ".rollback-config";

    private readonly HttpClient _httpClient;

    public ModSyncService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task SyncAsync(
        string modpackZipUrl,
        string? manifestUrl,
        string gameDirectory,
        IProgress<string>? progress = null,
        IProgress<double>? downloadProgress = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(manifestUrl))
        {
            await SyncFromManifestAsync(manifestUrl, gameDirectory, progress, downloadProgress, cancellationToken);
            return;
        }

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

        // Vérifie l'espace disque avant de commencer : auparavant, un disque plein ne se révélait
        // qu'en pleine extraction, avec une IOException peu claire. Facteur x3 par prudence : le zip
        // temporaire, les fichiers extraits et la sauvegarde de rollback (BackupCurrentModpackAsync)
        // coexistent brièvement sur le disque dans le pire cas.
        if (remoteMetadata?.ContentLength is > 0)
        {
            var requiredBytes = remoteMetadata.ContentLength.Value * 3;
            if (!DiskSpaceChecker.HasEnoughFreeSpace(gameDirectory, requiredBytes))
            {
                throw new InvalidOperationException(
                    $"Espace disque insuffisant pour mettre à jour le modpack (environ {DiskSpaceChecker.FormatBytes(requiredBytes)} nécessaires).");
            }
        }

        progress?.Report("Téléchargement du modpack...");
        var tempZipPath = Path.Combine(Path.GetTempPath(), $"modpack-{Guid.NewGuid():N}.zip");

        try
        {
            await DownloadAsync(modpackZipUrl, tempZipPath, remoteMetadata?.ETag, remoteMetadata?.ContentLength, progress, downloadProgress, cancellationToken);

            // Sauvegarde d'un cran en arrière (voir RollbackAsync) juste avant d'écraser mods/config
            // avec le contenu du nouveau zip.
            await BackupCurrentModpackAsync(gameDirectory, progress, cancellationToken);

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

    public DateTimeOffset? GetLastSyncedAt(string gameDirectory)
    {
        var zipSyncedAt = LoadCache(Path.Combine(gameDirectory, CacheFileName))?.SyncedAt;
        var manifestSyncedAt = LoadManifestSyncTimestamp(Path.Combine(gameDirectory, ManifestCacheFileName));

        if (zipSyncedAt is null)
        {
            return manifestSyncedAt;
        }

        if (manifestSyncedAt is null)
        {
            return zipSyncedAt;
        }

        return zipSyncedAt > manifestSyncedAt ? zipSyncedAt : manifestSyncedAt;
    }

    /// <summary>
    /// En mode zip, supprime le cache ETag local avant de resynchroniser : SyncAsync le traite
    /// alors forcément comme "pas à jour", même si le contenu distant n'a pas changé — seul moyen
    /// de forcer un retéléchargement complet quand c'est un fichier local qui a été corrompu/
    /// supprimé, pas le serveur qui a changé de version. En mode manifest, SyncFromManifestAsync
    /// revérifie déjà le hash de chaque fichier à chaque appel : Repair et Sync y sont la même
    /// opération, pas de cache à invalider.
    /// </summary>
    public Task RepairAsync(
        string modpackZipUrl,
        string? manifestUrl,
        string gameDirectory,
        IProgress<string>? progress = null,
        IProgress<double>? downloadProgress = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(manifestUrl))
        {
            progress?.Report("Réparation : revérification du hash de chaque fichier du modpack...");
            return SyncFromManifestAsync(manifestUrl, gameDirectory, progress, downloadProgress, cancellationToken);
        }

        var cachePath = Path.Combine(gameDirectory, CacheFileName);
        if (File.Exists(cachePath))
        {
            File.Delete(cachePath);
        }

        progress?.Report("Réparation : retéléchargement complet du modpack...");
        return SyncAsync(modpackZipUrl, manifestUrl, gameDirectory, progress, downloadProgress, cancellationToken);
    }

    public async Task PrefetchAsync(
        string modpackZipUrl,
        string? manifestUrl,
        string gameDirectory,
        IProgress<string>? progress = null,
        IProgress<double>? downloadProgress = null,
        CancellationToken cancellationToken = default)
    {
        // Mode manifest : chaque fichier est déjà petit et téléchargé individuellement à la
        // demande (SyncFromManifestAsync), contrairement au zip unique qui peut faire plusieurs
        // centaines de Mo — moins de valeur à précharger en tâche de fond, et ça éviterait surtout
        // de dupliquer la logique de diff (manifest vs local) ici pour un gain marginal.
        if (!string.IsNullOrWhiteSpace(manifestUrl))
        {
            return;
        }

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

    public bool HasRollbackAvailable(string gameDirectory) =>
        Directory.Exists(Path.Combine(gameDirectory, RollbackModsDirName)) ||
        Directory.Exists(Path.Combine(gameDirectory, RollbackConfigDirName));

    /// <summary>
    /// Restaure mods/config tels que sauvegardés juste avant la dernière mise à jour appliquée
    /// (voir BackupCurrentModpackAsync) : un seul cran de recul est conservé, pas un historique.
    ///
    /// Effet volontairement temporaire, pas un vrai pin de version : le cache de synchro (ETag ou
    /// horodatage manifest) n'est pas touché ici. En mode zip, si le contenu distant n'a pas changé
    /// depuis, SyncAsync le considérera "à jour" via ce cache et laissera le rollback en place ; en
    /// mode manifest, en revanche, SyncFromManifestAsync revérifie systématiquement chaque fichier
    /// par hash contre le manifest courant, donc une synchronisation normale ultérieure retélécharge
    /// et annule ce rollback tant que le VPS sert toujours la version problématique — une vraie
    /// protection durable demanderait que le VPS lui-même serve une version antérieure.
    /// </summary>
    public async Task RollbackAsync(
        string gameDirectory,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!HasRollbackAvailable(gameDirectory))
        {
            throw new InvalidOperationException("Aucune version précédente du modpack n'est disponible pour un retour en arrière.");
        }

        progress?.Report("Retour à la version précédente du modpack...");

        await Task.Run(() =>
        {
            RestoreDirectory(Path.Combine(gameDirectory, RollbackModsDirName), Path.Combine(gameDirectory, "mods"));
            RestoreDirectory(Path.Combine(gameDirectory, RollbackConfigDirName), Path.Combine(gameDirectory, "config"));
        }, cancellationToken);

        progress?.Report("Version précédente restaurée (effet temporaire, voir Paramètres).");
    }

    /// <summary>
    /// Sauvegarde best-effort de mods/ et config/ juste avant de les écraser par une nouvelle
    /// version du modpack, pour permettre RollbackAsync si une mise à jour s'avère cassée. Écrase
    /// la sauvegarde précédente : un seul cran de recul est conservé, pas un historique complet.
    /// </summary>
    private async Task BackupCurrentModpackAsync(string gameDirectory, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Run(() =>
            {
                BackupDirectory(Path.Combine(gameDirectory, "mods"), Path.Combine(gameDirectory, RollbackModsDirName));
                BackupDirectory(Path.Combine(gameDirectory, "config"), Path.Combine(gameDirectory, RollbackConfigDirName));
            }, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort : un échec de sauvegarde (espace disque, permissions) ne doit pas
            // empêcher la mise à jour elle-même, juste priver l'utilisateur du recul en cas de
            // pépin — dégradation acceptable plutôt que de bloquer toute la synchro pour ça.
            progress?.Report("Sauvegarde de la version précédente impossible, la mise à jour continue quand même.");
        }
    }

    private static void BackupDirectory(string sourceDir, string backupDir)
    {
        if (!Directory.Exists(sourceDir))
        {
            return;
        }

        if (Directory.Exists(backupDir))
        {
            Directory.Delete(backupDir, recursive: true);
        }

        CopyDirectoryContents(sourceDir, backupDir);
    }

    private static void RestoreDirectory(string backupDir, string targetDir)
    {
        if (!Directory.Exists(backupDir))
        {
            return;
        }

        if (Directory.Exists(targetDir))
        {
            Directory.Delete(targetDir, recursive: true);
        }

        CopyDirectoryContents(backupDir, targetDir);
    }

    private static void CopyDirectoryContents(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);
        foreach (var filePath in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, filePath);
            var destPath = Path.Combine(destinationDir, relative);
            var destDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            File.Copy(filePath, destPath, overwrite: true);
        }
    }

    /// <summary>
    /// Synchro incrémentale par manifest (v1.9.0) : compare le hash de chaque fichier local à
    /// celui du manifest et ne retélécharge que ceux qui diffèrent, au lieu de tout un zip à chaque
    /// mise à jour du pack — le gros gain face au mode zip pour un pack de plusieurs centaines de Mo
    /// dont une mise à jour ne change souvent qu'un ou deux mods.
    /// </summary>
    private async Task SyncFromManifestAsync(
        string manifestUrl,
        string gameDirectory,
        IProgress<string>? progress,
        IProgress<double>? downloadProgress,
        CancellationToken cancellationToken)
    {
        progress?.Report("Vérification du manifest du modpack...");
        var manifest = await FetchManifestAsync(manifestUrl, cancellationToken);

        Directory.CreateDirectory(gameDirectory);

        var toDownload = new List<KeyValuePair<string, ModpackManifestFile>>();
        foreach (var entry in manifest.Files)
        {
            var localPath = Path.Combine(gameDirectory, entry.Key);
            if (!File.Exists(localPath) || !await MatchesHashAsync(localPath, entry.Value.Sha256, cancellationToken))
            {
                toDownload.Add(entry);
            }
        }

        var manifestCachePath = Path.Combine(gameDirectory, ManifestCacheFileName);

        if (toDownload.Count == 0)
        {
            progress?.Report("Modpack déjà à jour.");
            SaveManifestSyncTimestamp(manifestCachePath);
            return;
        }

        await ReportChangelogAsync(manifestUrl, progress, cancellationToken);

        var totalBytes = toDownload.Sum(f => f.Value.Size ?? 0);

        // Voir le commentaire équivalent dans SyncAsync (mode zip) : facteur x2 ici (pas x3, un
        // fichier téléchargé remplace directement l'ancien, pas de zip temporaire intermédiaire).
        if (totalBytes > 0)
        {
            var requiredBytes = totalBytes * 2;
            if (!DiskSpaceChecker.HasEnoughFreeSpace(gameDirectory, requiredBytes))
            {
                throw new InvalidOperationException(
                    $"Espace disque insuffisant pour mettre à jour le modpack (environ {DiskSpaceChecker.FormatBytes(requiredBytes)} nécessaires).");
            }
        }

        // Sauvegarde d'un cran en arrière (voir RollbackAsync) juste avant d'écraser les fichiers
        // qui vont changer.
        await BackupCurrentModpackAsync(gameDirectory, progress, cancellationToken);

        progress?.Report($"{toDownload.Count} fichier(s) à mettre à jour...");
        long doneBytes = 0;
        var lastReportedPercent = -1;

        foreach (var (relativePath, entry) in toDownload)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"Téléchargement : {relativePath}");

            var localPath = Path.Combine(gameDirectory, relativePath);
            var directory = Path.GetDirectoryName(localPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var bytes = await _httpClient.GetByteArrayAsync(entry.Url, cancellationToken);
            await File.WriteAllBytesAsync(localPath, bytes, cancellationToken);

            doneBytes += bytes.LongLength;
            if (totalBytes > 0)
            {
                var percent = (int)(doneBytes * 100 / totalBytes);
                if (percent != lastReportedPercent)
                {
                    lastReportedPercent = percent;
                    downloadProgress?.Report(doneBytes / (double)totalBytes);
                }
            }
        }

        SaveManifestSyncTimestamp(manifestCachePath);
        progress?.Report("Modpack mis à jour.");
    }

    private async Task<ModpackManifest> FetchManifestAsync(string manifestUrl, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(manifestUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<ModpackManifest>(stream, cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Manifest du modpack invalide ou vide.");
    }

    private static async Task<bool> MatchesHashAsync(string filePath, string expectedSha256, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(expectedSha256))
        {
            // Manifest sans hash pour ce fichier : pas de moyen de vérifier, on suppose valide
            // plutôt que de forcer un retéléchargement à chaque appel.
            return true;
        }

        await using var stream = File.OpenRead(filePath);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        return string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase);
    }

    private static void SaveManifestSyncTimestamp(string cachePath)
    {
        File.WriteAllText(cachePath, JsonSerializer.Serialize(new ManifestSyncCache { SyncedAt = DateTimeOffset.Now }));
    }

    private static DateTimeOffset? LoadManifestSyncTimestamp(string cachePath)
    {
        if (!File.Exists(cachePath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ManifestSyncCache>(File.ReadAllText(cachePath))?.SyncedAt;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Affiche le contenu d'un éventuel changelog.txt hébergé à côté de l'URL de référence (le zip
    /// en mode classique, le manifest en mode incrémental), quand une mise à jour est détectée.
    /// Optionnel : si le fichier n'existe pas (404, VPS pas configuré pour ça) ou est inaccessible,
    /// on l'ignore silencieusement plutôt que de faire échouer la synchro pour un simple fichier de
    /// nouveautés.
    /// </summary>
    private async Task ReportChangelogAsync(string referenceUrl, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(referenceUrl, UriKind.Absolute, out var refUri))
        {
            return;
        }

        var changelogUri = new Uri(refUri, "changelog.txt");

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
    /// n'a pas d'URL de téléchargement par fichier individuel pour ne réparer que celui-là. Ce
    /// manifest.json "plat" (chemin -> hash seul) est distinct du ModpackManifest par fichier avec
    /// URL utilisé par SyncFromManifestAsync (deux formats, deux usages différents).
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

    private sealed class ManifestSyncCache
    {
        [JsonPropertyName("syncedAt")]
        public DateTimeOffset SyncedAt { get; set; }
    }
}
