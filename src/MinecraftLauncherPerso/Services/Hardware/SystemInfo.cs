using System.Runtime.InteropServices;

namespace MinecraftLauncherPerso.Services.Hardware;

/// <summary>
/// Informations matérielles utilisées pour calculer une RAM par défaut adaptée à la machine du
/// joueur (voir <see cref="Models.LauncherSettings"/>), au lieu d'une valeur fixe identique pour
/// tout le monde — même principe que la plupart des launchers tiers (ex. Helios/Paladium calculent
/// leur RAM recommandée à partir de la RAM totale du système).
/// </summary>
public static class SystemInfo
{
    /// <summary>
    /// Retourne la RAM physique totale de la machine en Mo, ou null si elle n'a pas pu être
    /// déterminée (l'appelant doit alors se rabattre sur une valeur par défaut fixe).
    /// </summary>
    public static int? GetTotalPhysicalMemoryMb()
    {
        var status = new MEMORYSTATUSEX();
        status.dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();

        if (!GlobalMemoryStatusEx(ref status))
        {
            return null;
        }

        return (int)(status.ullTotalPhys / 1024 / 1024);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }
}
