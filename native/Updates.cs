using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Kachalka.Updates;

namespace Kachalka;
public partial class MainWindow
{
    readonly CancellationTokenSource updateCancellation = new();
    readonly UpdateClient updateClient = new(UpdateTrust.PublicKey);
    UpdateOffer? updateOffer;
    string? preparedUpdateJob;
    bool checkingUpdate, updateRestartRequested;
    string updateStatus = "Проверка обновлений ещё не выполнялась.";
    TextBlock? updateStatusLabel;
    Button? updateCheckButton, updateInstallButton;
    static Version CurrentVersion => typeof(MainWindow).Assembly.GetName().Version ?? new(0, 19, 0);
    void UpdateStatus(string text)
    {
        updateStatus = text;
        if (updateStatusLabel is not null) updateStatusLabel.Text = text;
        if (updateCheckButton is not null) updateCheckButton.IsEnabled = !checkingUpdate;
        if (updateInstallButton is not null) updateInstallButton.Visibility = updateOffer is not null ? Visibility.Visible : Visibility.Collapsed;
        if (updateInstallButton is not null) updateInstallButton.IsEnabled = !checkingUpdate;
    }
    internal async Task CheckUpdatesAsync(bool manual = false)
    {
        if (checkingUpdate || closed || (!manual && !prefs.CheckForUpdates)) return;
        checkingUpdate = true; UpdateStatus("Проверяем обновления на GitHub…");
        try
        {
            updateOffer = await updateClient.CheckAsync(CurrentVersion, updateCancellation.Token);
            if (closed) return;
            UpdateStatus(updateOffer is null ? "Установлена последняя версия." : "Доступна версия " + updateOffer.Manifest.Version + ".");
            if (updateOffer is not null && prefs.AutoUpdate) await PrepareUpdateAsync();
        }
        catch (OperationCanceledException) { if (!closed) UpdateStatus("Проверка обновления отменена или истекло время ожидания."); }
        catch (Exception error) { if (!closed) UpdateStatus("Не удалось проверить обновление: " + error.Message); }
        finally { checkingUpdate = false; if (!closed) UpdateStatus(updateStatus); }
    }
    async Task PrepareUpdateAsync()
    {
        if (preparedUpdateJob is not null || updateOffer is null) return;
        var install = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        if (UpdateArchive.Inside(Preferences.DataDir, install) || string.Equals(Path.GetFullPath(Preferences.DataDir), install, StringComparison.OrdinalIgnoreCase) || UpdateArchive.Inside(prefs.Folder, install) ||
            string.Equals(Path.GetFullPath(prefs.Folder), install, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Для обновления перенеси папку загрузок за пределы папки приложения.");
        var probe = Path.Combine(Directory.GetParent(install)?.FullName ?? throw new IOException("Нельзя обновлять корневую папку диска."), ".kachalka-write-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(probe); Directory.Delete(probe);
        var id = Guid.NewGuid().ToString("N");
        var work = Path.Combine(Preferences.DataDir, "updates", id);
        Directory.CreateDirectory(work);
        bool ready = false;
        try
        {
            var archive = Path.Combine(work, "package.zip");
            UpdateStatus("Скачиваем обновление…");
            await updateClient.DownloadAsync(updateOffer, archive, new Progress<int>(p => UpdateStatus($"Скачиваем обновление: {p}%")), updateCancellation.Token);
            var manifest = Path.Combine(work, "latest.json"); var signature = Path.Combine(work, "latest.sig");
            await File.WriteAllBytesAsync(manifest, updateOffer.ManifestBytes, updateCancellation.Token);
            await File.WriteAllBytesAsync(signature, updateOffer.Signature, updateCancellation.Token);
            File.Copy(Path.Combine(install, "Kachalka.Updater.exe"), Path.Combine(work, "updater.exe"));
            using var parent = Process.GetCurrentProcess();
            var job = new UpdateJob(install, archive, manifest, signature, parent.Id, parent.StartTime.ToUniversalTime().Ticks, id);
            var path = Path.Combine(work, "job.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(job), updateCancellation.Token);
            preparedUpdateJob = path;
            ready = true;
            UpdateStatus("Версия " + updateOffer.Manifest.Version + " готова. Установится при выходе из приложения.");
            Status.Text = "Обновление скачано. Для установки открой Настройки → Обновления.";
        }
        finally { if (!ready) { preparedUpdateJob = null; Directory.Delete(work, true); } }
    }
    async Task RestartForUpdateAsync()
    {
        if (checkingUpdate) return;
        checkingUpdate = true;
        try { await PrepareUpdateAsync(); if (preparedUpdateJob is not null) { updateRestartRequested = true; Close(); } }
        catch (Exception error) { UpdateStatus("Не удалось подготовить обновление: " + error.Message); }
        finally { checkingUpdate = false; if (!closed) UpdateStatus(updateStatus); }
    }
    void LaunchPreparedUpdate()
    {
        if (preparedUpdateJob is null || (!prefs.AutoUpdate && !updateRestartRequested)) return;
        var helper = Path.Combine(Path.GetDirectoryName(preparedUpdateJob)!, "updater.exe");
        var start = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(helper)! };
        start.ArgumentList.Add("--job"); start.ArgumentList.Add(preparedUpdateJob);
        using var process = Process.Start(start) ?? throw new IOException("Не удалось запустить установщик обновления.");
    }
    void RenderUpdateSettings(StackPanel panel)
    {
        void Toggle(string label, bool value, Action<bool> save)
        {
            var toggle = new CheckBox { Content = Text(label), IsChecked = value, Style = (Style)FindResource("SettingsSwitch"), Margin = new(0, 0, 0, 12) };
            bool previous = value;
            toggle.Click += (_, _) =>
            {
                try { save(toggle.IsChecked == true); prefs.Save(); previous = toggle.IsChecked == true; }
                catch (Exception error) { save(previous); toggle.IsChecked = previous; UpdateStatus("Не удалось сохранить настройку: " + error.Message); }
            };
            panel.Children.Add(toggle);
        }
        Toggle("Проверять обновления при запуске", prefs.CheckForUpdates, v => prefs.CheckForUpdates = v);
        Toggle("Автоматически скачивать и устанавливать при выходе", prefs.AutoUpdate, v => prefs.AutoUpdate = v);
        panel.Children.Add(Text("Обновление применяется после сохранения очереди. Настройки и загруженные файлы остаются на месте.", 12, true));
        updateStatusLabel = Text(updateStatus); panel.Children.Add(updateStatusLabel);
        var actions = new WrapPanel(); panel.Children.Add(actions);
        updateCheckButton = AsyncButton("Проверить обновления", () => CheckUpdatesAsync(true)); actions.Children.Add(updateCheckButton);
        updateInstallButton = AsyncButton("Обновить и перезапустить", RestartForUpdateAsync); actions.Children.Add(updateInstallButton);
        actions.Children.Add(Button("Что нового", () => Process.Start(new ProcessStartInfo(updateOffer?.Manifest.ReleaseNotesUrl ?? "https://github.com/" + UpdateManifest.Repository + "/releases") { UseShellExecute = true })));
        UpdateStatus(updateStatus);
    }
}
