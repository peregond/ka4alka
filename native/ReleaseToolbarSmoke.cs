using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Kachalka;

public partial class MainWindow
{
    object CheckReleaseToolbar(string stage,bool checking,bool expanded)
    {
        var toolbar=FindVisual<WrapPanel>(Body,x=>x.Name=="ReleaseToolbar")??throw new Exception(stage+": release toolbar is missing.");
        // Refresh, sources and the poor-quality switch hide behind "Ещё фильтры" until a scan runs or the panel is opened.
        var reopened=false;
        if(!toolbar.IsVisible&&current!=null)
        {
            reopened=true;ReleaseSelection(current.Id).More=true;Render();UpdateLayout();
            toolbar=FindVisual<WrapPanel>(Body,x=>x.Name=="ReleaseToolbar")??throw new Exception(stage+": release toolbar is missing after opening more filters.");
        }
        var toggle=FindVisual<Button>(toolbar,x=>AutomationProperties.GetName(x)=="Показать состояние источников")??throw new Exception(stage+": sources action is outside the toolbar.");
        var retry=FindVisual<Button>(toolbar,x=>AutomationProperties.GetName(x)=="Обновить варианты загрузки")??throw new Exception(stage+": refresh action is outside the toolbar.");
        var minimum=FindVisual<ComboBox>(toolbar,x=>AutomationProperties.GetName(x)=="Качество раздач")??throw new Exception(stage+": minimum quality is outside the toolbar.");
        var hide=FindVisual<CheckBox>(toolbar,x=>AutomationProperties.GetName(x)=="Скрыть плохое качество")??throw new Exception(stage+": poor-quality control is outside the toolbar.");
        if(VisualElements<Button>(Body).Count(x=>AutomationProperties.GetName(x)=="Обновить варианты загрузки")!=1||VisualElements<CheckBox>(Body).Count(x=>AutomationProperties.GetName(x)=="Скрыть плохое качество")!=1)
            throw new Exception(stage+": release actions are duplicated.");
        if(retry.IsEnabled==checking)throw new Exception(stage+": refresh availability does not follow the source scan.");
        var progress=FindVisual<Border>(toolbar,x=>AutomationProperties.GetName(x)=="Поиск раздач")??throw new Exception(stage+": source progress is outside the toolbar.");
        if((progress.Visibility==Visibility.Visible)!=checking)throw new Exception(stage+": source progress did not settle.");
        if(progress.Width>120||progress.Background!=null||progress.BorderThickness!=new Thickness(0)||progress.Padding!=new Thickness(0)||VisualElements<ProgressBar>(progress).Any(x=>x.Height>4))
            throw new Exception(stage+": source progress still occupies a separate card.");
        var rows=FindVisual<StackPanel>(Body,x=>x.Name=="ReleaseSourceDetails")??throw new Exception(stage+": source details are missing.");
        if((rows.Visibility==Visibility.Visible)!=expanded||!ReferenceEquals(rows.Parent,toolbar.Parent))throw new Exception(stage+": source details are not revealed on demand beside the filters.");
        if(expanded&&releaseViews.TryGetValue(current?.Id??0,out var view)&&view.Sources.Length>0&&rows.Children.Count!=view.Sources.Length)
            throw new Exception(stage+": some per-source states were dropped.");
        if(VisualElements<TextBlock>(Body).Any(x=>x.Text.StartsWith("Варианты получены ",StringComparison.Ordinal)||x.Text.StartsWith("Сохранённая подборка:",StringComparison.Ordinal)||x.Text.Contains("последний ответ",StringComparison.Ordinal)))
            throw new Exception(stage+": release receipt dates still occupy the detail page.");
        foreach(var element in new FrameworkElement[]{toggle,retry,minimum,hide})
        {
            var bounds=element.TransformToAncestor(toolbar).TransformBounds(new Rect(new Point(),element.RenderSize));
            if(!element.IsVisible||bounds.Width<=0||bounds.Left<-.5||bounds.Right>toolbar.ActualWidth+1||bounds.Top<-.5||bounds.Bottom>toolbar.ActualHeight+1)
                throw new Exception(stage+": a release toolbar action is clipped.");
        }
        if(toolbar.ActualWidth>1000&&toolbar.ActualHeight>64)throw new Exception(stage+": desktop source actions no longer fit in a compact filter row.");
        var result=new{Stage=stage,BodyWidth=Math.Round(Body.ActualWidth),ToolbarHeight=Math.Round(toolbar.ActualHeight),SourceDetailsVisible=expanded,Checking=checking,RefreshEnabled=retry.IsEnabled,InlineSourceActions=true,ReceiptDatesHidden=true};
        if(reopened&&current!=null){ReleaseSelection(current.Id).More=false;Render();UpdateLayout();}
        return result;
    }
}
