using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace GalilunaShield;

/// <summary>
/// Encrypts sensitive evidence at rest (audio clips, recordings, screenshots) using the Windows Data
/// Protection API (DPAPI), bound to the current Windows user, with optional extra entropy derived from the
/// parent PIN. This protects the files if the disk is read from another computer or another Windows account,
/// and (when a PIN is set) if another process on the same account tries to read them without the PIN. It is
/// not a substitute for running the child on a standard (non-administrator) account.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class EvidenceProtector
{
    /// <summary>Extension added to encrypted files.</summary>
    public const string Extension = ".enc";

    private readonly byte[]? _entropy;

    public bool Enabled { get; }

    public EvidenceProtector(bool enabled, string? parentPin)
    {
        Enabled = enabled && OperatingSystem.IsWindows();
        _entropy = string.IsNullOrEmpty(parentPin)
            ? null
            : SHA256.HashData(Encoding.UTF8.GetBytes("GalilunaShield.v1:" + parentPin));
    }

    /// <summary>True if the path points at an encrypted evidence file.</summary>
    public static bool IsEncrypted(string path) => path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);

    public byte[] Protect(byte[] plain) => ProtectedData.Protect(plain, _entropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] cipher) => ProtectedData.Unprotect(cipher, _entropy, DataProtectionScope.CurrentUser);

    /// <summary>Writes bytes to <paramref name="path"/>. When enabled, encrypts and appends the .enc extension. Returns the actual path written.</summary>
    public string WriteBytes(string path, byte[] data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (Enabled)
        {
            var enc = path + Extension;
            File.WriteAllBytes(enc, Protect(data));
            return enc;
        }
        File.WriteAllBytes(path, data);
        return path;
    }

    /// <summary>Reads a file, decrypting it if it is an encrypted evidence file.</summary>
    public byte[] ReadBytes(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return IsEncrypted(path) ? Unprotect(bytes) : bytes;
    }

    /// <summary>Encrypts an existing plaintext file in place (writes .enc, deletes the original). Returns the new path, or the original on failure.</summary>
    public string EncryptFileInPlace(string path)
    {
        if (!Enabled || IsEncrypted(path) || !File.Exists(path)) return path;
        try
        {
            var enc = path + Extension;
            File.WriteAllBytes(enc, Protect(File.ReadAllBytes(path)));
            File.Delete(path);
            return enc;
        }
        catch
        {
            return path;
        }
    }

    /// <summary>Decrypts an encrypted file to a temporary file for viewing/playback, returning the temp path (caller deletes it).</summary>
    public string DecryptToTemp(string path)
    {
        var plain = ReadBytes(path);
        var ext = Path.GetExtension(Path.GetFileNameWithoutExtension(path)); // strip .enc, keep .wav/.jpg
        if (string.IsNullOrEmpty(ext)) ext = ".tmp";
        var dir = Path.Combine(Path.GetTempPath(), "GalilunaShield");
        Directory.CreateDirectory(dir);
        var temp = Path.Combine(dir, Guid.NewGuid().ToString("N") + ext);
        File.WriteAllBytes(temp, plain);
        return temp;
    }
}
