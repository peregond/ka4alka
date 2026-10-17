using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Controls;
using System.Windows.Markup;
namespace Kachalka;

public sealed class UiText : IValueConverter
{
    public static UiText Instance {get;}=new();
    public static void Bind(TextBlock block,string source)=>block.SetBinding(TextBlock.TextProperty,new Binding(nameof(LanguageState.Version)){Source=LanguageState.Instance,Converter=Instance,ConverterParameter=source,Mode=BindingMode.OneWay});
    public object Convert(object value,Type targetType,object parameter,CultureInfo culture)=>(parameter as string) switch
    {
        "@genres"=>AppLanguage.Genres(value?.ToString()),
        "@meta"=>string.Join(" · ",(value?.ToString()??"").Split(" · ").Select(AppLanguage.Genres)),
        _=>AppLanguage.T(parameter as string??value?.ToString())
    };
    public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>Binding.DoNothing;
}
[MarkupExtensionReturnType(typeof(object))]
public sealed class TrExtension : MarkupExtension
{
    public string Text {get;set;}="";
    public TrExtension(){}
    public TrExtension(string text)=>Text=text;
    public override object ProvideValue(IServiceProvider serviceProvider)=>new Binding(nameof(LanguageState.Version)){Source=LanguageState.Instance,Mode=BindingMode.OneWay,Converter=UiText.Instance,ConverterParameter=Text}.ProvideValue(serviceProvider);
}
