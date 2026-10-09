using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace Kachalka.Lan;

internal sealed class LanState : IDisposable
{
    readonly string directory;
    readonly string statePath;
    static ReadOnlySpan<byte> StateMagic => "KLDP1"u8;
    public SavedState Data { get; }
    public X509Certificate2 Certificate { get; }
    public string Fingerprint { get; }

    public LanState(LanServiceOptions options)
    {
        directory = Path.GetFullPath(options.StateDirectory);
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        statePath = Path.Combine(directory, "state.json");
        var keyPath = Path.Combine(directory, "identity.key");
        var exists = File.Exists(statePath);
        try
        {
            if (exists && new FileInfo(statePath).Length > 4 * 1024 * 1024) throw new InvalidDataException();
            var saved = exists ? File.ReadAllBytes(statePath) : null;
            if (saved is not null && OperatingSystem.IsWindows())
            {
                if (!saved.AsSpan().StartsWith(StateMagic)) throw new InvalidDataException("Настройки локальной сети повреждены.");
                saved = Dpapi.Unprotect(saved[StateMagic.Length..]);
            }
            try { Data = saved is not null ? JsonSerializer.Deserialize<SavedState>(saved, LanProtocol.JsonOptions) ?? throw new InvalidDataException() : new SavedState { Id = Guid.NewGuid(), Name = CleanName(options.DeviceName) }; }
            finally { if (saved is not null) CryptographicOperations.ZeroMemory(saved); }
            if (Data.Id == Guid.Empty || Data.Peers.Count > 128 || Data.Receipts.Count > 256) throw new InvalidDataException();
            Data.Name = CleanName(Data.Name);
            if (exists && !File.Exists(keyPath)) throw new InvalidDataException("Ключ локальной сети отсутствует.");
            if (File.Exists(keyPath))
            {
                var key = File.ReadAllBytes(keyPath);
                if (key.Length > 128 * 1024) throw new InvalidDataException();
                var plain = OperatingSystem.IsWindows() ? Dpapi.Unprotect(key) : key;
                try { Certificate = LoadCertificate(plain); }
                finally { CryptographicOperations.ZeroMemory(plain); }
            }
            else
            {
                using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                var request = new CertificateRequest($"CN=Ka4alka-{Data.Id:N}", key, HashAlgorithmName.SHA256);
                request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
                request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
                var eku = new OidCollection { new("1.3.6.1.5.5.7.3.1"), new("1.3.6.1.5.5.7.3.2") };
                request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(eku, true));
                using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(20));
                var plain = generated.Export(X509ContentType.Pfx);
                try
                {
                    // Schannel needs a current-user key container, including on the very first launch.
                    Certificate = LoadCertificate(plain);
                    WritePrivate(keyPath, OperatingSystem.IsWindows() ? Dpapi.Protect(plain) : plain);
                }
                finally { CryptographicOperations.ZeroMemory(plain); }
            }
            if (!Certificate.HasPrivateKey) throw new InvalidDataException();
            Fingerprint = Convert.ToHexString(SHA256.HashData(Certificate.RawData));
            Save();
        }
        catch (Exception ex) when (ex is not LanException) { throw new LanException("Не удалось прочитать настройки локальной сети. Проверьте доступ к папке приложения.", ex); }
    }

    internal static X509Certificate2 LoadCertificate(byte[] pfx)
    {
        // Do not request PersistKeySet: disposing the certificate also removes its temporary key container.
        // The only durable identity copy remains the current-user DPAPI blob written by this class.
        var flags = OperatingSystem.IsWindows()
            ? X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable
            : X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable;
        return X509CertificateLoader.LoadPkcs12(pfx, null, flags);
    }

    public void Save()
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(Data, LanProtocol.JsonOptions);
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var protectedData = Dpapi.Protect(json);
                var bytes = new byte[StateMagic.Length + protectedData.Length];
                StateMagic.CopyTo(bytes); protectedData.CopyTo(bytes, StateMagic.Length);
                WritePrivate(statePath, bytes);
            }
            else WritePrivate(statePath, json);
        }
        finally { CryptographicOperations.ZeroMemory(json); }
    }
    void WritePrivate(string path, byte[] bytes)
    {
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            if (OperatingSystem.IsWindows()) File.WriteAllBytes(temporary, bytes);
            else
            {
                using var stream = new FileStream(temporary, new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite });
                stream.Write(bytes);
                stream.Flush(true);
            }
            File.Move(temporary, path, true);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static string CleanName(string? value)
    {
        var name = value?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80 || name.Any(char.IsControl)) throw new LanException("Имя устройства должно содержать от 1 до 80 символов.");
        return name;
    }

    public void Dispose() => Certificate.Dispose();
}

internal sealed class SavedState
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public List<LanDevice> Peers { get; set; } = [];
    public List<SavedReceipt> Receipts { get; set; } = [];
}

internal sealed record SavedReceipt(string PeerFingerprint, Guid RequestId, string ContentDigest, LanDownloadReceipt Receipt, DateTimeOffset CreatedUtc);

internal static class Dpapi
{
    [StructLayout(LayoutKind.Sequential)] struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true)] static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)] static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr pointer);
    public static byte[] Protect(byte[] value) => Convert(value, true);
    public static byte[] Unprotect(byte[] value) => Convert(value, false);
    static byte[] Convert(byte[] value, bool protect)
    {
        var input = new Blob { Length = value.Length, Data = Marshal.AllocHGlobal(value.Length) };
        Blob output = default;
        try
        {
            Marshal.Copy(value, 0, input.Data, value.Length);
            var okay = protect ? CryptProtectData(ref input, "Ka4alka LAN identity", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output) : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!okay) throw new CryptographicException(Marshal.GetLastWin32Error());
            var bytes = new byte[output.Length];
            Marshal.Copy(output.Data, bytes, 0, output.Length);
            return bytes;
        }
        finally
        {
            for (var i = 0; i < input.Length; i++) Marshal.WriteByte(input.Data, i, 0);
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero)
            {
                for (var i = 0; i < output.Length; i++) Marshal.WriteByte(output.Data, i, 0);
                LocalFree(output.Data);
            }
        }
    }
}
