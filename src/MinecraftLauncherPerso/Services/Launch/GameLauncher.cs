using System.Diagnostics;
using System.Text;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.ProcessBuilder;
using MinecraftLauncherPerso.Services.Auth;

namespace MinecraftLauncherPerso.Services.Launch;

public sealed class GameLauncher : IGameLauncher
{
    public async Task<ProcessWrapper> LaunchAsync(
        MinecraftLauncher launcher,
        string versionId,
        MinecraftSession session,
        string javaExecutablePath,
        int minRamMb,
        int maxRamMb,
        string? serverIp = null,
        int serverPort = 25565,
        IProgress<string>? gameOutput = null,
        CancellationToken cancellationToken = default)
    {
        var mSession = new MSession(session.Username, session.AccessToken, session.Uuid);

        var process = await launcher.BuildProcessAsync(versionId, new MLaunchOption
        {
            Session = mSession,
            JavaPath = javaExecutablePath,
            MinimumRamMb = minRamMb,
            MaximumRamMb = maxRamMb,
            ServerIp = serverIp,
            ServerPort = serverPort,
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

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        return processWrapper;
    }
}
