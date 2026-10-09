namespace Kachalka.Updates;

public sealed record UpdateJob(string InstallDirectory, string ArchivePath, string ManifestPath, string SignaturePath,
    int ParentPid, long ParentStartTicks, string Id, string? ComponentsPath=null, string? ComponentsDirectory=null);

public static class UpdateInstaller
{
    // All moves stay on one volume. Never delete a previous installation before a healthy launch.
    public static async Task ApplyAsync(string install, string candidate, string backup, Func<Task<bool>> launchAndCheck, Func<Task> stopNew, Action launchOld)
    {
        if (!Directory.Exists(install) || !Directory.Exists(candidate) || Directory.Exists(backup))
            throw new IOException("Недопустимые папки установки.");
        Directory.Move(install, backup);
        bool switched = false;
        try
        {
            Directory.Move(candidate, install); switched = true;
            if (!await launchAndCheck()) throw new IOException("Новая версия не смогла запуститься.");
        }
        catch
        {
            if (switched)
            {
                await stopNew();
                Directory.Move(install, candidate);
            }
            Directory.Move(backup, install);
            launchOld();
            throw;
        }
    }
}
