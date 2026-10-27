using System.Text;

namespace MinecraftLauncherPerso.Services.Launch;

/// <summary>
/// Arguments JVM supplémentaires saisis dans Paramètres > Jeu (v2.2.0), pour contourner un bug
/// précis sans nouvelle version du launcher (ex. exclure une méthode du JIT après un crash natif).
/// Découpe la saisie à la manière d'une ligne de commande (espaces, guillemets doubles) et refuse
/// ce qui ne peut pas être un argument JVM ou entrerait en conflit avec un réglage du launcher.
/// </summary>
public static class JvmArgumentParser
{
    /// <summary>Longueur maximale de la saisie : largement assez pour quelques options, et borne une saisie absurde.</summary>
    public const int MaxLength = 1000;

    /// <summary>Résultat : soit des arguments, soit un message d'erreur lisible.</summary>
    public sealed record ParseResult(IReadOnlyList<string> Arguments, string? Error)
    {
        public bool IsValid => Error is null;
    }

    public static ParseResult Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new ParseResult([], null);
        }

        if (text.Length > MaxLength)
        {
            return Fail($"Arguments JVM : {MaxLength} caractères maximum.");
        }

        if (text.Any(c => char.IsControl(c) && c is not '\t'))
        {
            return Fail("Arguments JVM : caractères de contrôle interdits (une seule ligne).");
        }

        var tokens = Tokenize(text, out var unbalancedQuote);
        if (unbalancedQuote)
        {
            return Fail("Arguments JVM : guillemet non refermé.");
        }

        foreach (var token in tokens)
        {
            if (!token.StartsWith('-'))
            {
                return Fail($"Arguments JVM : « {token} » doit commencer par « - » (ex. -XX:+UseG1GC).");
            }

            if (token.StartsWith("-Xmx", StringComparison.OrdinalIgnoreCase) || token.StartsWith("-Xms", StringComparison.OrdinalIgnoreCase))
            {
                return Fail($"Arguments JVM : « {token} » est géré par le curseur de RAM ci-dessus.");
            }
        }

        return new ParseResult(tokens, null);
    }

    private static ParseResult Fail(string message) => new([], message);

    private static List<string> Tokenize(string text, out bool unbalancedQuote)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var hasToken = false;

        foreach (var c in text)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                hasToken = true;
            }
            else if (!inQuotes && char.IsWhiteSpace(c))
            {
                if (hasToken)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    hasToken = false;
                }
            }
            else
            {
                current.Append(c);
                hasToken = true;
            }
        }

        if (hasToken)
        {
            tokens.Add(current.ToString());
        }

        unbalancedQuote = inQuotes;
        return tokens;
    }
}
