param([Parameter(Mandatory)][string]$Setup,[Parameter(Mandatory)][string]$Output)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class InstallerScreen {
 [StructLayout(LayoutKind.Sequential)] public struct Rect {public int Left,Top,Right,Bottom;}
 [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr dialog,int id);
 [DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr window);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
 [DllImport("user32.dll",EntryPoint="GetWindowLongW")] public static extern int GetWindowStyle(IntPtr window,int index);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr window,StringBuilder text,int length);
 public delegate bool EnumProc(IntPtr window,IntPtr state);
 [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent,EnumProc callback,IntPtr state);
 public static IntPtr FindRunCheckbox(IntPtr parent) {
  var found=IntPtr.Zero;
  EnumProc callback=(window,state)=>{
   var text=new StringBuilder(512);GetWindowText(window,text,text.Capacity);
   var kind=GetWindowStyle(window,-16)&15;
   if(IsWindowVisible(window)&&(kind==2||kind==3)&&text.ToString().Replace("&","").StartsWith("Запустить Качалку")){found=window;return false;}
   return true;
  };
  EnumChildWindows(parent,callback,IntPtr.Zero);return found;
 }
 [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr window,uint message,IntPtr param,IntPtr data);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr handle,out Rect rect);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr handle,IntPtr context,uint flags);
}
'@
$process=Start-Process -FilePath $Setup -PassThru
try {
 $deadline=(Get-Date).AddSeconds(90)
 $root=$null
 do {
  $process.Refresh()
  if($process.HasExited){throw 'Interactive installer exited before its finish screen'}
  if($process.MainWindowHandle -ne 0){$root=[System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)}
  if($null -eq $root){Start-Sleep -Milliseconds 100}
 } while($null -eq $root -and (Get-Date) -lt $deadline)
 if($null -eq $root){throw 'Installer window missing'}
 $finish=$null;$guide=$null
 while((Get-Date)-lt $deadline){
  $process.Refresh()
  if($process.HasExited){throw 'Installer exited before the final page'}
  $texts=$root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)
  $guide=@($texts) | Where-Object {$_.Current.Name.StartsWith('При первом запуске выбери папку')} | Select-Object -First 1
  # NSIS uses IDOK (1) for Next, Install, and Finish on the outer dialog.
  # Native activation also works when accessibility includes a mnemonic in Name.
  $next=[InstallerScreen]::GetDlgItem($process.MainWindowHandle,1)
  if($null -ne $guide -and $next -ne [IntPtr]::Zero){$finish=[System.Windows.Automation.AutomationElement]::FromHandle($next);break}
  if($next -ne [IntPtr]::Zero -and [InstallerScreen]::IsWindowEnabled($next)){
   [InstallerScreen]::SendMessage($next,0x00F5,[IntPtr]::Zero,[IntPtr]::Zero) | Out-Null
  }
  Start-Sleep -Milliseconds 250
 }
 if($null -eq $finish){
  $observed=@($root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)) | ForEach-Object {$_.Current.Name}
  throw ('Installer never reached Finish; observed controls: '+($observed -join ' | '))
 }
 if($null -eq $guide){throw 'Installer finish guide is missing'}
 $bounds=$guide.Current.BoundingRectangle
 $scale=$bounds.Width/292.5
 # MUI uses 195 horizontal and 60 vertical dialog units for the expanded guide.
 # Check the actual control can fit wrapped instructions at its Windows DPI.
 $font=[System.Drawing.Font]::new('Tahoma',8.25*$scale,[System.Drawing.FontStyle]::Regular,[System.Drawing.GraphicsUnit]::Point)
 $measure=[System.Drawing.Bitmap]::new(2,2);$graphics=[System.Drawing.Graphics]::FromImage($measure)
 try{$size=$graphics.MeasureString($guide.Current.Name,$font,[int]$bounds.Width);if($size.Height-gt $bounds.Height+2){throw ('Installer guide is clipped: text '+$size.Height+' vs control '+$bounds.Height)}}finally{$graphics.Dispose();$font.Dispose();$measure.Dispose()}
 # NSIS creates a native BUTTON with BS_CHECKBOX/BS_AUTOCHECKBOX.
 # UIA can classify it differently; verify the actual visible native control.
 $run=[IntPtr]::Zero;$checkboxDeadline=(Get-Date).AddSeconds(5)
 do {
  $run=[InstallerScreen]::FindRunCheckbox($process.MainWindowHandle)
  if($run -eq [IntPtr]::Zero){Start-Sleep -Milliseconds 100}
 }while($run -eq [IntPtr]::Zero -and (Get-Date)-lt $checkboxDeadline)
 if($run -eq [IntPtr]::Zero){
  $observed=@($root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)) | ForEach-Object {"$($_.Current.ControlType.ProgrammaticName): $($_.Current.Name)"}
  throw ('Installer launch checkbox missing; observed: '+($observed -join ' | '))
 }
 [InstallerScreen]::SendMessage($run,0x00F1,[IntPtr]::Zero,[IntPtr]::Zero) | Out-Null
 if([InstallerScreen]::SendMessage($run,0x00F0,[IntPtr]::Zero,[IntPtr]::Zero) -ne [IntPtr]::Zero){throw 'Cannot disable optional installer launch'}
 $rect=[InstallerScreen+Rect]::new();if(-not[InstallerScreen]::GetWindowRect($process.MainWindowHandle,[ref]$rect)){throw 'Cannot read installer size'}
 New-Item -ItemType Directory -Force $Output | Out-Null
 $bitmap=[System.Drawing.Bitmap]::new($rect.Right-$rect.Left,$rect.Bottom-$rect.Top);$draw=[System.Drawing.Graphics]::FromImage($bitmap);$context=$draw.GetHdc()
 try{if(-not[InstallerScreen]::PrintWindow($process.MainWindowHandle,$context,2)){throw 'Cannot capture installer finish page'}}finally{$draw.ReleaseHdc($context);$draw.Dispose()}
 try{$bitmap.Save((Join-Path $Output 'installer-finish.png'),[System.Drawing.Imaging.ImageFormat]::Png)}finally{$bitmap.Dispose()}
 $finishHandle=[InstallerScreen]::GetDlgItem($process.MainWindowHandle,1)
 if($finishHandle -eq [IntPtr]::Zero -or -not[InstallerScreen]::IsWindowEnabled($finishHandle)){throw 'Installer Finish button is unavailable'}
 [InstallerScreen]::SendMessage($finishHandle,0x00F5,[IntPtr]::Zero,[IntPtr]::Zero) | Out-Null
 if(-not $process.WaitForExit(15000)-or $process.ExitCode -ne 0){throw 'Interactive installer did not finish cleanly'}
 'PASS: interactive EXE finish page has readable folder/firewall/startup guidance and an optional launch checkbox'
} finally {if(-not $process.HasExited){$process.Kill($true)};$process.Dispose()}
