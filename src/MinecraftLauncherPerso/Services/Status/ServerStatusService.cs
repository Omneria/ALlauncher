using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MinecraftLauncherPerso.Services.Status;

/// <summary>
/// Implémente le protocole Server List Ping de Minecraft (celui que l'écran multijoueur du jeu
/// utilise pour afficher joueurs connectés/latence à côté de chaque serveur de la liste) : un
/// handshake suivi d'une requête status sur une connexion TCP brute, sans authentification.
/// </summary>
public sealed partial class ServerStatusService : IServerStatusService
{
    // 4s était trop court pour une adresse qui route parfois via un VPN (latence de handshake plus
    // élevée que sur une connexion directe) : un ping qui timeout à tort affichait "hors ligne" à
    // un serveur en réalité joignable.
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan PingTimeout = TimeSpan.FromSeconds(3);

    public async Task<ServerStatus> PingAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(Timeout);

            using var client = new TcpClient();
            await client.ConnectAsync(host, port, timeoutCts.Token);

            using var stream = client.GetStream();

            await WriteHandshakeAsync(stream, host, port, timeoutCts.Token);
            await WriteStatusRequestAsync(stream, timeoutCts.Token);

            var json = await ReadStatusResponseAsync(stream, timeoutCts.Token);
            var latencyMs = await MeasureLatencyAsync(stream, timeoutCts.Token);
            return ParseStatus(json, latencyMs);
        }
        catch (Exception ex)
        {
            // Hors ligne, port fermé, hôte introuvable... : l'UI n'a besoin que d'un oui/non pour
            // le statut, mais le détail (DNS, connexion refusée, timeout) reste utile pour
            // diagnostiquer un faux "hors ligne" (ex. adresse qui ne route que via un VPN précis) :
            // exposé en tooltip par MainWindow plutôt que juste avalé.
            var detail = ex is OperationCanceledException
                ? $"Timeout après {Timeout.TotalSeconds:0}s en contactant {host}:{port}."
                : $"{ex.GetType().Name} en contactant {host}:{port} : {ex.Message}";
            return new ServerStatus(false, 0, 0, detail);
        }
    }

    private static async Task WriteHandshakeAsync(NetworkStream stream, string host, int port, CancellationToken cancellationToken)
    {
        using var payload = new MemoryStream();
        WriteVarInt(payload, 0x00); // packet id
        WriteVarInt(payload, 47); // protocol version : sans importance pour une requête status
        WriteString(payload, host);
        WriteUShortBigEndian(payload, (ushort)port);
        WriteVarInt(payload, 1); // next state = status

        await WritePacketAsync(stream, payload.ToArray(), cancellationToken);
    }

    private static Task WriteStatusRequestAsync(NetworkStream stream, CancellationToken cancellationToken) =>
        WritePacketAsync(stream, [0x00], cancellationToken);

    private static async Task WritePacketAsync(NetworkStream stream, byte[] payload, CancellationToken cancellationToken)
    {
        using var packet = new MemoryStream();
        WriteVarInt(packet, payload.Length);
        packet.Write(payload, 0, payload.Length);

        await stream.WriteAsync(packet.ToArray(), cancellationToken);
    }

    private static async Task<string> ReadStatusResponseAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        await ReadVarIntAsync(stream, cancellationToken); // longueur totale du paquet, pas nécessaire ici
        var packetId = await ReadVarIntAsync(stream, cancellationToken);
        if (packetId != 0x00)
        {
            throw new InvalidOperationException($"Paquet inattendu (id={packetId}) en réponse au status request.");
        }

        var jsonLength = await ReadVarIntAsync(stream, cancellationToken);
        var buffer = new byte[jsonLength];
        var totalRead = 0;
        while (totalRead < jsonLength)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(totalRead, jsonLength - totalRead), cancellationToken);
            if (read == 0)
            {
                throw new EndOfStreamException("Connexion fermée avant la fin de la réponse status.");
            }

            totalRead += read;
        }

        return Encoding.UTF8.GetString(buffer);
    }

    /// <summary>
    /// Paquet ping/pong du protocole (celui qu'utilise la liste multijoueur du jeu pour afficher la
    /// latence) : vrai aller-retour jusqu'au serveur Minecraft. Null si le serveur ne répond pas au
    /// ping (certains ferment la connexion après le status) : la latence reste alors inconnue,
    /// le serveur n'en est pas moins en ligne.
    /// </summary>
    private static async Task<int?> MeasureLatencyAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        // Délai propre au ping : un serveur qui ne répond pas au pong ne doit pas épuiser le délai
        // global et passer pour "hors ligne".
        using var pingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        pingCts.CancelAfter(PingTimeout);
        try
        {
            using var payload = new MemoryStream();
            WriteVarInt(payload, 0x01); // packet id : ping
            payload.Write(BitConverter.GetBytes(Environment.TickCount64)); // valeur libre, renvoyée telle quelle

            var stopwatch = Stopwatch.StartNew();
            await WritePacketAsync(stream, payload.ToArray(), pingCts.Token);
            await ReadVarIntAsync(stream, pingCts.Token); // longueur
            var packetId = await ReadVarIntAsync(stream, pingCts.Token);
            stopwatch.Stop();

            return packetId == 0x01 ? (int)Math.Max(1, stopwatch.ElapsedMilliseconds) : null;
        }
        catch (Exception ex) when (ex is IOException or SocketException or InvalidOperationException
            || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return null;
        }
    }

    // internal : testé directement (voir InternalsVisibleTo), sans vrai serveur.
    internal static ServerStatus ParseStatus(string json, int? latencyMs = null)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var players = root.GetProperty("players");
        var online = players.GetProperty("online").GetInt32();
        var max = players.GetProperty("max").GetInt32();

        int? protocol = null;
        string? versionName = null;
        if (root.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.Object)
        {
            if (version.TryGetProperty("protocol", out var protocolElement) && protocolElement.TryGetInt32(out var value))
            {
                protocol = value;
            }

            if (version.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String)
            {
                versionName = nameElement.GetString();
            }
        }

        var motd = root.TryGetProperty("description", out var description) ? FlattenChat(description) : "";
        motd = FormattingCodePattern().Replace(motd, "").Trim();

        // Échantillon de pseudos que le serveur accepte de révéler (au plus une douzaine, parfois masqué).
        List<string>? names = null;
        if (players.TryGetProperty("sample", out var sample) && sample.ValueKind == JsonValueKind.Array)
        {
            names = sample.EnumerateArray()
                .Select(entry => entry.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() : null)
                .OfType<string>()
                .Where(name => name.Length > 0)
                .ToList();
        }

        return new ServerStatus(true, online, max,
            LatencyMs: latencyMs,
            ProtocolVersion: protocol,
            VersionName: versionName,
            Motd: motd.Length > 0 ? motd : null,
            PlayerNames: names);
    }

    /// <summary>MOTD : texte simple, ou composant de chat JSON ({"text": ..., "extra": [...]}).</summary>
    private static string FlattenChat(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? "",
        JsonValueKind.Array => string.Concat(element.EnumerateArray().Select(FlattenChat)),
        JsonValueKind.Object =>
            (element.TryGetProperty("text", out var text) ? FlattenChat(text) : "")
            + (element.TryGetProperty("extra", out var extra) ? FlattenChat(extra) : ""),
        _ => "",
    };

    /// <summary>Codes de mise en forme "§a", "§l"... du MOTD, sans signification hors du jeu.</summary>
    [GeneratedRegex("§.")]
    private static partial Regex FormattingCodePattern();

    /// <summary>
    /// Numéro de protocole réseau d'une version de Minecraft, pour vérifier que le serveur tourne
    /// bien la version que le launcher installe. Null pour une version inconnue ici (rien n'est
    /// alors signalé plutôt qu'une fausse alerte).
    /// </summary>
    public static int? ExpectedProtocol(string minecraftVersion) => minecraftVersion switch
    {
        "1.20" or "1.20.1" => 763,
        _ => null,
    };

    // internal (au lieu de private) + accepte Stream plutôt que NetworkStream : uniquement pour
    // que MinecraftLauncherPerso.Tests puisse vérifier l'encodage/décodage VarInt (voir
    // InternalsVisibleTo dans le csproj) contre un MemoryStream, sans avoir besoin d'une vraie
    // connexion TCP. Comportement de production inchangé (NetworkStream est un Stream).
    internal static void WriteVarInt(Stream stream, int value)
    {
        var unsigned = (uint)value;
        do
        {
            var b = (byte)(unsigned & 0x7F);
            unsigned >>= 7;
            if (unsigned != 0)
            {
                b |= 0x80;
            }

            stream.WriteByte(b);
        } while (unsigned != 0);
    }

    private static void WriteString(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteVarInt(stream, bytes.Length);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static void WriteUShortBigEndian(Stream stream, ushort value)
    {
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }

    internal static async Task<int> ReadVarIntAsync(Stream stream, CancellationToken cancellationToken)
    {
        var result = 0;
        var shift = 0;
        var buffer = new byte[1];

        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, 1), cancellationToken);
            if (read == 0)
            {
                throw new EndOfStreamException("Connexion fermée pendant la lecture d'un VarInt.");
            }

            var b = buffer[0];
            result |= (b & 0x7F) << shift;

            if ((b & 0x80) == 0)
            {
                break;
            }

            shift += 7;
            if (shift >= 32)
            {
                throw new InvalidOperationException("VarInt trop long.");
            }
        }

        return result;
    }
}
