using System.Text.Json.Serialization;
using System.Windows.Media;
namespace Kachalka;
public partial record MediaItem
{
    [JsonIgnore] public Brush Cover { get { var b = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(Color), (Color)ColorConverter.ConvertFromString("#20262E"), 75); b.Freeze(); return b; } }
}
