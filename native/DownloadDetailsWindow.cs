using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Kachalka;

public partial class MainWindow
{
    void DownloadDetails(object sender,RoutedEventArgs e)
    {
        var item=(DownloadItem)((Button)sender).Tag;
        downloads.Update();
        var window=new Window
        {
            Title="Файлы · "+item.DisplayName,Owner=this,
            Width=Math.Clamp(ActualWidth-30,320,760),Height=Math.Clamp(ActualHeight-30,280,600),
            MinWidth=320,MinHeight=280,WindowStartupLocation=WindowStartupLocation.CenterOwner,
            UseLayoutRounding=true
        };
        window.Resources["IconFile"]=FindResource("IconFile");
        window.SetResourceReference(Window.BackgroundProperty,"Bg");
        window.SetResourceReference(Window.ForegroundProperty,"Text");
        var panel=new DockPanel{Margin=new(18)};
        window.Content=panel;

        var header=new DockPanel{Margin=new(0,0,0,10)};
        DockPanel.SetDock(header,Dock.Top);panel.Children.Add(header);
        var heading=Text(item.DisplayName,20);heading.FontWeight=FontWeights.SemiBold;
        heading.TextWrapping=TextWrapping.NoWrap;heading.TextTrimming=TextTrimming.CharacterEllipsis;
        heading.ToolTip=item.DisplayName;heading.Margin=new(0);
        header.Children.Add(heading);

        var totals=new Border{CornerRadius=new(10),Padding=new(10,8,10,8),Margin=new(0,0,0,8)};
        totals.SetResourceReference(Border.BackgroundProperty,"AccentSoft");
        var summary=new StackPanel();totals.Child=summary;
        var status=Text("",12);status.FontWeight=FontWeights.Medium;status.Margin=new(0,0,0,3);
        var bytes=Text("",11,true);bytes.Margin=new(0,0,0,5);
        var totalProgress=new ProgressBar{Maximum=100,Height=4,BorderThickness=new(0)};
        totalProgress.SetResourceReference(Control.ForegroundProperty,"Accent");
        totalProgress.SetResourceReference(Control.BackgroundProperty,"Edge");
        summary.Children.Add(status);summary.Children.Add(bytes);summary.Children.Add(totalProgress);
        DockPanel.SetDock(totals,Dock.Top);panel.Children.Add(totals);

        var filter=new TextBox{Height=34,Padding=new(10,6,10,6),Margin=new(0,0,0,9),ToolTip="Найти серию или файл по названию и папке"};
        filter.SetResourceReference(Control.ForegroundProperty,"Text");
        filter.SetResourceReference(Control.BackgroundProperty,"Panel");
        filter.SetResourceReference(Control.BorderBrushProperty,"Edge");
        AutomationProperties.SetName(filter,"Найти файл в раздаче");
        var searchHost=new Grid();searchHost.Children.Add(filter);
        var placeholder=Text("Найти серию или файл…",12,true);
        placeholder.Margin=new(11,0,11,9);placeholder.VerticalAlignment=VerticalAlignment.Center;
        placeholder.IsHitTestVisible=false;placeholder.TextWrapping=TextWrapping.NoWrap;
        searchHost.Children.Add(placeholder);
        DockPanel.SetDock(searchHost,Dock.Top);panel.Children.Add(searchHost);

        var footer=new DockPanel{LastChildFill=false,Margin=new(0,10,0,0)};
        DockPanel.SetDock(footer,Dock.Bottom);panel.Children.Add(footer);
        var close=ActionButton("Закрыть","IconBack",()=>window.Close());
        close.Margin=new(0);close.Padding=new(10,6,10,6);close.IsCancel=true;
        DockPanel.SetDock(close,Dock.Right);footer.Children.Add(close);
        var folder=ActionButton("Папка","IconFolder",()=>OpenFolder(new Button{Tag=item},new RoutedEventArgs()));
        folder.Margin=new(0,0,8,0);folder.Padding=new(10,6,10,6);folder.ToolTip=item.Folder;
        folder.IsEnabled=!string.IsNullOrWhiteSpace(item.Folder);footer.Children.Add(folder);

        var rows=new ObservableCollection<DownloadFileRow>();
        var view=CollectionViewSource.GetDefaultView(rows);
        view.Filter=value=>value is DownloadFileRow row&&(filter.Text.Length==0||row.RelativePath.Contains(filter.Text.Trim(),StringComparison.CurrentCultureIgnoreCase));
        var list=new ListBox{ItemsSource=view,ItemTemplate=DownloadFileTemplate(),Margin=new(0),SelectionMode=SelectionMode.Single};
        list.SetResourceReference(Control.ForegroundProperty,"Text");
        list.SetResourceReference(Control.BackgroundProperty,"Bg");
        ScrollViewer.SetVerticalScrollBarVisibility(list,ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Disabled);
        ScrollViewer.SetCanContentScroll(list,true);
        VirtualizingPanel.SetIsVirtualizing(list,true);
        VirtualizingPanel.SetVirtualizationMode(list,VirtualizationMode.Recycling);
        VirtualizingPanel.SetScrollUnit(list,ScrollUnit.Pixel);
        var itemStyle=new Style(typeof(ListBoxItem));
        itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch));
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(0)));
        itemStyle.Setters.Add(new Setter(UIElement.FocusableProperty,true));
        itemStyle.Setters.Add(new Setter(Control.TemplateProperty,new ControlTemplate(typeof(ListBoxItem)){VisualTree=new FrameworkElementFactory(typeof(ContentPresenter))}));
        list.ItemContainerStyle=itemStyle;
        AutomationProperties.SetName(list,"Файлы загрузки");
        var content=new Grid();content.Children.Add(list);
        var empty=Text("",13,true);empty.TextAlignment=TextAlignment.Center;
        empty.HorizontalAlignment=HorizontalAlignment.Center;empty.VerticalAlignment=VerticalAlignment.Center;
        empty.MaxWidth=310;empty.Margin=new(12);empty.IsHitTestVisible=false;content.Children.Add(empty);
        panel.Children.Add(content);

        void EmptyState()
        {
            placeholder.Visibility=filter.Text.Length==0?Visibility.Visible:Visibility.Collapsed;
            empty.Text=rows.Count==0?"Файлы появятся после получения метаданных раздачи.":"По этому запросу файлов нет.";
            empty.Visibility=view.IsEmpty?Visibility.Visible:Visibility.Collapsed;
        }
        void Refresh()
        {
            downloads.Update();
            var existing=rows.ToDictionary(row=>row.Key);
            var current=item.Files.ToArray();
            for(var index=0;index<current.Length;index++)
            {
                var file=current[index];
                if(!existing.TryGetValue((file.Name,file.FullPath),out var row))
                {
                    row=new DownloadFileRow(file);rows.Insert(index,row);
                }
                else
                {
                    row.Update(file);
                    var oldIndex=rows.IndexOf(row);if(oldIndex!=index)rows.Move(oldIndex,index);
                }
            }
            while(rows.Count>current.Length)rows.RemoveAt(rows.Count-1);
            var completed=current.Count(file=>file.Progress>=100);
            status.Text=current.Length==0?"Получаем список файлов…":$"Готово файлов: {completed} из {current.Length}";
            var size=current.Sum(file=>(decimal)Math.Max(0,file.Size));
            var received=current.Sum(file=>(decimal)Math.Max(0,file.Size)*(decimal)(double.IsFinite(file.Progress)?Math.Clamp(file.Progress,0,100):0)/100);
            bytes.Text=size==0?"Состав и размеры файлов появятся вместе с метаданными.":$"Скачано {DownloadService.FormatBytes((long)Math.Min(received,long.MaxValue))} из {DownloadService.FormatBytes((long)Math.Min(size,long.MaxValue))}";
            totalProgress.IsIndeterminate=current.Length==0;
            totalProgress.Value=size>0?(double)(received/size*100):0;
            EmptyState();
        }
        filter.TextChanged+=(_,_)=>{view.Refresh();if(list.Items.Count>0)list.ScrollIntoView(list.Items[0]);EmptyState();};
        void Fit()
        {
            panel.Margin=new(window.ActualWidth<440?12:18);
            heading.FontSize=window.ActualWidth<440?17:20;
            bytes.Visibility=window.ActualHeight<380?Visibility.Collapsed:Visibility.Visible;
            header.Margin=new(0,0,0,window.ActualHeight<380?7:10);
        }
        window.SizeChanged+=(_,_)=>Fit();
        window.PreviewKeyDown+=(_,args)=>
        {
            if(args.Key==Key.F&&Keyboard.Modifiers.HasFlag(ModifierKeys.Control)){filter.Focus();filter.SelectAll();args.Handled=true;}
        };
        var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(2)};
        timer.Tick+=(_,_)=>Refresh();window.Closed+=(_,_)=>timer.Stop();
        Refresh();timer.Start();window.ShowDialog();
    }

    static DataTemplate DownloadFileTemplate()
    {
        var template=new DataTemplate(typeof(DownloadFileRow));
        var frame=new FrameworkElementFactory(typeof(Border)){Name="FileFrame"};
        frame.SetResourceReference(Border.BackgroundProperty,"Panel");
        frame.SetResourceReference(Border.BorderBrushProperty,"Edge");
        frame.SetValue(Border.BorderThicknessProperty,new Thickness(1));
        frame.SetValue(Border.CornerRadiusProperty,new CornerRadius(9));
        frame.SetValue(Border.PaddingProperty,new Thickness(10,8,10,8));
        frame.SetValue(FrameworkElement.MarginProperty,new Thickness(0,0,0,6));
        var dock=new FrameworkElementFactory(typeof(DockPanel));frame.AppendChild(dock);
        var icon=new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
        icon.SetResourceReference(System.Windows.Shapes.Path.DataProperty,"IconFile");
        icon.SetResourceReference(Shape.StrokeProperty,"Muted");
        icon.SetValue(Shape.StrokeThicknessProperty,1.6d);icon.SetValue(Shape.StretchProperty,Stretch.Uniform);
        icon.SetValue(FrameworkElement.WidthProperty,16d);icon.SetValue(FrameworkElement.HeightProperty,18d);
        icon.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Top);
        icon.SetValue(FrameworkElement.MarginProperty,new Thickness(0,2,9,0));icon.SetValue(DockPanel.DockProperty,Dock.Left);
        dock.AppendChild(icon);
        var stack=new FrameworkElementFactory(typeof(StackPanel));dock.AppendChild(stack);
        FrameworkElementFactory Label(string binding,double size,string brush)
        {
            var text=new FrameworkElementFactory(typeof(TextBlock));text.SetBinding(TextBlock.TextProperty,new Binding(binding));
            text.SetResourceReference(TextBlock.ForegroundProperty,brush);text.SetValue(TextBlock.FontSizeProperty,size);
            text.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);return text;
        }
        var title=new FrameworkElementFactory(typeof(DockPanel));stack.AppendChild(title);
        var state=Label("StateText",11,"Accent");state.SetValue(DockPanel.DockProperty,Dock.Right);
        state.SetValue(FrameworkElement.MarginProperty,new Thickness(9,0,0,0));title.AppendChild(state);
        var name=Label("Name",13,"Text");name.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);
        name.SetBinding(FrameworkElement.ToolTipProperty,new Binding("RelativePath"));title.AppendChild(name);
        var path=Label("Directory",10,"Muted");path.SetValue(FrameworkElement.MarginProperty,new Thickness(0,2,0,4));
        path.SetBinding(FrameworkElement.ToolTipProperty,new Binding("RelativePath"));stack.AppendChild(path);
        var progress=new FrameworkElementFactory(typeof(ProgressBar));progress.SetValue(ProgressBar.MaximumProperty,100d);
        progress.SetValue(FrameworkElement.HeightProperty,4d);progress.SetValue(Control.BorderThicknessProperty,new Thickness(0));
        progress.SetResourceReference(Control.ForegroundProperty,"Accent");progress.SetResourceReference(Control.BackgroundProperty,"Edge");
        progress.SetBinding(ProgressBar.ValueProperty,new Binding("Progress"){Mode=BindingMode.OneWay});stack.AppendChild(progress);
        var amount=Label("DownloadedText",11,"Muted");amount.SetValue(FrameworkElement.MarginProperty,new Thickness(0,4,0,0));stack.AppendChild(amount);
        template.VisualTree=frame;
        foreach(var stateName in new[]{"IsMouseOver","IsSelected"})
        {
            var trigger=new DataTrigger{Binding=new Binding(stateName){RelativeSource=new RelativeSource(RelativeSourceMode.FindAncestor,typeof(ListBoxItem),1)},Value=true};
            trigger.Setters.Add(new Setter(Border.BackgroundProperty,new DynamicResourceExtension(stateName=="IsSelected"?"Selected":"Hover"),"FileFrame"));
            if(stateName=="IsSelected")trigger.Setters.Add(new Setter(Border.BorderBrushProperty,new DynamicResourceExtension("Accent"),"FileFrame"));
            template.Triggers.Add(trigger);
        }
        return template;
    }

    sealed class DownloadFileRow:INotifyPropertyChanged
    {
        DownloadFile file;
        public DownloadFileRow(DownloadFile value){file=value;}
        public (string Name,string FullPath) Key=>(file.Name,file.FullPath);
        public string RelativePath=>file.Name.Replace('\\','/');
        public string Name=>RelativePath[(RelativePath.LastIndexOf('/')+1)..];
        public string Directory=>RelativePath.LastIndexOf('/') is var index&&index>=0?RelativePath[..index]:"В корне раздачи";
        public double Progress=>double.IsFinite(file.Progress)?Math.Clamp(file.Progress,0,100):0;
        public string StateText=>Progress>=100?"Готово":$"{Progress:F1}%";
        public string DownloadedText=>$"{DownloadService.FormatBytes((long)((decimal)Math.Max(0,file.Size)*(decimal)Progress/100))} из {DownloadService.FormatBytes(Math.Max(0,file.Size))}";
        public event PropertyChangedEventHandler? PropertyChanged;
        public void Update(DownloadFile value)
        {
            if(file==value)return;file=value;PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(null));
        }
    }
}
