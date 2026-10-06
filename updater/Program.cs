using System.Diagnostics;
using System.Text.Json;
using Kachalka.Updates;

if (args.Length != 2 || args[0] != "--job") return 2;
var jobPath = Path.GetFullPath(args[1]);
var work = Path.GetDirectoryName(jobPath)!;
Process? launched = null;
string? oldInstall = null;
Version? oldVersion = null;
bool restarted = false;
try
{
    var job = JsonSerializer.Deserialize<UpdateJob>(File.ReadAllText(jobPath)) ?? throw new InvalidDataException("Missing job");
    if (!Guid.TryParseExact(job.Id, "N", out _) || Path.GetFileName(work) != job.Id ||
        new[] { job.ArchivePath, job.ManifestPath, job.SignaturePath }.Any(p => !UpdateArchive.Inside(p, work)))
        throw new InvalidDataException("Invalid update job");
    var install = Path.TrimEndingDirectorySeparator(Path.GetFullPath(job.InstallDirectory));
    if (Directory.GetParent(install) is null || UpdateArchive.Inside(AppContext.BaseDirectory, install) ||
        (File.GetAttributes(install) & FileAttributes.ReparsePoint) != 0)
        throw new InvalidDataException("Invalid installation directory");
    var manifest = UpdateManifest.Verify(File.ReadAllBytes(job.ManifestPath), File.ReadAllBytes(job.SignaturePath), UpdateTrust.PublicKey);
    var currentAssembly = System.Reflection.AssemblyName.GetAssemblyName(Path.Combine(install, "Kachalka.dll"));
    oldInstall = install; oldVersion = currentAssembly.Version;
    if (!manifest.IsNewerThan(currentAssembly.Version!)) throw new InvalidDataException("Update is not newer");
    try
    {
        using var parent = Process.GetProcessById(job.ParentPid);
        if (parent.StartTime.ToUniversalTime().Ticks != job.ParentStartTicks) throw new InvalidDataException("Parent identity changed");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await parent.WaitForExitAsync(timeout.Token);
    }
    catch (ArgumentException) { }
    var parentDirectory = Path.GetDirectoryName(install)!;
    var candidate = Path.Combine(parentDirectory, ".kachalka-new-" + job.Id);
    var backup = Path.Combine(parentDirectory, ".kachalka-backup-" + job.Id);
    UpdateArchive.ExtractVerified(job.ArchivePath, candidate, manifest);
    var marker = Path.Combine(work, "healthy");
    if (File.Exists(marker)) File.Delete(marker);
    await UpdateInstaller.ApplyAsync(install, candidate, backup, async () =>
    {
        var start = new ProcessStartInfo(Path.Combine(install, "Kachalka.exe")) { UseShellExecute = false, WorkingDirectory = install };
        start.ArgumentList.Add("--update-health"); start.ArgumentList.Add(job.Id);
        launched = Process.Start(start) ?? throw new IOException("New process did not start");
        var end = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < end && !launched.HasExited)
        {
            if (File.Exists(marker) && File.ReadAllText(marker) == manifest.Version) return true;
            await Task.Delay(250);
        }
        return false;
    }, async () =>
    {
        if (launched is not null && !launched.HasExited) { launched.Kill(true); await launched.WaitForExitAsync(); }
    }, () => { Process.Start(new ProcessStartInfo(Path.Combine(install, "Kachalka.exe")) { UseShellExecute = false, WorkingDirectory = install }); restarted = true; });
    restarted = true;
    if (OperatingSystem.IsWindows())
    {
        using var registration = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Kachalka", true);
        if (registration?.GetValue("InstallLocation") is string registered && Path.GetFullPath(registered).Equals(install, StringComparison.OrdinalIgnoreCase))
            registration.SetValue("DisplayVersion", manifest.Version);
    }
    File.WriteAllText(Path.Combine(work, "result.txt"), "Обновление установлено: " + manifest.Version + ". Предыдущая версия: " + backup);
    File.Delete(job.ArchivePath);
    return 0;
}
catch (Exception error)
{
    File.WriteAllText(Path.Combine(work, "result.txt"), "Не удалось установить обновление: " + error.Message);
    if (!restarted && oldInstall is not null && File.Exists(Path.Combine(oldInstall, "Kachalka.exe")) &&
        System.Reflection.AssemblyName.GetAssemblyName(Path.Combine(oldInstall, "Kachalka.dll")).Version == oldVersion)
        Process.Start(new ProcessStartInfo(Path.Combine(oldInstall, "Kachalka.exe")) { UseShellExecute = false, WorkingDirectory = oldInstall });
    return 1;
}
finally { launched?.Dispose(); }
