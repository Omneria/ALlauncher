using System.Reflection;

namespace MinecraftLauncherPerso.Services.Update;

/// <summary>
/// Version du launcher en cours d'exécution. Depuis la v1.12.0, un exe construit hors tag (CI sur
/// dev ou une branche de travail) porte "-dev.&lt;numéro de build&gt;" (voir DevBuildNumber dans le
/// csproj) : on distingue ainsi un exe de test de la release du même numéro, et la mise à jour
/// automatique propose la vraie release à qui teste une préversion.
/// </summary>
public static class AppVersion
{
    private static readonly Assembly Assembly = typeof(AppVersion).Assembly;

    /// <summary>Numéro de version (Major.Minor.Build), identique pour un build de test et la
    /// release correspondante.</summary>
    public static Version Current { get; } = Assembly.GetName().Version ?? new Version(0, 0, 0);

    /// <summary>Vrai pour un exe construit hors tag.</summary>
    public static bool IsDevBuild { get; } = IsDevLabel(InformationalVersion());

    /// <summary>Texte affiché : "1.12.0" pour une release, "1.12.0-dev.153" pour un build de test.</summary>
    public static string Display { get; } = DisplayLabel(InformationalVersion(), Current);

    private static string? InformationalVersion() =>
        Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    // internal : testés directement (voir InternalsVisibleTo).
    internal static bool IsDevLabel(string? informationalVersion) =>
        StripMetadata(informationalVersion).Contains("-dev", StringComparison.OrdinalIgnoreCase);

    internal static string DisplayLabel(string? informationalVersion, Version version)
    {
        // Le SDK ajoute "+<sha du commit>" à InformationalVersion : utile pour un diagnostic, pas
        // pour l'affichage.
        var label = StripMetadata(informationalVersion);
        return label.Length > 0 ? label : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    /// <summary>
    /// Vrai si une release <paramref name="remote"/> doit être proposée : plus récente, ou de même
    /// numéro quand on exécute un build de test (la release finale remplace sa préversion).
    /// </summary>
    internal static bool IsUpdate(Version remote, Version current, bool currentIsDevBuild) =>
        remote > current || (currentIsDevBuild && remote == current);

    private static string StripMetadata(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return "";
        }

        var plus = informationalVersion.IndexOf('+');
        return (plus >= 0 ? informationalVersion[..plus] : informationalVersion).Trim();
    }
}
