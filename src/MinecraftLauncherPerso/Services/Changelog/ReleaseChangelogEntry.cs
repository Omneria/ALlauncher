namespace MinecraftLauncherPerso.Services.Changelog;

/// <summary>Une entrée de changelog telle qu'affichée (une par release GitHub non-draft/pré-release).</summary>
public sealed record ReleaseChangelogEntry(
    string Tag,
    string Url,
    DateTimeOffset PublishedAt,
    bool IsLatest,
    IReadOnlyList<string> Notes);
