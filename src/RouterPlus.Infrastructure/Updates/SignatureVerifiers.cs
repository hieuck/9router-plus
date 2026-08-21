using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using RouterPlus.Core.Updates;

namespace RouterPlus.Infrastructure.Updates;

public sealed class WindowsReleaseSignatureVerifier : IUpdateSignatureVerifier
{
    private readonly WindowsAuthenticodeVerifier _authenticodeVerifier = new();
    private readonly PinnedManifestSignatureVerifier _manifestVerifier = new();

    public bool IsAvailable => OperatingSystem.IsWindows();

    public bool VerifyManifest(string manifestPath, ReleaseManifest manifest, string expectedPublisher) =>
        IsAvailable && _manifestVerifier.Verify(manifestPath, manifest, expectedPublisher);

    public bool VerifyExecutable(string executablePath, string expectedPublisher) =>
        IsAvailable && _authenticodeVerifier.Verify(executablePath, expectedPublisher);
}

public sealed class PinnedManifestSignatureVerifier
{
    private const string PublicKeyPem = """
-----BEGIN PUBLIC KEY-----
MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAu3haw0FyvFwamjqjhVtx
BUvpGLL4Ix2P8ejEsRTnd0qjYtiPuqPdy+UHsGg/zC+Yxrd1YwRRxLhGhZLpUl3b
3K5ZMXiDjEsdZsW+wdKWleqGX7ZFhaLUbVW9WrcVEqki8Pufk+wbOq2nctUSGARr
6hJPdwZD47FLSnO0Q+QZR9LOhwAQokilEZFInVTlwbSj7VfHtqh1PRtEkrTavIio
EAnAbXcm0XPzBK7yZkt415vHkyo64rkDy3f56zJe3TgZy0g0f9R9Mk44R65cIILy
+FQLNsJN0MTLRzFRpNiWV/e+myxgVtqMDyN18ea5vBlv52LZ2Xo/eAy51end8zMS
gQIDAQAB
-----END PUBLIC KEY-----
""";

    public bool Verify(string manifestPath, ReleaseManifest manifest, string expectedPublisher)
    {
        if (!File.Exists(manifestPath)
            || !string.Equals(manifest.Publisher, expectedPublisher, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            var signature = Convert.FromBase64String(manifest.Signature);
            using var rsa = RSA.Create();
            rsa.ImportFromPem(PublicKeyPem);
            return rsa.VerifyData(
                Encoding.UTF8.GetBytes(manifest.SigningPayload),
                signature,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}

public sealed class WindowsAuthenticodeVerifier
{
    private static readonly Guid GenericVerifyAction = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    public bool Verify(string executablePath, string expectedPublisher)
    {
        if (!OperatingSystem.IsWindows()
            || !File.Exists(executablePath)
            || string.IsNullOrWhiteSpace(expectedPublisher))
        {
            return false;
        }

        try
        {
            using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(executablePath));
            var publisherMatches = certificate.Subject.Contains(expectedPublisher, StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false),
                    expectedPublisher,
                    StringComparison.OrdinalIgnoreCase);
            return publisherMatches && VerifyTrust(executablePath) == 0;
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static uint VerifyTrust(string filePath)
    {
        var filePathPointer = Marshal.StringToCoTaskMemUni(filePath);
        var fileInfo = new WinTrustFileInfo
        {
            StructureSize = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
            FilePath = filePathPointer
        };
        var fileInfoPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustFileInfo>());
        Marshal.StructureToPtr(fileInfo, fileInfoPointer, fDeleteOld: false);
        var data = new WinTrustData
        {
            StructureSize = (uint)Marshal.SizeOf<WinTrustData>(),
            UIChoice = 2,
            RevocationChecks = 0,
            UnionChoice = 1,
            FileInfo = fileInfoPointer,
            StateAction = 0,
            ProviderFlags = 0,
            UIContext = 0
        };
        var dataPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustData>());
        Marshal.StructureToPtr(data, dataPointer, fDeleteOld: false);
        try
        {
            var actionIdentifier = GenericVerifyAction;
            return WinVerifyTrust(IntPtr.Zero, ref actionIdentifier, dataPointer);
        }
        finally
        {
            Marshal.DestroyStructure<WinTrustData>(dataPointer);
            Marshal.FreeCoTaskMem(dataPointer);
            Marshal.DestroyStructure<WinTrustFileInfo>(fileInfoPointer);
            Marshal.FreeCoTaskMem(fileInfoPointer);
            Marshal.FreeCoTaskMem(filePathPointer);
        }
    }

    [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern uint WinVerifyTrust(
        IntPtr windowHandle,
        ref Guid actionIdentifier,
        IntPtr trustData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint StructureSize;
        public IntPtr FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public uint StructureSize;
        public IntPtr PolicyCallbackData;
        public IntPtr SIPClientData;
        public uint UIChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr FileInfo;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr URLReference;
        public uint ProviderFlags;
        public uint UIContext;
    }
}
