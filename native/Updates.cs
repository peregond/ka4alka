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
    Button? updateActionButton;
    static Version CurrentVersion => typeof(MainWindow).Assembly.GetName().Version ?? new(0, 19, 0);
    void UpdateStatus(string text)
    {
        updateStatus = text;RefreshSidebarUpdate();
        if (updateStatusLabel is not null) updateStatusLabel.Text = text;
        if (updateActionButton is not null)
        {
            updateActionButton.Content=checkingUpdate?preparedUpdateJob is not null?"Устанавливаем…":updateOffer is null?"Проверяем…":"Скачиваем…":preparedUpdateJob is not null?"Обновить":updateOffer is not null?"Скачать":"Проверить обновления";
            updateActionButton.IsEnabled=!checkingUpdate;updateActionButton.Style=(Style)FindResource(updateOffer is not null||preparedUpdateJob is not null?"PrimaryButton":typeof(Button));
            updateActionButton.ToolTip=preparedUpdateJob is not null?"Установить обновление и перезапустить приложение":updateOffer is not null?"Скачать обновлённые компоненты":"Проверить новую версию на GitHub";
            System.Windows.Automation.AutomationProperties.SetName(updateActionButton,updateActionButton.Content.ToString());
        }
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
            if (updateOffer is not null && prefs.AutoUpdate && !manual) await PrepareUpdateAsync();
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
            string? componentsPath=null,componentsDirectory=null;
            if(updateOffer.Manifest.Components!=null)
            {
                UpdateStatus("Сравниваем компоненты установленной и новой версии…");
                var plan=await updateClient.PlanComponentsAsync(updateOffer,install,updateCancellation.Token);
                var stage=Path.Combine(work,"components");
                try
                {
                    await updateClient.DownloadComponentsAsync(updateOffer,plan,stage,new Progress<ComponentProgress>(p=>UpdateStatus($"Компонентов к обновлению: {p.ChangedFiles}. Скачано {p.Received/1048576d:F1} из {p.Total/1048576d:F1} МБ; без скачивания: {p.ReusedFiles}.")),updateCancellation.Token);
                    componentsPath=Path.Combine(work,"components.json");componentsDirectory=stage;
                    await File.WriteAllBytesAsync(componentsPath,plan.CatalogBytes,updateCancellation.Token);
                }
                catch(RangeNotSupportedException){UpdateStatus("Сервер не поддерживает компоненты. Скачиваем полный пакет…");}
            }
            if(componentsPath==null)
                await updateClient.DownloadAsync(updateOffer,archive,new Progress<int>(p=>UpdateStatus($"Скачиваем обновление: {p}%")),updateCancellation.Token);
            var manifest = Path.Combine(work, "latest.json"); var signature = Path.Combine(work, "latest.sig");
            await File.WriteAllBytesAsync(manifest, updateOffer.ManifestBytes, updateCancellation.Token);
            await File.WriteAllBytesAsync(signature, updateOffer.Signature, updateCancellation.Token);
            File.Copy(Path.Combine(install, "Kachalka.Updater.exe"), Path.Combine(work, "updater.exe"));
            using var parent = Process.GetCurrentProcess();
            var job = new UpdateJob(install, archive, manifest, signature, parent.Id, parent.StartTime.ToUniversalTime().Ticks, id,componentsPath,componentsDirectory);
            var path = Path.Combine(work, "job.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(job), updateCancellation.Token);
            preparedUpdateJob = path;
            ready = true;
            UpdateStatus("Версия " + updateOffer.Manifest.Version + " готова. "+(prefs.AutoUpdate?"Установится при выходе из приложения.":"Нажми «Обновить», чтобы установить."));
            Status.Text = "Обновление готово. Нажми «Обновить», чтобы установить его и перезапустить приложение.";
        }
        finally { if (!ready) { preparedUpdateJob = null; Directory.Delete(work, true); } }
    }
    async Task RestartForUpdateAsync()
    {
        if (checkingUpdate) return;
        checkingUpdate = true;UpdateStatus(updateStatus);
        try { await PrepareUpdateAsync(); if (preparedUpdateJob is not null) { updateRestartRequested = true; Close(); } }
        catch (Exception error) { UpdateStatus("Не удалось подготовить обновление: " + error.Message); }
        finally { checkingUpdate = false; if (!closed) UpdateStatus(updateStatus); }
    }
    async Task UpdateActionAsync()
    {
        if(checkingUpdate||closed)return;
        if(preparedUpdateJob is not null){await RestartForUpdateAsync();return;}
        if(updateOffer is null){await CheckUpdatesAsync(true);return;}
        checkingUpdate=true;UpdateStatus("Скачиваем обновление…");
        try{await PrepareUpdateAsync();}
        catch(OperationCanceledException){if(!closed)UpdateStatus("Загрузка обновления отменена. Можно попробовать ещё раз.");}
        catch(Exception error){if(!closed)UpdateStatus("Не удалось скачать обновление: "+error.Message);}
        finally{checkingUpdate=false;if(!closed)UpdateStatus(updateStatus);}
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
            var toggle = new CheckBox { Content = SwitchTitle(label), IsChecked = value, Style = (Style)FindResource("SettingsSwitch"), Margin = new(0, 0, 0, 12) };
            bool previous = value;
            toggle.Click += (_, _) =>
            {
                try { save(toggle.IsChecked == true); prefs.Save(); previous = toggle.IsChecked == true; }
                catch (Exception error) { save(previous); toggle.IsChecked = previous; UpdateStatus("Не удалось сохранить настройку: " + error.Message); }
            };
            panel.Children.Add(toggle);
        }
        // Header: icon tile, version and status on the left, the single update action on the right.
        var head=new Grid{Margin=new(0,0,0,18)};head.ColumnDefinitions.Add(new(){Width=GridLength.Auto});head.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});head.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var tile=new Border{Width=48,Height=48,CornerRadius=new(14),Margin=new(0,0,16,0),VerticalAlignment=VerticalAlignment.Center};tile.SetResourceReference(Border.BackgroundProperty,"Accent");
        var tileGlyph=new System.Windows.Shapes.Path{Data=(System.Windows.Media.Geometry)FindResource("IconDownload"),Width=22,Height=22,Stretch=System.Windows.Media.Stretch.Uniform,StrokeThickness=2,StrokeStartLineCap=System.Windows.Media.PenLineCap.Round,StrokeEndLineCap=System.Windows.Media.PenLineCap.Round,StrokeLineJoin=System.Windows.Media.PenLineJoin.Round};tileGlyph.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty,"AccentInk");tile.Child=tileGlyph;head.Children.Add(tile);
        var info=new StackPanel{VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(info,1);head.Children.Add(info);
        var nameLine=new StackPanel{Orientation=Orientation.Horizontal};nameLine.Children.Add(new TextBlock{Text="Качалка ",FontSize=15,FontWeight=FontWeights.SemiBold});
        nameLine.Children.Add(new TextBlock{Text=typeof(MainWindow).Assembly.GetName().Version?.ToString(3)??"",FontFamily=(System.Windows.Media.FontFamily)FindResource("MonoFont"),FontSize=15,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center});info.Children.Add(nameLine);
        updateStatusLabel = Text(updateStatus,13);updateStatusLabel.Margin=new(0,3,0,0);updateStatusLabel.SetResourceReference(TextBlock.ForegroundProperty,"Accent");info.Children.Add(updateStatusLabel);
        updateActionButton=new Button {Name="SettingsUpdateAction",Height=48,MinHeight=48,Margin=new(16,0,0,0),VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(updateActionButton,2);head.Children.Add(updateActionButton);
        updateActionButton.Click+=async(_,_)=>await UpdateActionAsync();
        // On a narrow card the action drops below the version and status instead of squeezing them.
        head.SizeChanged+=(_,e)=>
        {
            if(!e.WidthChanged)return;var narrowHead=e.NewSize.Width<420;
            if(narrowHead&&head.RowDefinitions.Count==0){head.RowDefinitions.Add(new());head.RowDefinitions.Add(new());}
            if(!narrowHead)head.RowDefinitions.Clear();
            Grid.SetRow(updateActionButton,narrowHead?1:0);Grid.SetColumn(updateActionButton,narrowHead?0:2);Grid.SetColumnSpan(updateActionButton,narrowHead?3:1);
            updateActionButton.Margin=narrowHead?new(0,14,0,0):new(16,0,0,0);updateActionButton.HorizontalAlignment=narrowHead?HorizontalAlignment.Left:HorizontalAlignment.Stretch;
        };
        panel.Children.Add(head);
        Toggle("Проверять обновления при запуске", prefs.CheckForUpdates, v => prefs.CheckForUpdates = v);
        Toggle("Автоматически скачивать и устанавливать при выходе", prefs.AutoUpdate, v => prefs.AutoUpdate = v);
        panel.Children.Add(Hint("Обновление применяется после сохранения очереди. Неизменившиеся компоненты повторно не скачиваются. Настройки и загруженные файлы остаются на месте."));
        var actions = new WrapPanel(); panel.Children.Add(actions);
        var notes=new TextBlock {Margin=new(10,0,0,0),VerticalAlignment=VerticalAlignment.Center};
        var link=new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("Что нового"));link.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty,"Accent");
        link.Click+=(_,_)=>
        {
            try{Process.Start(new ProcessStartInfo(updateOffer?.Manifest.ReleaseNotesUrl??"https://github.com/"+UpdateManifest.Repository+"/releases"){UseShellExecute=true});}
            catch(Exception error){UpdateStatus("Не удалось открыть описание обновления: "+error.Message);}
        };
        notes.Inlines.Add(link);actions.Children.Add(notes);
        UpdateStatus(updateStatus);
    }
}
