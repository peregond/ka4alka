using System.Windows;
using System.Windows.Controls;
namespace Kachalka;

// Native message-box button language follows Windows, not the app preference.
// Keep native dialogs for Russian and use the shared app controls for English.
public static class LocalizedDialog
{
    public static MessageBoxResult Show(string text,string caption)=>Show(null,text,caption);
    public static MessageBoxResult Show(Window? owner,string text,string caption,MessageBoxButton buttons=MessageBoxButton.OK,MessageBoxImage image=MessageBoxImage.None,MessageBoxResult defaultResult=MessageBoxResult.None)
    {
        if(AppLanguage.Current!="en")return owner==null?MessageBox.Show(text,caption,buttons,image,defaultResult):MessageBox.Show(owner,text,caption,buttons,image,defaultResult);
        var dialog=Create(owner,text,caption,buttons,defaultResult,out var result);
        dialog.ShowDialog();return result();
    }
    internal static Window Create(Window? owner,string text,string caption,MessageBoxButton buttons,MessageBoxResult defaultResult,out Func<MessageBoxResult> result)
    {
        var selected=buttons is MessageBoxButton.YesNo?MessageBoxResult.No:MessageBoxResult.Cancel;
        var window=new Window{Title=AppLanguage.T(caption),Width=Math.Min(480,SystemParameters.WorkArea.Width-32),SizeToContent=SizeToContent.Height,MaxHeight=Math.Max(200,SystemParameters.WorkArea.Height-40),ResizeMode=ResizeMode.NoResize,ShowInTaskbar=owner==null,Owner=owner,WindowStartupLocation=owner==null?WindowStartupLocation.CenterScreen:WindowStartupLocation.CenterOwner};
        var panel=new StackPanel{Margin=new(24)};
        var message=new TextBlock{Text=AppLanguage.T(text),TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,20)};message.SetResourceReference(TextBlock.ForegroundProperty,"Text");panel.Children.Add(message);
        var actions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};panel.Children.Add(actions);
        var choices=buttons switch{MessageBoxButton.YesNo=>new[]{MessageBoxResult.Yes,MessageBoxResult.No},MessageBoxButton.YesNoCancel=>new[]{MessageBoxResult.Yes,MessageBoxResult.No,MessageBoxResult.Cancel},MessageBoxButton.OKCancel=>new[]{MessageBoxResult.OK,MessageBoxResult.Cancel},_=>new[]{MessageBoxResult.OK}};
        foreach(var choice in choices)
        {
            var button=new Button{Content=choice.ToString(),MinWidth=72,Margin=new(8,0,0,0),IsDefault=choice==(defaultResult==MessageBoxResult.None?choices[0]:defaultResult),IsCancel=choice is MessageBoxResult.No or MessageBoxResult.Cancel};
            System.Windows.Automation.AutomationProperties.SetName(button,choice.ToString());button.Click+=(_,_)=>{selected=choice;window.Close();};actions.Children.Add(button);
        }
        window.Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};result=()=>selected;return window;
    }
}
