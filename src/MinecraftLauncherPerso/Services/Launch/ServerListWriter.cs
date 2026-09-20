using System.Text;

namespace MinecraftLauncherPerso.Services.Launch;

/// <summary>
/// Écrit servers.dat (NBT non compressé, format vanilla lu par l'écran multijoueur) avec un seul
/// serveur. Appelé à chaque lancement pour réinitialiser la liste : un joueur qui aurait ajouté
/// manuellement un autre serveur dans le jeu le retrouve retiré au lancement suivant. N'empêche
/// pas d'ajouter un autre serveur *pendant* la session en cours (ce n'est pas un mod côté client,
/// juste une remise à zéro du fichier avant chaque démarrage).
/// </summary>
public static class ServerListWriter
{
    private const byte TagEnd = 0;
    private const byte TagByte = 1;
    private const byte TagString = 8;
    private const byte TagList = 9;
    private const byte TagCompound = 10;

    public static void WriteSingleServer(string gameDirectory, string serverName, string serverAddress)
    {
        Directory.CreateDirectory(gameDirectory);
        var path = Path.Combine(gameDirectory, "servers.dat");

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream);

        WriteTagHeader(writer, TagCompound, ""); // racine

        WriteTagHeader(writer, TagList, "servers");
        writer.Write(TagCompound); // type des éléments de la liste
        WriteInt32BigEndian(writer, 1); // un seul élément

        // Élément de la liste (compound sans nom/type, juste son contenu).
        WriteTagHeader(writer, TagString, "name");
        WriteNbtString(writer, serverName);
        WriteTagHeader(writer, TagString, "ip");
        WriteNbtString(writer, serverAddress);
        WriteTagHeader(writer, TagByte, "acceptTextures");
        writer.Write((byte)1);
        writer.Write(TagEnd); // fin du compound de l'élément

        writer.Write(TagEnd); // fin du compound racine
    }

    private static void WriteTagHeader(BinaryWriter writer, byte tagType, string name)
    {
        writer.Write(tagType);
        WriteNbtString(writer, name);
    }

    private static void WriteNbtString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteUInt16BigEndian(writer, (ushort)bytes.Length);
        writer.Write(bytes);
    }

    private static void WriteUInt16BigEndian(BinaryWriter writer, ushort value)
    {
        writer.Write((byte)(value >> 8));
        writer.Write((byte)value);
    }

    private static void WriteInt32BigEndian(BinaryWriter writer, int value)
    {
        writer.Write((byte)(value >> 24));
        writer.Write((byte)(value >> 16));
        writer.Write((byte)(value >> 8));
        writer.Write((byte)value);
    }
}
