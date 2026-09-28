using System.IO;
using System.Text;

namespace MinecraftLauncherPerso.Services.Diagnostics;

/// <summary>
/// Contenu du rapport "SIGNALER UN PROBLÈME" (Paramètres), copié dans le presse-papier pour être
/// collé sur Discord. Complété en v1.15.0 avec ce qu'il fallait jusqu'ici redemander au joueur à
/// chaque fois : la RAM physique du PC, la liste des mods réellement présents dans mods/ (un mod
/// ajouté à la main ou une version en double explique beaucoup de crashs) et la fin du log du
/// jeu (logs/latest.log), là où se trouve l'erreur quand c'est Minecraft qui plante, pas le
/// launcher.
/// </summary>
public static class ProblemReportBuilder
{
    internal const int GameLogLineCount = 50;
    internal const int LauncherLogLineCount = 30;

    public static async Task<string> BuildAsync(ProblemReportInfo info)
    {
        var lines = new List<string>
        {
            "=== Rapport Omnéria Games ===",
            $"Version launcher : {info.LauncherVersion}",
            $"OS : {Environment.OSVersion.VersionString}",
            $"RAM du PC : {(info.PhysicalMemoryMb is int total ? $"{total / 1024.0:0.#} Go" : "inconnue")}",
            $"RAM configurée : {info.MinRamMb}-{info.MaxRamMb} Mo",
            $"Serveur : {info.ServerAddress}",
            $"Dernière synchro modpack : {(info.LastSyncedAt is null ? "jamais" : info.LastSyncedAt.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm"))}",
            "",
        };

        lines.AddRange(DescribeMods(Path.Combine(info.GameDirectory, "mods")));
        lines.Add("");

        lines.Add($"--- Dernières lignes du jeu (logs/latest.log) ---");
        lines.AddRange(await ReadLastLinesAsync(Path.Combine(info.GameDirectory, "logs", "latest.log"), GameLogLineCount, "logs/latest.log"));
        lines.Add("");

        lines.Add("--- Dernières lignes de launcher.log ---");
        lines.AddRange(await ReadLastLinesAsync(info.LauncherLogPath, LauncherLogLineCount, "launcher.log"));

        return HideUserProfilePath(string.Join(Environment.NewLine, lines));
    }

    private static List<string> DescribeMods(string modsDirectory)
    {
        if (!Directory.Exists(modsDirectory))
        {
            return ["--- Mods installés : dossier mods/ absent ---"];
        }

        var mods = Directory.EnumerateFiles(modsDirectory, "*.jar")
            .Select(Path.GetFileName)
            .OfType<string>()
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var lines = new List<string> { $"--- Mods installés ({mods.Count}) ---" };
        lines.AddRange(mods);
        return lines;
    }

    /// <summary>
    /// Fin d'un fichier texte, lue en partage : le jeu (ou le launcher lui-même) peut être en train
    /// d'y écrire, et File.ReadAllLines échouait alors avec "fichier utilisé par un autre processus".
    /// </summary>
    internal static async Task<List<string>> ReadLastLinesAsync(string path, int count, string label)
    {
        if (!File.Exists(path))
        {
            return [$"(aucun {label} pour l'instant)"];
        }

        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var tail = new Queue<string>(count);
            while (await reader.ReadLineAsync() is { } line)
            {
                if (tail.Count == count)
                {
                    tail.Dequeue();
                }

                tail.Enqueue(line);
            }

            return [.. tail];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [$"({label} illisible : {ex.Message})"];
        }
    }

    /// <summary>
    /// Les logs contiennent des chemins complets (C:\Users\Prénom\...) : le rapport part sur un
    /// Discord partagé, le nom de session Windows du joueur n'a rien à y faire.
    /// </summary>
    internal static string HideUserProfilePath(string text)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrEmpty(profile) ? text : text.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Ce que le rapport doit connaître des réglages et de l'état du launcher.</summary>
public sealed record ProblemReportInfo(
    string LauncherVersion,
    int? PhysicalMemoryMb,
    int MinRamMb,
    int MaxRamMb,
    string ServerAddress,
    DateTimeOffset? LastSyncedAt,
    string GameDirectory,
    string LauncherLogPath);
