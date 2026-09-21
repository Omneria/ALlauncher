using System.Runtime.CompilerServices;

// Donne au projet de tests l'accès aux membres "internal" exposés uniquement pour être testables
// (encodage VarInt du ping serveur, parsing de version, écriture NBT...) sans les rendre publics
// pour autant — voir tests/MinecraftLauncherPerso.Tests.
[assembly: InternalsVisibleTo("MinecraftLauncherPerso.Tests")]
