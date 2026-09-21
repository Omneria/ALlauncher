using System.Text;
using MinecraftLauncherPerso.Services.Launch;
using Xunit;

namespace MinecraftLauncherPerso.Tests;

/// <summary>
/// ServerListWriter écrit servers.dat à la main (NBT non compressé, tags/entiers big-endian, sans
/// bibliothèque NBT) : auparavant aucun test ne couvrait cet encodage, un bug d'off-by-one ou
/// d'endianness y aurait simplement produit un servers.dat corrompu, découvert seulement en jeu
/// (liste multijoueur vide/cassée) sans lien évident avec sa cause.
///
/// Plutôt que de dépendre d'une bibliothèque NBT externe côté tests, on décode ici "à la main" la
/// structure fixe et connue que WriteSingleServer produit (root compound -> liste "servers" à un
/// élément -> name/ip/acceptTextures) : suffisant pour vérifier l'encodage réel sans réimplémenter
/// un lecteur NBT générique.
/// </summary>
public sealed class ServerListWriterTests
{
    [Theory]
    [InlineData("Astral Nexus", "astralnexusmc.duckdns.org")]
    [InlineData("Éàçüñ Serveur", "127.0.0.1")] // caractères non-ASCII : vérifie le longueur-préfixe UTF-8, pas le nombre de caractères .NET
    [InlineData("", "")]
    public void WriteSingleServer_produit_un_NBT_qui_redonne_le_nom_et_ladresse(string serverName, string serverAddress)
    {
        using var stream = new MemoryStream();

        ServerListWriter.WriteSingleServer(stream, serverName, serverAddress);

        stream.Position = 0;
        using var reader = new BinaryReader(stream);

        // Compound racine.
        Assert.Equal(10, reader.ReadByte()); // TAG_Compound
        Assert.Equal("", ReadNbtString(reader)); // nom (vide, tag racine anonyme)

        // Tag "servers" : liste.
        Assert.Equal(9, reader.ReadByte()); // TAG_List
        Assert.Equal("servers", ReadNbtString(reader));
        Assert.Equal(10, reader.ReadByte()); // type des éléments : TAG_Compound
        Assert.Equal(1, ReadInt32BigEndian(reader)); // un seul élément

        // Élément de la liste : compound sans nom/type propre, juste son contenu.
        Assert.Equal(8, reader.ReadByte()); // TAG_String
        Assert.Equal("name", ReadNbtString(reader));
        Assert.Equal(serverName, ReadNbtString(reader));

        Assert.Equal(8, reader.ReadByte()); // TAG_String
        Assert.Equal("ip", ReadNbtString(reader));
        Assert.Equal(serverAddress, ReadNbtString(reader));

        Assert.Equal(1, reader.ReadByte()); // TAG_Byte
        Assert.Equal("acceptTextures", ReadNbtString(reader));
        Assert.Equal(1, reader.ReadByte());

        Assert.Equal(0, reader.ReadByte()); // TAG_End de l'élément
        Assert.Equal(0, reader.ReadByte()); // TAG_End de la racine
        Assert.Equal(stream.Length, stream.Position); // rien après : pas d'octet en trop écrit
    }

    private static string ReadNbtString(BinaryReader reader)
    {
        var length = ReadUInt16BigEndian(reader);
        var bytes = reader.ReadBytes(length);
        return Encoding.UTF8.GetString(bytes);
    }

    private static ushort ReadUInt16BigEndian(BinaryReader reader)
    {
        var high = reader.ReadByte();
        var low = reader.ReadByte();
        return (ushort)((high << 8) | low);
    }

    private static int ReadInt32BigEndian(BinaryReader reader)
    {
        var b0 = reader.ReadByte();
        var b1 = reader.ReadByte();
        var b2 = reader.ReadByte();
        var b3 = reader.ReadByte();
        return (b0 << 24) | (b1 << 16) | (b2 << 8) | b3;
    }
}
