using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace Kachalka.Updates;

public static class UpdateArchive
{
    static readonly char[] Forbidden = ['<', '>', ':', '"', '|', '?', '*'];
    public static void ExtractVerified(string archive, string destination, UpdateManifest manifest)
    {
        manifest.Validate();
        using (var stream = File.OpenRead(archive))
            if (stream.Length != manifest.Package.Size || !Convert.ToHexString(SHA256.HashData(stream)).Equals(manifest.Package.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Архив обновления повреждён.");
        if (Directory.Exists(destination)) throw new IOException("Папка новой версии уже существует.");
        using var zip = ZipFile.OpenRead(archive);
        if (zip.Entries.Count is 0 or > 10000) throw new InvalidDataException("Недопустимое количество файлов.");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? root = null; long expanded = 0;
        foreach (var entry in zip.Entries)
        {
            var parts = entry.FullName.Replace('\\', '/').TrimEnd('/').Split('/');
            if (parts.Length < 1 || parts.Any(p => p.Length == 0 || p is "." or ".." || p.EndsWith('.') || p.EndsWith(' ') || p.IndexOfAny(Forbidden) >= 0 || p.Any(char.IsControl)) ||
                parts.Any(p => System.Text.RegularExpressions.Regex.IsMatch(p, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) ||
                (entry.ExternalAttributes & 0x400) != 0 || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidDataException("Недопустимый путь в архиве обновления.");
            root ??= parts[0];
            if (parts[0] != root || !paths.Add(string.Join('/', parts)) || (parts.Length == 1 && !entry.FullName.EndsWith('/')))
                throw new InvalidDataException("Архив должен содержать одну папку приложения без повторов.");
            expanded = checked(expanded + entry.Length);
            if (expanded > 2L * 1024 * 1024 * 1024) throw new InvalidDataException("Распакованное обновление слишком большое.");
        }
        Directory.CreateDirectory(destination);
        try
        {
            foreach (var entry in zip.Entries)
            {
                var parts = entry.FullName.Replace('\\', '/').TrimEnd('/').Split('/');
                if (parts.Length == 1) continue;
                var file = Path.GetFullPath(Path.Combine(destination, Path.Combine(parts[1..])));
                if (!Inside(file, destination)) throw new InvalidDataException("Путь выходит за папку обновления.");
                if (entry.FullName.EndsWith('/')) Directory.CreateDirectory(file);
                else { Directory.CreateDirectory(Path.GetDirectoryName(file)!); entry.ExtractToFile(file, false); }
            }
            ValidateApplication(destination,manifest);
        }
        catch { Directory.Delete(destination, true); throw; }
    }
    public static void ValidateApplication(string destination,UpdateManifest manifest)
    {
        foreach (var file in new[] { "Kachalka.exe", "Kachalka.dll", "Kachalka.runtimeconfig.json", "Kachalka.Updater.exe" })
            if (!File.Exists(Path.Combine(destination, file))) throw new InvalidDataException("В обновлении отсутствует " + file);
        using var assembly = File.OpenRead(Path.Combine(destination, "Kachalka.dll"));
        using var pe = new PEReader(assembly);
        if (pe.GetMetadataReader().GetAssemblyDefinition().Version.ToString(3) != manifest.Version)
            throw new InvalidDataException("Версия приложения не совпадает с описанием обновления.");
    }
    public static bool Inside(string path, string root) => Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
