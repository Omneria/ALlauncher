namespace MinecraftLauncherPerso.Services.Formatting;

/// <summary>« il y a 3 h », « à l'instant » : durée écoulée depuis un instant, en français court.</summary>
public static class RelativeTime
{
    public static string Format(DateTimeOffset instant, DateTimeOffset now)
    {
        var elapsed = now - instant;
        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return "à l'instant";
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            return $"il y a {(int)elapsed.TotalMinutes} min";
        }

        if (elapsed < TimeSpan.FromDays(1))
        {
            return $"il y a {(int)elapsed.TotalHours} h";
        }

        return $"il y a {(int)elapsed.TotalDays} j";
    }

    public static string Format(DateTimeOffset instant) => Format(instant, DateTimeOffset.Now);
}
