using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Kachalka;

public partial class MainWindow
{
    void ShowBugReport()=>CreateBugReport().ShowDialog();

    internal Window CreateBugReport(Func<CancellationToken,Task<string>>? collectReport=null,Action<Uri>? openBrowser=null,Action<string>? copyReport=null,Action<string>? saveReport=null,Func<string,string,string,BugReportDraft>? draftBuilder=null,Func<string,CancellationToken,Task>? saveAsyncReport=null)
    {
        var privatePaths=new[]{prefs.Folder,Preferences.DataDir,Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}.Concat(downloads.Items.Select(item=>item.Folder)).ToArray();
        var area=SystemParameters.WorkArea;
        var window=new Window{Owner=this,Title="Сообщить об ошибке",Width=Math.Min(700,Math.Max(340,area.Width-40)),Height=Math.Min(730,Math.Max(360,area.Height-40)),MinWidth=340,MinHeight=360,WindowStartupLocation=WindowStartupLocation.CenterOwner,UseLayoutRounding=true,FontFamily=FontFamily,FontSize=13};
        window.Resources.MergedDictionaries.Add(Resources);
        window.SetResourceReference(Control.BackgroundProperty,"Bg");window.SetResourceReference(Control.ForegroundProperty,"Text");
        var root=new DockPanel{Margin=new(20)};window.Content=root;
        var footer=new StackPanel();DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var notice=Text("Собираем диагностику…",12,true);notice.Name="BugReportNotice";notice.Margin=new(0,12,0,8);footer.Children.Add(notice);
        var actions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};footer.Children.Add(actions);
        var copy=ActionButton("Скопировать","IconFile",()=>{});copy.Name="CopyBugReport";copy.Padding=new(10,7,10,7);copy.Margin=new(0,0,6,6);actions.Children.Add(copy);
        var save=ActionButton("Сохранить","IconFolder",()=>{});save.Name="SaveBugReport";save.Padding=new(10,7,10,7);save.Margin=new(0,0,6,6);actions.Children.Add(save);
        var open=Button("Открыть форму GitHub",()=>{});open.Name="OpenBugReport";open.Style=(Style)FindResource("PrimaryButton");open.Padding=new(12,7,12,7);open.Margin=new(0,0,6,6);actions.Children.Add(open);
        var close=Button("Закрыть",window.Close);close.Style=(Style)FindResource("QuietButton");close.IsCancel=true;close.Padding=new(10,7,10,7);close.Margin=new(0,0,0,6);actions.Children.Add(close);
        var scroll=new ScrollViewer{Style=(Style)FindResource("PageScroll"),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        AutomationProperties.SetName(scroll,"Предпросмотр отчёта об ошибке");root.Children.Add(scroll);
        var body=new StackPanel();scroll.Content=body;
        var heading=Text("Поможем разобраться",23);heading.FontWeight=FontWeights.SemiBold;body.Children.Add(heading);
        body.Children.Add(Text("Опиши проблему и просмотри отчёт. Кнопка откроет заполненную форму GitHub в браузере; отправишь её уже там.",13,true));
        TextBox Field(string label,string name,int height,int maxLength)
        {
            var caption=Text(label,13);caption.FontWeight=FontWeights.Medium;caption.Margin=new(0,10,0,7);body.Children.Add(caption);
            var box=new TextBox{Name=name,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=height,MaxLength=maxLength,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new(10),Margin=new(0),IsUndoEnabled=false};
            AutomationProperties.SetName(box,label);body.Children.Add(box);return box;
        }
        var problem=Field("Что произошло?","BugReportProblem",80,4000);
        problem.ToolTip="Например: загрузка остановилась после возвращения из сна, хотя интернет уже работает.";
        var steps=Field("Как повторить · необязательно","BugReportSteps",66,4000);
        var preview=Field("Диагностика · можно удалить лишнее","BugReportPreview",190,250_000);preview.IsReadOnly=true;preview.FontSize=12;
        body.Children.Add(Text("Пути к папкам, magnet-ссылки и ключи доступа скрываются. Названия загрузок могут остаться в отчёте — при желании удали их здесь.",11,true));
        var cts=new CancellationTokenSource();var token=cts.Token;
        bool loading=true,closed=false,building=false,queued=false,saving=false;int revision=0,appliedRevision=-1;BugReportDraft? draft=null;
        var debounce=new DispatcherTimer(DispatcherPriority.Background,window.Dispatcher){Interval=TimeSpan.FromMilliseconds(180)};
        bool Ready()=>!closed&&!loading&&appliedRevision==revision&&draft!=null&&problem.Text.Trim().Length>0;
        void RefreshActions()
        {
            if(closed)return;
            var ready=Ready();copy.IsEnabled=ready;save.IsEnabled=ready&&!saving;
            open.IsEnabled=ready&&draft!.FormUri!=null;
            open.Content=draft?.RequiresClipboard==true?"Скопировать и открыть GitHub":"Открыть форму GitHub";
            AutomationProperties.SetName(open,open.Content.ToString());
        }
        void RefreshNotice()
        {
            if(closed)return;
            RefreshActions();
            if(loading){notice.Text="Собираем диагностику… Можно уже описать проблему.";return;}
            if(problem.Text.Trim().Length==0)notice.Text="Напиши, что произошло, чтобы подготовить отчёт.";
            else if(!Ready())notice.Text="Подготавливаем очищенный отчёт…";
            else if(draft!.DescriptionTooLong)notice.Text="Описание слишком длинное для ссылки. Скопируй или сохрани полный отчёт: текст и шаги сохранены целиком.";
            else if(draft.RequiresClipboard)notice.Text="Весь отчёт не помещается в ссылку. Скопируем его целиком и откроем форму с описанием; диагностику вставь из буфера обмена.";
            else notice.Text="Отчёт готов. Проверь его перед открытием формы GitHub.";
        }
        void ScheduleDraft()
        {
            if(closed)return;
            revision++;queued=true;appliedRevision=-1;draft=null;debounce.Stop();
            RefreshNotice();if(!loading)debounce.Start();
        }
        async Task BuildDraft()
        {
            if(closed||loading)return;
            if(building){queued=true;return;}
            building=true;queued=false;
            // Snapshot strings on the dispatcher. Redaction and URI generation
            // run once for a coalesced edit, never once per keystroke on the UI.
            var requestedRevision=revision;var problemText=problem.Text;var stepsText=steps.Text;var diagnosticText=preview.Text;
            try
            {
                var result=await Task.Run(()=>
                {
                    token.ThrowIfCancellationRequested();
                    var prepared=draftBuilder?.Invoke(problemText,stepsText,diagnosticText)??BugReportDraft.Create(problemText,stepsText,diagnosticText,privatePaths);
                    token.ThrowIfCancellationRequested();return prepared;
                },token);
                if(closed||requestedRevision!=revision)return;
                draft=result;appliedRevision=requestedRevision;RefreshNotice();
            }
            catch(OperationCanceledException)when(closed){}
            catch(Exception error){if(!closed&&requestedRevision==revision)notice.Text="Не удалось подготовить отчёт: "+DiagnosticReport.Redact(error.Message,privatePaths);}
            finally
            {
                building=false;
                if(!closed&&queued&&!loading){debounce.Stop();debounce.Start();}
            }
        }
        debounce.Tick+=async(_,_)=>{debounce.Stop();await BuildDraft();};
        void Copy(string text){if(copyReport!=null)copyReport(text);else Clipboard.SetText(text);}
        copy.Click+=(_,_)=>
        {
            if(!Ready())return;
            try{Copy(draft!.FullReport);notice.Text="Полный очищенный отчёт скопирован.";}
            catch(Exception error){notice.Text="Не удалось скопировать отчёт: "+DiagnosticReport.Redact(error.Message,privatePaths);}
        };
        save.Click+=async(_,_)=>
        {
            if(saving||!Ready())return;
            saving=true;RefreshActions();
            try
            {
                var report=draft!.FullReport;
                if(saveAsyncReport!=null){await saveAsyncReport(report,token);if(!closed)notice.Text="Отчёт сохранён.";return;}
                if(saveReport!=null){saveReport(report);notice.Text="Отчёт сохранён.";return;}
                var picker=new SaveFileDialog{Title="Сохранить отчёт об ошибке",FileName="Kachalka-bug-report.txt",Filter="Текстовый отчёт (*.txt)|*.txt",DefaultExt=".txt",AddExtension=true};
                if(picker.ShowDialog(window)!=true)return;
                await File.WriteAllTextAsync(picker.FileName,report,token);
                if(!closed)notice.Text="Отчёт сохранён. Его можно приложить к сообщению об ошибке.";
            }
            catch(OperationCanceledException)when(closed){}
            catch(Exception error){if(!closed)notice.Text="Не удалось сохранить отчёт: "+DiagnosticReport.Redact(error.Message,privatePaths);}
            finally{saving=false;RefreshActions();}
        };
        open.Click+=(_,_)=>
        {
            try
            {
                if(!Ready()||draft?.FormUri is not Uri uri)return;
                if(draft.RequiresClipboard)Copy(draft.FullReport);
                if(openBrowser!=null)openBrowser(uri);else Process.Start(new ProcessStartInfo(uri.AbsoluteUri){UseShellExecute=true});
                notice.Text="Форма открыта в браузере. Проверь данные и нажми отправку на GitHub.";
            }
            catch(Exception error){notice.Text="Не удалось открыть форму. Можно скопировать или сохранить отчёт: "+DiagnosticReport.Redact(error.Message,privatePaths);}
        };
        problem.TextChanged+=(_,_)=>ScheduleDraft();steps.TextChanged+=(_,_)=>ScheduleDraft();preview.TextChanged+=(_,_)=>ScheduleDraft();
        window.Closed+=(_,_)=>{closed=true;debounce.Stop();cts.Cancel();cts.Dispose();};
        window.Loaded+=async(_,_)=>
        {
            try
            {
                var report=collectReport==null?await DiagnosticReport.CreateAsync(prefs,downloads,token):await collectReport(token);
                var cleaned=await Task.Run(()=>DiagnosticReport.Redact(report,privatePaths),token);
                if(closed)return;preview.Text=cleaned;preview.IsReadOnly=false;loading=false;ScheduleDraft();problem.Focus();
            }
            catch(OperationCanceledException)when(closed){}
            catch(Exception error)
            {
                if(closed)return;
                preview.Text="Не удалось прочитать логи: "+DiagnosticReport.Redact(error.Message,privatePaths);preview.IsReadOnly=false;loading=false;ScheduleDraft();
            }
        };
        RefreshNotice();return window;
    }
}
