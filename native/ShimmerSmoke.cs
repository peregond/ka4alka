using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
namespace Kachalka;
public partial class MainWindow
{
    public async Task ShimmerSmokeTest(string output)
    {
        Directory.CreateDirectory(output);section="Настройки";current=null;metadataLifetime.Cancel();catalogRefreshTimer.Stop();liveRequest?.Cancel();Render();
        var checks=new List<string>();
        void Check(bool pass,string message){if(!pass)throw new Exception(message);checks.Add(message);}
        async Task Settle(){UpdateLayout();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);await Task.Delay(30);}
        Body.Children.Clear();var host=new StackPanel();Body.Children.Add(host);
        var wave=new ShimmerWave(true);var a=new Border{Height=50};var b=new Border{Height=50};
        host.Children.Add(a);host.Children.Add(b);await Settle();
        wave.Show(a,0);wave.Show(b,0);await Settle();
        Check(wave.IsRunning&&wave.LaneCount==1&&ReferenceEquals(a.Background,b.Background),"One shared gradient lane serves multiple loading cards.");
        wave.Hide(a);Check(wave.IsRunning,"Another loading card keeps the shared animation alive.");
        wave.Hide(b);Check(!wave.IsRunning&&a.Visibility==Visibility.Collapsed&&b.Visibility==Visibility.Collapsed,"Last completed card stops the animation.");
        wave.Show(a,0);wave.Pause(true);Check(!wave.IsRunning,"The light rests while the page is scrolled.");wave.Pause(false);Check(wave.IsRunning,"The light goes on after scrolling.");wave.Hide(a);
        wave.Show(a,0);wave.Stop();Check(!wave.IsRunning&&a.Visibility==Visibility.Collapsed,"Theme/Lite reset stops and detaches the prior wave.");
        wave.Show(a,0);host.Children.Remove(a);await Settle();Check(!wave.IsRunning,"Unloading the last card stops its wave.");
        var skeleton=new StackPanel();var shape=new Border{Height=30};skeleton.Children.Add(shape);var placeholder=new ShimmerWave(true);placeholder.Paint(shape,0);placeholder.Run(skeleton);host.Children.Add(skeleton);await Settle();
        Check(placeholder.IsRunning,"A visible loading placeholder starts its animation.");
        skeleton.Visibility=Visibility.Collapsed;await Settle();Check(!placeholder.IsRunning,"Collapsing a filled placeholder stops its animation.");
        var still=new ShimmerWave(false);var quiet=new Border();still.Paint(quiet,0);still.Show(quiet,0);Check(!still.IsRunning&&still.LaneCount==0,"Motion-disabled placeholders create no animation lanes.");
        // Lite mode keeps the loading shimmer (it is only a brush transform), unlike the heavier decorative animations.
        var motionBefore=MotionAllowed;var liteBefore=lightweightMotion;MotionAllowed=true;lightweightMotion=true;
        try
        {
            var lite=NewShimmer();var liteShape=new Border();lite.Paint(liteShape,0);
            Check(lite.LaneCount==(SystemParameters.ClientAreaAnimation?1:0)&&!Motion,"Lite mode keeps the loading shimmer while the heavier animations stay off.");
        }
        finally{MotionAllowed=motionBefore;lightweightMotion=liteBefore;}
        Check(ShimmerWave.Lift(Colors.Black)!=Colors.Black&&ShimmerWave.Lift(Colors.White)!=Colors.White,"The shimmer remains visible in dark and light themes.");
        File.WriteAllText(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(checks));Close();
    }
}
