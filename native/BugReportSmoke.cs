using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Kachalka;

public partial class MainWindow
{
    public async Task BugReportSmokeTest(string output)
    {
        Directory.CreateDirectory(output);var originalLight=prefs.Light;var windows=new List<Window>();var checks=new List<string>();
        void Check(bool value,string message){if(!value)throw new Exception(message);checks.Add(message);}
        async Task Settle(Window dialog){dialog.UpdateLayout();await dialog.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(80);dialog.UpdateLayout();}
        async Task Until(Func<bool> condition,string message){var until=DateTime.UtcNow.AddSeconds(5);while(!condition()&&DateTime.UtcNow<until)await Task.Delay(20);Check(condition(),message);}
        T Find<T>(Window dialog,string name)where T:FrameworkElement=>FindVisual<T>(dialog,element=>element.Name==name)??throw new Exception("Missing bug-report field "+name);
        try
        {
            var completion=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);var opened=new List<Uri>();var copied=new List<string>();var saved=new List<string>();
            var holdDraft=false;var draftWorker=false;using var draftEntered=new ManualResetEventSlim();using var releaseDraft=new ManualResetEventSlim();
            var saveCalls=0;TaskCompletionSource? heldSave=null;
            var dialog=CreateBugReport(_=>completion.Task,uri=>opened.Add(uri),report=>copied.Add(report),null,(p,s,d)=>
            {
                draftWorker=!Dispatcher.CheckAccess();
                if(holdDraft){draftEntered.Set();if(!releaseDraft.Wait(TimeSpan.FromSeconds(5)))throw new Exception("Held draft worker timed out.");}
                return BugReportDraft.Create(p,s,d,[prefs.Folder,Preferences.DataDir]);
            },async(report,token)=>{saveCalls++;saved.Add(report);if(heldSave!=null)await heldSave.Task.WaitAsync(token);});windows.Add(dialog);dialog.Show();await Settle(dialog);
            var problem=Find<TextBox>(dialog,"BugReportProblem");problem.Text="Загрузка остановилась после сна";
            var steps=Find<TextBox>(dialog,"BugReportSteps");steps.Text="1. Начать загрузку\n2. Перевести ноутбук в сон\n3. Вернуться";
            Check(problem.IsEnabled&&!Find<Button>(dialog,"OpenBugReport").IsEnabled,"Description remains usable while reports load, and export waits for review.");
            Check(opened.Count==0&&copied.Count==0&&saved.Count==0,"Opening the native preview never opens a browser, sends an issue or writes a report.");
            var beat=false;await Dispatcher.InvokeAsync(()=>beat=true,DispatcherPriority.Input);Check(beat,"Input dispatcher responds while report collection is pending.");
            completion.SetResult("Наблюдения\n"+prefs.Folder+"\ntoken=private-fixture-token\nhttps://tracker.test/private-fixture-passkey/announce");await Settle(dialog);
            var preview=Find<TextBox>(dialog,"BugReportPreview");var open=Find<Button>(dialog,"OpenBugReport");
            await Until(()=>open.IsEnabled,"Reviewed initial report finishes its worker validation.");
            Check(!preview.IsReadOnly&&open.IsEnabled&&!preview.Text.Contains("private-fixture-token")&&!preview.Text.Contains("private-fixture-passkey")&&!preview.Text.Contains(prefs.Folder),"Loaded preview is editable and removes private folders and tracker credentials.");
            preview.Text+="\nПроверенная пользователем заметка";
            Check(!open.IsEnabled,"Editing immediately disables export until the current revision is sanitized.");
            await Until(()=>open.IsEnabled,"Edited report validates before browser export.");open.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Check(opened.Count==1&&opened[0].Host=="github.com"&&opened[0].AbsolutePath=="/peregond/ka4alka/issues/new"&&Uri.UnescapeDataString(opened[0].Query).Contains("Проверенная пользователем заметка")&&copied.Count==0,"Explicit action opens a meaningful reviewed report through the fake browser callback only.");
            Find<Button>(dialog,"CopyBugReport").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));Find<Button>(dialog,"SaveBugReport").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Check(copied.Count==1&&saved.Count==1&&saved[0]==copied[0]&&saved[0].Contains("Как повторить"),"Copy and save callbacks preserve the complete report and reproduction steps.");
            holdDraft=true;preview.Text=string.Concat(Enumerable.Repeat("Наблюдение: соединение ожидало участников\n",6000))+"Последний факт";
            await Until(()=>draftEntered.IsSet,"Large edited preview starts one background draft build.");
            Check(draftWorker&&!open.IsEnabled,"Large report sanitization runs off the dispatcher and keeps stale export disabled.");
            var inputBeat=false;await Dispatcher.InvokeAsync(()=>inputBeat=true,DispatcherPriority.Input);
            dialog.Activate();problem.BringIntoView();dialog.UpdateLayout();
            SetForegroundWindow(new WindowInteropHelper(dialog).Handle);
            var inputPoint=problem.PointToScreen(new Point(problem.ActualWidth*.5,problem.ActualHeight*.5));
            if(!SetCursorPos((int)inputPoint.X,(int)inputPoint.Y))throw new Exception("Cannot position native report input.");
            mouse_event(2,0,0,0,0);await Task.Delay(70);mouse_event(4,0,0,0,0);await Task.Delay(100);
            problem.Text="Актуальное описание после быстрого редактирования";
            steps.Text="Актуальные шаги\n2. Проверить подключение";
            Check(inputBeat&&problem.IsKeyboardFocusWithin&&!open.IsEnabled,"Native mouse focus and input dispatcher remain responsive while a loaded large report validates in the worker.");
            holdDraft=false;releaseDraft.Set();await Until(()=>open.IsEnabled,"Coalesced edits publish only the latest validated report revision.");
            Check(open.Content.ToString()=="Скопировать и открыть GitHub","Large reports expose the explicit copy-and-open action.");
            open.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));Check(opened.Count==2&&copied.Count==2&&copied[^1].EndsWith("Последний факт")&&opened[^1].AbsoluteUri.Length<=BugReportDraft.MaximumUriLength,"Overflow copies the complete reviewed report before opening a bounded form link.");
            Check(copied[^1].Contains("Актуальное описание после быстрого редактирования")&&copied[^1].Contains("Актуальные шаги"),"A discarded older build cannot export stale description or steps.");
            heldSave=new(TaskCreationOptions.RunContinuationsAsynchronously);var save=Find<Button>(dialog,"SaveBugReport");save.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            problem.Text="Изменённое описание во время сохранения";
            await Until(()=>Find<Button>(dialog,"CopyBugReport").IsEnabled,"Editing during a held file write can validate a new report.");
            save.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));Check(saveCalls==2&&!save.IsEnabled,"Revalidation and duplicate clicks never overlap a pending file write.");
            heldSave.SetResult();await Until(()=>save.IsEnabled,"Save becomes available after the pending write finishes.");heldSave=null;
            problem.Text=new string('я',4000);
            await Until(()=>Find<Button>(dialog,"CopyBugReport").IsEnabled,"Oversized description completes validation for copy/save.");
            Check(!open.IsEnabled&&Find<Button>(dialog,"CopyBugReport").IsEnabled,"An oversized problem keeps copy/save available and never opens a silently truncated description.");
            problem.Text="Загрузка остановилась после сна";preview.Text="Проверенная диагностика\nПодключено 0 участников\nПуть скрыт";
            await Until(()=>open.IsEnabled,"Screenshot report finishes validation.");
            foreach(var light in new[]{false,true})
            foreach(var size in new[]{("wide",680d,700d),("narrow",360d,440d)})
            {
                prefs.Light=light;ApplyTheme();dialog.Width=size.Item2;dialog.Height=size.Item3;await Settle(dialog);
                var scroll=FindVisual<ScrollViewer>(dialog,s=>AutomationProperties.GetName(s)=="Предпросмотр отчёта об ошибке")??throw new Exception("Missing report scroll viewer.");
                Check(scroll.ViewportWidth>240&&((FrameworkElement)scroll.Content).ActualWidth<=scroll.ViewportWidth+1,"Bug-report contents fit "+size.Item1+" "+(light?"light":"dark")+" without horizontal scrolling.");
                foreach(var button in new[]{open,Find<Button>(dialog,"CopyBugReport"),Find<Button>(dialog,"SaveBugReport")})
                {
                    var point=button.TranslatePoint(new Point(),dialog);Check(point.X>=0&&point.X+button.ActualWidth<=dialog.ActualWidth+1,"Report footer action fits inside the "+size.Item1+" dialog.");
                }
                var bitmap=new RenderTargetBitmap((int)Math.Ceiling(dialog.ActualWidth),(int)Math.Ceiling(dialog.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(dialog);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file=File.Create(Path.Combine(output,"bug-report-"+size.Item1+"-"+(light?"light":"dark")+".png"));encoder.Save(file);
            }
            var canceled=false;
            var pending=CreateBugReport(token=>{token.Register(()=>canceled=true);return Task.Delay(Timeout.Infinite,token).ContinueWith(_=>"",token);},uri=>throw new Exception("Unexpected browser action."));windows.Add(pending);pending.Show();await Settle(pending);pending.Close();await Task.Delay(30);
            Check(canceled,"Closing a pending report cancels background collection.");
            File.WriteAllText(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(new{Passed=true,ExternalRequests=0,Checks=checks},new JsonSerializerOptions{WriteIndented=true}));
        }
        finally
        {
            foreach(var window in windows)if(window.IsVisible)window.Close();prefs.Light=originalLight;ApplyTheme();
        }
        Close();
    }
}
