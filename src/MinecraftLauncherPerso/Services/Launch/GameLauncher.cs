using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.ProcessBuilder;
using MinecraftLauncherPerso.Models;
using MinecraftLauncherPerso.Services.Auth;

namespace MinecraftLauncherPerso.Services.Launch;

public sealed class GameLauncher : IGameLauncher
{
    public event EventHandler<int>? GameExited;

    public async Task<ProcessWrapper> LaunchAsync(
        MinecraftLauncher launcher,
        string versionId,
        MinecraftSession session,
        string javaExecutablePath,
        LauncherSettings settings,
        IProgress<string>? gameOutput = null,
        CancellationToken cancellationToken = default)
    {
        var process = await launcher.BuildProcessAsync(versionId, new MLaunchOption
        {
            Session = ToMSession(session),
            JavaPath = javaExecutablePath,
            MinimumRamMb = settings.MinRamMb,
            MaximumRamMb = settings.MaxRamMb,
            ServerIp = settings.ServerHost,
            ServerPort = settings.ServerPort,
            ScreenWidth = settings.ScreenWidth,
            ScreenHeight = settings.ScreenHeight,
        });

        var processWrapper = new ProcessWrapper(process);

        // ProcessWrapper.StartWithEvents() (CmlLib.Core) force CreateNoWindow=false, ce qui fait
        // apparaître une fenêtre de console pour java.exe (application console sans parent
        // console attaché) : on démarre le process nous-mêmes avec CreateNoWindow=true au lieu
        // d'appeler StartWithEvents(), en reproduisant sa logique de redirection de sortie.
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.StandardOutputEncoding = Encoding.UTF8;
        process.StartInfo.StandardErrorEncoding = Encoding.UTF8;
        process.EnableRaisingEvents = true;
        process.OutputDataReceived += (_, e) => gameOutput?.Report(e.Data ?? "");
        process.ErrorDataReceived += (_, e) => gameOutput?.Report(e.Data ?? "");
        process.Exited += (_, _) => GameExited?.Invoke(this, process.ExitCode);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        return processWrapper;
    }

    /// <summary>
    /// Session passée au jeu. Sans UserType, CmlLib lance avec "--userType Mojang" (et les textes
    /// "xuid" / "clientId" en guise de valeurs) : Minecraft ne demande alors pas sa clé de
    /// signature de chat à Mojang, et un serveur avec enforce-secure-profile=true répond "Tchat
    /// désactivé à cause de l'absence de la clé publique du profil". "msa" = compte Microsoft,
    /// ce qu'est toujours la session renvoyée par MicrosoftAuthService.
    /// </summary>
    internal static MSession ToMSession(MinecraftSession session) =>
        new(session.Username, session.AccessToken, session.Uuid)
        {
            UserType = "msa",
            Xuid = ReadXuid(session.AccessToken) ?? "0",
        };

    /// <summary>
    /// Le jeton Minecraft (login_with_xbox) est un JWT dont la charge utile contient le xuid du
    /// compte Xbox. Best-effort : le jeu n'en a besoin que pour la télémétrie, d'où "0" à défaut.
    /// </summary>
    internal static string? ReadXuid(string accessToken)
    {
        var parts = accessToken.Split('.');
        if (parts.Length < 2)
        {
            return null;
        }

        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
            using var json = JsonDocument.Parse(Convert.FromBase64String(payload));
            return json.RootElement.TryGetProperty("xuid", out var xuid) && xuid.ValueKind == JsonValueKind.String
                ? xuid.GetString()
                : null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }
    }
}
