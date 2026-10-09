using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;
namespace Kachalka;

public static class WindowsIntegration
{
    public const string StartupKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string StartupName="Ka4alka";
    public static string Executable=>Path.Combine(AppContext.BaseDirectory,"Kachalka.exe");
    public static string StartupCommand=>"\""+Executable+"\" --startup";
    public static bool StartupEnabled
    {
        get{using var key=Registry.CurrentUser.OpenSubKey(StartupKey);return string.Equals(key?.GetValue(StartupName) as string,StartupCommand,StringComparison.OrdinalIgnoreCase);}
    }
    public static void SetStartup(bool enabled)
    {
        using var key=Registry.CurrentUser.CreateSubKey(StartupKey,true);
        if(enabled)key.SetValue(StartupName,StartupCommand,RegistryValueKind.String);
        else key.DeleteValue(StartupName,false);
    }
    internal static string FirewallScript(string executable)
    {
        var literal="'"+Path.GetFullPath(executable).Replace("'","''")+"'";
        // Only this executable receives incoming TCP/UDP traffic on trusted
        // private networks. Other firewall rules and profiles stay as configured.
        return "$ErrorActionPreference='Stop';try{$app="+literal+";foreach($protocol in @('TCP','UDP')){$name='Ka4alka-Torrent-'+$protocol;$rule=Get-NetFirewallRule -Name $name -ErrorAction SilentlyContinue;if($rule){$rule|Set-NetFirewallRule -Direction Inbound -Action Allow -Enabled True -Profile Private -Program $app -Protocol $protocol}else{New-NetFirewallRule -Name $name -DisplayName ('Качалка: входящие '+$protocol) -Direction Inbound -Action Allow -Enabled True -Profile Private -Program $app -Protocol $protocol|Out-Null}};exit 0}catch{exit 1}";
    }
    public static async Task<bool> AllowFirewallAsync()
    {
        var powershell=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe");
        var encoded=Convert.ToBase64String(Encoding.Unicode.GetBytes(FirewallScript(Executable)));
        var start=new ProcessStartInfo(powershell){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden,Arguments="-NoProfile -NonInteractive -EncodedCommand "+encoded};
        try
        {
            using var process=Process.Start(start)??throw new IOException("Не удалось открыть настройки брандмауэра.");
            await process.WaitForExitAsync();
            if(process.ExitCode!=0)throw new IOException("Windows не смогла добавить исключение. Проверь права администратора и службу брандмауэра.");
            return true;
        }
        catch(Win32Exception error) when(error.NativeErrorCode==1223){return false;}
    }
}
