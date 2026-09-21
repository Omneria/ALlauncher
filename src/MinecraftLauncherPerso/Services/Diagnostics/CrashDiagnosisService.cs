namespace MinecraftLauncherPerso.Services.Diagnostics;

/// <summary>
/// Diagnostic best-effort d'une sortie anormale du jeu (code de sortie non nul), à partir de la
/// sortie console (stdout/stderr) déjà capturée par GameLauncher/MainWindow. Volontairement limité
/// à quelques motifs connus et fréquents sur CE modpack précis (RAM insuffisante, version Java
/// incompatible, fichier de mod corrompu/manquant) plutôt qu'un moteur générique : contrairement à
/// un launcher multi-modpack, on peut se permettre de cibler les causes réellement rencontrées ici
/// au lieu d'essayer de couvrir n'importe quelle erreur de n'importe quel mod.
/// </summary>
public static class CrashDiagnosisService
{
    public sealed record CrashDiagnosis(string Message, bool SuggestsRepair);

    /// <summary>
    /// Cherche parmi les dernières lignes de sortie du jeu (voir MainWindow, tampon borné alimenté
    /// par IGameLauncher.LaunchAsync) un motif connu. Retourne null si rien de reconnu : dans ce
    /// cas, le message d'erreur brut (code de sortie) reste le seul affiché, plutôt que d'inventer
    /// une explication non fiable.
    /// </summary>
    public static CrashDiagnosis? Diagnose(IEnumerable<string> outputLines)
    {
        var lines = outputLines as IReadOnlyCollection<string> ?? outputLines.ToList();

        if (lines.Any(l => l.Contains("OutOfMemoryError", StringComparison.OrdinalIgnoreCase)))
        {
            return new CrashDiagnosis(
                "Le jeu a manqué de mémoire (OutOfMemoryError). Essayez d'augmenter la RAM maximum " +
                "dans Paramètres.", SuggestsRepair: false);
        }

        if (lines.Any(l => l.Contains("UnsupportedClassVersionError", StringComparison.OrdinalIgnoreCase)
                         || l.Contains("has been compiled by a more recent version", StringComparison.OrdinalIgnoreCase)))
        {
            return new CrashDiagnosis(
                "Version de Java incompatible détectée. Ce modpack nécessite Java 8 — si une autre " +
                "version est forcée via JAVA_HOME, elle peut prendre le pas sur le Java 8 du launcher.",
                SuggestsRepair: false);
        }

        if (lines.Any(l => l.Contains("A fatal error has been detected by the Java Runtime Environment", StringComparison.OrdinalIgnoreCase))
                 && lines.Any(l => l.Contains(".dll", StringComparison.OrdinalIgnoreCase) || l.Contains(".so", StringComparison.OrdinalIgnoreCase)))
        {
            return new CrashDiagnosis(
                "Crash natif détecté (bibliothèque native manquante ou corrompue, ex. LWJGL). Une " +
                "réparation du modpack peut résoudre le problème si un fichier a été altéré.",
                SuggestsRepair: true);
        }

        if (lines.Any(l => l.Contains("Missing or unsupported mandatory dependencies", StringComparison.OrdinalIgnoreCase)
                         || l.Contains("MissingModsException", StringComparison.OrdinalIgnoreCase)
                         || (l.Contains("mods", StringComparison.OrdinalIgnoreCase) && l.Contains("could not be loaded", StringComparison.OrdinalIgnoreCase))))
        {
            return new CrashDiagnosis(
                "Un ou plusieurs mods n'ont pas pu être chargés (fichier manquant ou corrompu). " +
                "Une réparation du modpack (Paramètres) devrait résoudre le problème.",
                SuggestsRepair: true);
        }

        if (lines.Any(l => l.Contains("ZipException", StringComparison.OrdinalIgnoreCase)
                         || l.Contains("zip END header not found", StringComparison.OrdinalIgnoreCase)))
        {
            return new CrashDiagnosis(
                "Un fichier .jar semble corrompu (archive illisible). Une réparation du modpack " +
                "(Paramètres) devrait le retélécharger proprement.",
                SuggestsRepair: true);
        }

        return null;
    }
}
