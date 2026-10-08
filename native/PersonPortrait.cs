using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.IO;
using FilePath=System.IO.Path;
namespace Kachalka;

public partial class MainWindow
{
    Border PersonPortrait(CinemaPerson person,double width,double height,MediaItem? origin=null)
    {
        var grid=new Grid();var fallback=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,IsHitTestVisible=false};
        var silhouette=new System.Windows.Shapes.Path{Data=Geometry.Parse("M12,3 A4,4 0 1 1 11.99,3 M4,21 L4,19 C4,13 20,13 20,19 L20,21"),Width=34,Height=34,Stretch=Stretch.Uniform,StrokeThickness=1.5,Opacity=.45};
        silhouette.SetResourceReference(Shape.StrokeProperty,"Muted");fallback.Children.Add(silhouette);
        var absent=Text("Нет фото",10,true);absent.HorizontalAlignment=HorizontalAlignment.Center;absent.Margin=new(0,9,0,0);absent.Opacity=.7;fallback.Children.Add(absent);grid.Children.Add(fallback);
        AutomationProperties.SetName(fallback,"Нет фотографии: "+person.Name);
        var image=new Image{Stretch=Stretch.UniformToFill};AutomationProperties.SetName(image,"Фотография: "+person.Name);grid.Children.Add(image);
        var progress=Text("Фото…",9,true);progress.HorizontalAlignment=HorizontalAlignment.Center;progress.VerticalAlignment=VerticalAlignment.Bottom;progress.Margin=new(0,0,0,7);progress.Visibility=Visibility.Collapsed;progress.IsHitTestVisible=false;grid.Children.Add(progress);
        var frame=new Border{Width=width,Height=height,CornerRadius=new(11),ClipToBounds=true,Child=grid,Margin=new(0,0,0,8),BorderThickness=new(1)};
        frame.SetResourceReference(Border.BackgroundProperty,"PanelAlt");frame.SetResourceReference(Border.BorderBrushProperty,"EdgeSoft");frame.SizeChanged+=(_,_)=>ClipPoster(frame);
        CancellationTokenSource? request=null;bool attempted=false;string? portraitPath=null;
        Button? portraitOwner=null;bool cancelledByUnload=false;
        var scrollers=new List<ScrollViewer>();
        bool InViewport()
        {
            if(!frame.IsLoaded||frame.ActualWidth<=0||frame.ActualHeight<=0)return false;
            try
            {
                for(DependencyObject? parent=VisualTreeHelper.GetParent(image);parent!=null;parent=VisualTreeHelper.GetParent(parent))
                    if(parent is ScrollViewer scroll&&!frame.TransformToAncestor(scroll).TransformBounds(new Rect(new Point(),frame.RenderSize)).IntersectsWith(new Rect(0,0,scroll.ActualWidth,scroll.ActualHeight)))return false;
                return true;
            }
            catch(InvalidOperationException){return false;}
        }
        var retryMenu=new ContextMenu();retryMenu.SetResourceReference(Control.BackgroundProperty,"Panel");retryMenu.SetResourceReference(Control.ForegroundProperty,"Text");
        var retry=new MenuItem{Header="Повторить загрузку фотографии"};retry.Click+=(_,_)=>{_ = LoadPhoto(true);};retryMenu.Items.Add(retry);
        AutomationProperties.SetName(retry,"Повторить фотографию: "+person.Name);
        void RetryMenu(bool available)
        {
            frame.ContextMenu=available?retryMenu:null;frame.Focusable=available&&portraitOwner==null;
            if(portraitOwner!=null&&(portraitOwner.ContextMenu==null||ReferenceEquals(portraitOwner.ContextMenu,retryMenu)))portraitOwner.ContextMenu=available?retryMenu:null;
        }
        async Task LoadPhoto(bool force=false)
        {
            if(request!=null||!InViewport()||image.Source!=null||attempted&&!force)return;
            attempted=true;cancelledByUnload=false;using var pending=new CancellationTokenSource(TimeSpan.FromSeconds(55));request=pending;RetryMenu(false);progress.Visibility=Visibility.Visible;absent.Visibility=Visibility.Collapsed;frame.ToolTip="Загружаем фотографию…";
            bool unavailable=false;
            try
            {
                for(var attempt=0;attempt<2;attempt++)
                {
                    try
                    {
                        var portrait=await new CinemaPeople(sourceClient).ResolvePortrait(person,pending.Token,origin,forceRefresh:force&&attempt==0);
                        var url=portrait.Url;
                        if(url==null)
                        {
                            unavailable=portrait.Status==PortraitStatus.Unavailable;
                            if(!unavailable)break;
                        }
                        else
                        {
                            var key=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(url)));portraitPath=FilePath.Combine(Preferences.DataDir,"portraits",key+".img");
                            await coverSlots.WaitAsync(pending.Token);
                            try
                            {
                                using var download=CancellationTokenSource.CreateLinkedTokenSource(pending.Token);download.CancelAfter(TimeSpan.FromSeconds(12));
                                var (bitmap,_)=await CoverCache.Load(portraitPath,2*1024*1024,
                                    ct=>sourceClient.Read(new Uri(url),2*1024*1024,ct),bytes=>{using var stream=new MemoryStream(bytes);var result=new BitmapImage();result.BeginInit();result.CacheOption=BitmapCacheOption.OnLoad;result.DecodePixelWidth=(int)Math.Ceiling(width*2);result.StreamSource=stream;result.EndInit();result.Freeze();return result;},download.Token);
                                if(image.IsLoaded&&!pending.IsCancellationRequested){image.Source=bitmap;fallback.Visibility=Visibility.Collapsed;frame.ToolTip=person.Name;}
                                return;
                            }
                            finally{coverSlots.Release();}
                        }
                    }
                    catch(OperationCanceledException)when(pending.IsCancellationRequested){throw;}
                    catch{unavailable=true;}
                    if(attempt==0&&InViewport())await Task.Delay(TimeSpan.FromSeconds(2.5),pending.Token);else break;
                }
            }
            catch(OperationCanceledException)when(pending.IsCancellationRequested){unavailable=image.IsLoaded;}
            finally
            {
                progress.Visibility=Visibility.Collapsed;if(ReferenceEquals(request,pending))request=null;
                if(image.Source==null)
                {
                    absent.Visibility=Visibility.Visible;
                    frame.ToolTip=unavailable?"Фотография временно недоступна. Можно повторить загрузку через меню правой кнопки.":"У источников пока нет фотографии.";
                    RetryMenu(true);
                    // Only unloading resets the automatic attempt. A deadline
                    // must settle into the fallback, rather than restart forever.
                    if(cancelledByUnload&&pending.IsCancellationRequested)
                    {
                        attempted=false;
                        if(image.IsLoaded)_=Dispatcher.BeginInvoke(new Action(VisiblePhoto));
                    }
                }
            }
        }
        void VisiblePhoto(){if(portraitPath!=null&&InViewport())CacheFiles.Touch(portraitPath);_ = LoadPhoto();}
        void Scrolled(object sender,ScrollChangedEventArgs args)=>VisiblePhoto();
        void ViewportResized(object sender,SizeChangedEventArgs args)=>VisiblePhoto();
        image.Loaded+=(_,_)=>
        {
            for(DependencyObject? parent=VisualTreeHelper.GetParent(image);parent!=null;parent=VisualTreeHelper.GetParent(parent))
            {
                if(portraitOwner==null&&parent is Button button)portraitOwner=button;
                if(parent is ScrollViewer scroll){scrollers.Add(scroll);scroll.ScrollChanged+=Scrolled;scroll.SizeChanged+=ViewportResized;}
            }
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,new Action(VisiblePhoto));
        };
        image.SizeChanged+=(_,_)=>VisiblePhoto();
        image.Unloaded+=(_,_)=>{cancelledByUnload=true;request?.Cancel();attempted=false;foreach(var scroll in scrollers){scroll.ScrollChanged-=Scrolled;scroll.SizeChanged-=ViewportResized;}scrollers.Clear();};
        return frame;
    }
}
