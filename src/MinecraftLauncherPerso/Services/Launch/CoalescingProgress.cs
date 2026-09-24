namespace MinecraftLauncherPerso.Services.Launch;

/// <summary>
/// IProgress qui ne garde que la DERNIÈRE valeur rapportée, appliquée seulement quand l'appelant
/// le décide (<see cref="Flush"/>, typiquement depuis un DispatcherTimer à ~10 Hz).
///
/// Pourquoi : pendant un téléchargement, CmlLib rapporte sa progression des milliers de fois par
/// seconde (une fois par fichier et par paquet d'octets). Avec un Progress&lt;T&gt; classique, chaque
/// rapport devient un message posté au thread UI : la file du Dispatcher se remplit plus vite
/// qu'elle ne se vide et le launcher paraît gelé pendant tout le téléchargement (vécu en v1.11.0).
/// Ici, <see cref="Report"/> ne fait qu'un échange atomique, depuis n'importe quel thread ;
/// l'interface ne se met à jour qu'au rythme de l'affichage, avec la valeur la plus récente.
/// </summary>
public sealed class CoalescingProgress<T> : IProgress<T> where T : class
{
    private readonly Action<T> _apply;
    private T? _pending;

    public CoalescingProgress(Action<T> apply)
    {
        _apply = apply;
    }

    /// <summary>Thread-safe, sans allocation ni verrou : remplace la valeur en attente.</summary>
    public void Report(T value) => Interlocked.Exchange(ref _pending, value);

    /// <summary>Applique la dernière valeur rapportée depuis le flush précédent, s'il y en a une.
    /// À appeler sur le thread qui possède l'interface.</summary>
    public void Flush()
    {
        var value = Interlocked.Exchange(ref _pending, null);
        if (value is not null)
        {
            _apply(value);
        }
    }
}
