namespace MinecraftLauncherPerso.Models;

public enum JavaSetupStage
{
    Checking,
    Downloading,
    Extracting,
    Ready,
}

/// <summary>Étape courante rapportée à l'UI pendant <see cref="Services.Java.IJavaManager.EnsureJavaAsync"/>.</summary>
public sealed record JavaSetupProgress(JavaSetupStage Stage, double PercentComplete, string Message);
