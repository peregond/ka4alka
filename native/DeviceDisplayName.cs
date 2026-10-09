using Microsoft.Win32;
using System.IO;

namespace Kachalka;

public static class DeviceDisplayName
{
    static bool Useful(string? value)=>!string.IsNullOrWhiteSpace(value)&&
        !new[]{"System Product Name","System manufacturer","Default string","To be filled by O.E.M.","Not Applicable","Unknown","None"}.Contains(value.Trim(),StringComparer.OrdinalIgnoreCase);

    public static string Default()
    {
        try
        {
            using var key=Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            var model=key?.GetValue("SystemProductName") as string;
            if(!Useful(model))model=key?.GetValue("SystemFamily") as string;
            if(Useful(model))
            {
                model=model!.Trim();
                var manufacturer=(key?.GetValue("SystemManufacturer") as string)?.Trim();
                manufacturer=manufacturer?.ToUpperInvariant() switch{"ASUSTEK COMPUTER INC."=>"ASUS","LENOVO"=>"Lenovo","HEWLETT-PACKARD"=>"HP",_=>manufacturer};
                var name=Useful(manufacturer)&&!model.Contains(manufacturer!,StringComparison.OrdinalIgnoreCase)?manufacturer+" "+model:model;
                return Clean(name);
            }
        }
        catch(System.Security.SecurityException){}catch(UnauthorizedAccessException){}catch(IOException){}catch(PlatformNotSupportedException){}
        return Clean(Environment.MachineName);
    }

    public static string Clean(string value)=>new string(value.Where(c=>!char.IsControl(c)).ToArray()).Trim() is {Length:>0} name?name[..Math.Min(60,name.Length)]:"Компьютер";
}
