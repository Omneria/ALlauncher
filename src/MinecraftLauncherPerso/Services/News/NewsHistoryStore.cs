using System.IO;
using System.Text.Json;

namespace MinecraftLauncherPerso.Services.News;

/// <summary>Une actu telle qu'elle est apparue dans news.txt à un instant donné.</summary>
public sealed record NewsHistoryEntry(DateTimeOffset FetchedAt, string Content);

/// <summary>
/// news.txt (voir NewsService) est un simple fichier texte "à plat" : le VPS ne garde aucun
/// historique, chaque requête ne renvoie que le contenu actuel. Pour afficher un historique côté
/// launcher malgré tout, chaque contenu distinct observé est horodaté et conservé localement
/// (%AppData%/MinecraftLauncherPerso/news-history.json), plafonné pour ne pas grossir indéfiniment.
/// </summary>
public sealed class NewsHistoryStore
{
    private const int MaxEntries = 20;

    private readonly string _filePath;

    public NewsHistoryStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MinecraftLauncherPerso", "news-history.json");
    }

    public List<NewsHistoryEntry> Load()
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<List<NewsHistoryEntry>>(json) ?? [];
        }
        catch (Exception)
        {
            // Historique corrompu : pas critique (juste un confort d'affichage), on repart à vide
            // plutôt que de faire planter le launcher pour ça.
            return [];
        }
    }

    /// <summary>
    /// Ajoute <paramref name="content"/> en tête de l'historique s'il diffère du plus récent connu
    /// (pas de doublon consécutif à chaque simple re-vérification du fichier), et persiste.
    /// Retourne l'historique à jour (que le contenu ait changé ou non).
    /// </summary>
    public List<NewsHistoryEntry> RecordIfNew(List<NewsHistoryEntry> history, string content)
    {
        if (history.Count > 0 && history[0].Content == content)
        {
            return history;
        }

        var updated = new List<NewsHistoryEntry> { new(DateTimeOffset.Now, content) };
        updated.AddRange(history);
        if (updated.Count > MaxEntries)
        {
            updated.RemoveRange(MaxEntries, updated.Count - MaxEntries);
        }

        Save(updated);
        return updated;
    }

    private void Save(List<NewsHistoryEntry> history)
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_filePath, JsonSerializer.Serialize(history));
        }
        catch (Exception)
        {
            // Best-effort : l'historique n'est qu'un confort d'affichage, pas une donnée critique.
        }
    }
}
