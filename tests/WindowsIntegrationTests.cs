using System.Diagnostics;
using System.Text;
using Kachalka;
using Microsoft.Win32;

static class WindowsIntegrationTests
{
    static async Task<string> PowerShell(string script)
    {
        var executable=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe");
        var start=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        start.ArgumentList.Add("-NoProfile");start.ArgumentList.Add("-NonInteractive");start.ArgumentList.Add("-EncodedCommand");start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes("$ErrorActionPreference='Stop';"+script)));
        using var process=Process.Start(start)!;var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();
        if(process.ExitCode!=0)throw new Exception("Windows integration check failed: "+await error);
        return await output;
    }
    public static async Task Run()
    {
        using var key=Registry.CurrentUser.CreateSubKey(WindowsIntegration.StartupKey,true);
        var previous=key.GetValue(WindowsIntegration.StartupName);var kind=previous is null?RegistryValueKind.String:key.GetValueKind(WindowsIntegration.StartupName);
        var unrelated=key.GetValueNames().Where(n=>n!=WindowsIntegration.StartupName).ToDictionary(n=>n,n=>key.GetValue(n)?.ToString());
        try
        {
            WindowsIntegration.SetStartup(true);
            if(!WindowsIntegration.StartupEnabled||key.GetValue(WindowsIntegration.StartupName) as string!="\""+WindowsIntegration.Executable+"\" --startup")throw new Exception("Startup command is missing its quoted executable or startup flag.");
            WindowsIntegration.SetStartup(false);
            if(WindowsIntegration.StartupEnabled||key.GetValue(WindowsIntegration.StartupName)!=null)throw new Exception("Startup option cannot be disabled.");
            if(unrelated.Any(pair=>key.GetValue(pair.Key)?.ToString()!=pair.Value))throw new Exception("Startup configuration changed an unrelated app.");
            Console.WriteLine("PASS: current-user startup enable/disable preserves unrelated shell and app entries");
        }
        finally{if(previous is null)key.DeleteValue(WindowsIntegration.StartupName,false);else key.SetValue(WindowsIntegration.StartupName,previous,kind);}
        var before=await PowerShell("if(Get-NetFirewallRule -Name 'Ka4alka-Torrent-*' -ErrorAction SilentlyContinue){throw 'Refusing to replace existing firewall fixtures'};(Get-NetFirewallProfile|Select-Object Name,Enabled|ConvertTo-Json -Compress)");
        try
        {
            if(!await WindowsIntegration.AllowFirewallAsync()||!await WindowsIntegration.AllowFirewallAsync())throw new Exception("Firewall permission was unexpectedly cancelled in the Windows fixture.");
            var literal="'"+WindowsIntegration.Executable.Replace("'","''")+"'";
            await PowerShell("$rules=@(Get-NetFirewallRule -Name 'Ka4alka-Torrent-*');if($rules.Count-ne 2){throw 'Firewall setup duplicated rules'};foreach($rule in $rules){if($rule.Profile-ne 'Private'-or $rule.Direction-ne 'Inbound'-or $rule.Action-ne 'Allow'-or $rule.Enabled-ne 'True'){throw 'Incorrect firewall scope'};if(($rule|Get-NetFirewallApplicationFilter).Program-ne "+literal+"){throw 'Firewall allows an unrelated executable'};$protocol=($rule|Get-NetFirewallPortFilter).Protocol;if($protocol-notin @('TCP','UDP',6,17)){throw 'Incorrect firewall protocol'}}");
            var after=await PowerShell("Get-NetFirewallProfile|Select-Object Name,Enabled|ConvertTo-Json -Compress");
            if(before.Trim()!=after.Trim())throw new Exception("Application exception changed global firewall profiles.");
            Console.WriteLine("PASS: explicit firewall setup is idempotent, limited to this executable and private TCP/UDP, and preserves firewall profiles");
        }
        finally{await PowerShell("Get-NetFirewallRule -Name 'Ka4alka-Torrent-*' -ErrorAction SilentlyContinue|Remove-NetFirewallRule");}
    }
}
