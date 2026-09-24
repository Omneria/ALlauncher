using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace MinecraftLauncherPerso.Services.Update;

/// <summary>
/// Signature Authenticode de l'exécutable (v1.11.0). Le fichier <c>.sha256</c> joint à chaque
/// release ne prouve que l'intégrité du transfert : il est servi par la même origine que l'exe,
/// donc un compte GitHub compromis publierait un exe *et* son empreinte cohérente. Une signature
/// Authenticode par un certificat de signature de code émis par une autorité reconnue
/// (voir le job "release" du workflow) prouve en plus *qui* a produit le fichier.
///
/// Politique côté launcher, volontairement conservatrice pour ne jamais bloquer un joueur :
/// l'exigence ne s'applique que si l'exe *courant* porte lui-même une signature valide (chaîne
/// jusqu'à une racine de confiance Windows). Dans ce cas, l'exe téléchargé doit porter une
/// signature valide du même éditeur (même sujet de certificat — pas la même empreinte, qui change
/// à chaque renouvellement du certificat). Un exe courant non signé, ou signé par un certificat
/// non reconnu (auto-signé), n'impose rien : on reste sur le contrôle SHA-256 seul.
/// </summary>
public static class AuthenticodeVerifier
{
    /// <summary>Sujet (DN) du certificat qui a signé le fichier, ou null si le fichier n'est pas signé.
    /// N'atteste PAS de la validité de la signature : voir <see cref="HasTrustedSignature"/>.</summary>
    public static string? GetSignerSubject(string filePath)
    {
        try
        {
            using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(filePath));
            return certificate.Subject;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    /// <summary>
    /// Vrai si la signature Authenticode du fichier est valide et remonte à une racine de confiance
    /// de la machine (WinVerifyTrust, la même vérification que l'explorateur Windows / SmartScreen).
    /// Faux pour un fichier non signé, altéré après signature, ou signé par un certificat non
    /// reconnu (auto-signé).
    /// </summary>
    public static bool HasTrustedSignature(string filePath)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(filePath))
        {
            return false;
        }

        var fileInfo = new WINTRUST_FILE_INFO
        {
            cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
            pcwszFilePath = Path.GetFullPath(filePath),
            hFile = IntPtr.Zero,
            pgKnownSubject = IntPtr.Zero,
        };

        var fileInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        try
        {
            Marshal.StructureToPtr(fileInfo, fileInfoPtr, fDeleteOld: false);

            var data = new WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                dwUIChoice = WTD_UI_NONE,
                fdwRevocationChecks = WTD_REVOKE_NONE,
                dwUnionChoice = WTD_CHOICE_FILE,
                pFile = fileInfoPtr,
                dwStateAction = WTD_STATEACTION_VERIFY,
                // Pas d'accès réseau pour la révocation : une vérification qui dépend d'un CRL
                // distant échouerait hors ligne, précisément quand on ne veut pas bloquer.
                dwProvFlags = WTD_CACHE_ONLY_URL_RETRIEVAL,
            };

            var actionId = WINTRUST_ACTION_GENERIC_VERIFY_V2;
            var result = WinVerifyTrust(IntPtr.Zero, actionId, ref data);

            // Libère l'état interne alloué par l'appel VERIFY, quel que soit le résultat.
            data.dwStateAction = WTD_STATEACTION_CLOSE;
            WinVerifyTrust(IntPtr.Zero, actionId, ref data);

            return result == 0;
        }
        finally
        {
            Marshal.DestroyStructure<WINTRUST_FILE_INFO>(fileInfoPtr);
            Marshal.FreeHGlobal(fileInfoPtr);
        }
    }

    private static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private const uint WTD_UI_NONE = 2;
    private const uint WTD_REVOKE_NONE = 0;
    private const uint WTD_CHOICE_FILE = 1;
    private const uint WTD_STATEACTION_VERIFY = 1;
    private const uint WTD_STATEACTION_CLOSE = 2;
    private const uint WTD_CACHE_ONLY_URL_RETRIEVAL = 0x00001000;

    [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID, ref WINTRUST_DATA pWVTData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }
}
