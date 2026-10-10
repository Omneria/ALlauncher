namespace MinecraftLauncherPerso.Services.Status;

/// <summary>Gravité affichée par une pastille de statut (v2.0.0).</summary>
public enum StatusKind
{
    Neutral,
    Online,
    Warning,
    Offline,
}

/// <summary>Ce que l'interface affiche pour l'état du serveur : pastille, libellé et infobulle.</summary>
public sealed record ServerStatusPresentation(StatusKind Kind, string Text, string? Tooltip)
{
    /// <summary>
    /// Version du serveur comparée à celle que le launcher installe (v1.12.0) : un serveur passé à
    /// une autre version de Minecraft refuserait la connexion avec un message peu clair.
    /// </summary>
    public static ServerStatusPresentation From(ServerStatus status, string minecraftVersion)
    {
        if (!status.IsOnline)
        {
            return new ServerStatusPresentation(StatusKind.Offline, "HORS LIGNE", status.ErrorDetail);
        }

        var expectedProtocol = ServerStatusService.ExpectedProtocol(minecraftVersion);
        if (expectedProtocol is not null && status.ProtocolVersion is not null && status.ProtocolVersion != expectedProtocol)
        {
            return new ServerStatusPresentation(
                StatusKind.Warning,
                "EN LIGNE · VERSION DIFFÉRENTE",
                $"Le serveur annonce {status.VersionName ?? "une autre version"} (protocole {status.ProtocolVersion}), le launcher installe Minecraft {minecraftVersion} : la connexion risque d'être refusée.");
        }

        return new ServerStatusPresentation(
            StatusKind.Online,
            status.LatencyMs is { } latency ? $"EN LIGNE · {latency} ms" : "EN LIGNE",
            null);
    }
}
