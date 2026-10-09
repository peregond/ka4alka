using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kachalka.Updates;

public sealed record UpdatePackage(string Url, string FileName, long Size, string Sha256, string Format);
public sealed record UpdateManifest(int SchemaVersion, string AppId, string Channel, string Version,
    string Platform, string MinimumWindows, UpdatePackage Package, string ReleaseNotesUrl, bool AutomaticInstallationAvailable, ComponentIndex? Components=null)
{
    public const string Repository = "peregond/ka4alka";
    public const long MaxPackageBytes = 512L * 1024 * 1024;
    public const string ManifestUrl = "https://github.com/" + Repository + "/releases/latest/download/latest.json";
    public static UpdateManifest Verify(byte[] bytes, byte[] signature, string publicKey)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(publicKey);
        if (!rsa.VerifyData(bytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            throw new InvalidDataException("Подпись обновления не прошла проверку.");
        var manifest = JsonSerializer.Deserialize<UpdateManifest>(bytes, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Пустое описание обновления.");
        manifest.Validate();
        return manifest;
    }
    public void Validate()
    {
        if (SchemaVersion != 1 || AppId != "kachalka" || Channel != "stable" || Platform != "win-x64" || MinimumWindows != "10" ||
            Version is null || !Regex.IsMatch(Version, @"^\d+\.\d+\.\d+$") || !System.Version.TryParse(Version, out _) || Package is null ||
            Package.Format != "portable-zip" || Package.FileName != "Kachalka-win-x64.zip" ||
            Package.Size <= 0 || Package.Size > MaxPackageBytes || Package.Sha256 is null || !Regex.IsMatch(Package.Sha256, @"\A[0-9a-fA-F]{64}\z") ||
            Package.Url != $"https://github.com/{Repository}/releases/download/v{Version}/Kachalka-win-x64.zip" ||
            ReleaseNotesUrl != $"https://github.com/{Repository}/releases/tag/v{Version}" || !AutomaticInstallationAvailable)
            throw new InvalidDataException("Неподдерживаемое описание обновления.");
        if(Components is {} index&&(index.Url!=$"https://github.com/{Repository}/releases/download/v{Version}/components.json"||index.Size<=0||index.Size>ComponentCatalog.MaxIndexBytes||!ComponentCatalog.ValidHash(index.Sha256)))
            throw new InvalidDataException("Неверное описание компонентов обновления.");
    }
    public bool IsNewerThan(System.Version current) => System.Version.Parse(Version) > new System.Version(current.Major, current.Minor, Math.Max(0, current.Build));
}
