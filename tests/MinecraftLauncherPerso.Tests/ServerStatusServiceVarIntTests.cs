using MinecraftLauncherPerso.Services.Status;
using Xunit;

namespace MinecraftLauncherPerso.Tests;

/// <summary>
/// L'encodage VarInt (protocole Server List Ping de Minecraft) était jusqu'ici entièrement non
/// testé : un bug d'encodage/décodage silencieux y aurait simplement fait échouer tous les pings
/// serveur, indiscernable en pratique d'un serveur réellement hors ligne (voir le catch générique
/// dans ServerStatusService.PingAsync).
/// </summary>
public sealed class ServerStatusServiceVarIntTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(127)] // tient sur un seul octet (dernière valeur avant le bit de continuation)
    [InlineData(128)] // première valeur nécessitant deux octets
    [InlineData(300)]
    [InlineData(25565)] // port par défaut d'un serveur Minecraft
    [InlineData(2097151)] // dernière valeur sur 3 octets
    [InlineData(int.MaxValue)]
    public async Task WriteVarInt_puis_ReadVarIntAsync_redonne_la_valeur_dorigine(int value)
    {
        using var stream = new MemoryStream();
        ServerStatusService.WriteVarInt(stream, value);
        stream.Position = 0;

        var result = await ServerStatusService.ReadVarIntAsync(stream, CancellationToken.None);

        Assert.Equal(value, result);
    }

    [Fact]
    public void WriteVarInt_zero_tient_sur_un_seul_octet()
    {
        using var stream = new MemoryStream();
        ServerStatusService.WriteVarInt(stream, 0);

        Assert.Equal(new byte[] { 0x00 }, stream.ToArray());
    }

    [Fact]
    public void WriteVarInt_25565_encode_sur_trois_octets_avec_bit_de_continuation()
    {
        // Valeur utilisée dans le handshake du ping serveur pour indiquer le port par défaut :
        // vérifie l'encodage exact octet par octet, pas juste le round-trip ci-dessus, pour
        // détecter un décalage de bits qui produirait par coïncidence la même valeur au décodage.
        using var stream = new MemoryStream();
        ServerStatusService.WriteVarInt(stream, 25565);

        Assert.Equal(new byte[] { 0xDD, 0xC7, 0x01 }, stream.ToArray());
    }

    [Fact]
    public async Task ReadVarIntAsync_leve_si_le_flux_se_termine_en_plein_milieu()
    {
        // Octet avec le bit de continuation posé (0x80), mais rien derrière : simule une connexion
        // coupée en plein milieu de la lecture d'un VarInt.
        using var stream = new MemoryStream([0x80]);

        await Assert.ThrowsAsync<EndOfStreamException>(
            () => ServerStatusService.ReadVarIntAsync(stream, CancellationToken.None));
    }
}
