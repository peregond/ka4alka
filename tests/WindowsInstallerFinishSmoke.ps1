param([Parameter(Mandatory)][string]$Setup,[Parameter(Mandatory)][string]$Output)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class InstallerScreen {
 [StructLayout(LayoutKind.Sequential)] public struct Rect {public int Left,Top,Right,Bottom;}
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr handle,out Rect rect);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr handle,IntPtr context,uint flags);
}
'@
$process=Start-Process -FilePath $Setup -PassThru
try {
 $deadline=(Get-Date).AddSeconds(45)
 $root=$null
 do {
  $process.Refresh()
  if($process.HasExited){throw 'Interactive installer exited before its finish screen'}
  if($process.MainWindowHandle -ne 0){$root=[System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)}
  if($null -eq $root){Start-Sleep -Milliseconds 100}
 } while($null -eq $root -and (Get-Date) -lt $deadline)
 if($null -eq $root){throw 'Installer window missing'}
 $buttons=[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::Button)
 function Find-Button([string]$pattern){@($root.FindAll([System.Windows.Automation.TreeScope]::Descendants,$buttons)) | Where-Object {$_.Current.IsEnabled -and $_.Current.Name -match $pattern} | Select-Object -First 1}
 $finish=$null
 while((Get-Date)-lt $deadline){
  $finish=Find-Button '^Готово$'
  if($null -ne $finish){break}
  $next=Find-Button '^(Далее|Установить)'
  if($null -ne $next){([System.Windows.Automation.InvokePattern]$next.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()}
  Start-Sleep -Milliseconds 250
 }
 if($null -eq $finish){throw 'Installer never reached Finish'}
 $texts=$root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)
 $guide=@($texts) | Where-Object {$_.Current.Name.StartsWith('При первом запуске выбери папку')} | Select-Object -First 1
 if($null -eq $guide){throw 'Installer finish guide is missing'}
 $bounds=$guide.Current.BoundingRectangle
 $scale=$bounds.Width/292.5
 # MUI uses 195 horizontal and 60 vertical dialog units for the expanded guide.
 # Check the actual control can fit wrapped instructions at its Windows DPI.
 $font=[System.Drawing.Font]::new('Tahoma',8.25*$scale,[System.Drawing.FontStyle]::Regular,[System.Drawing.GraphicsUnit]::Point)
 $measure=[System.Drawing.Bitmap]::new(2,2);$graphics=[System.Drawing.Graphics]::FromImage($measure)
 try{$size=$graphics.MeasureString($guide.Current.Name,$font,[int]$bounds.Width);if($size.Height-gt $bounds.Height+2){throw ('Installer guide is clipped: text '+$size.Height+' vs control '+$bounds.Height)}}finally{$graphics.Dispose();$font.Dispose();$measure.Dispose()}
 $checks=[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::CheckBox)
 $run=@($root.FindAll([System.Windows.Automation.TreeScope]::Descendants,$checks)) | Where-Object {$_.Current.Name.StartsWith('Запустить Качалку')} | Select-Object -First 1
 if($null -eq $run){throw 'Installer launch checkbox missing'}
 $toggle=[System.Windows.Automation.TogglePattern]$run.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
 if($toggle.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On){$toggle.Toggle()}
 $rect=[InstallerScreen+Rect]::new();if(-not[InstallerScreen]::GetWindowRect($process.MainWindowHandle,[ref]$rect)){throw 'Cannot read installer size'}
 New-Item -ItemType Directory -Force $Output | Out-Null
 $bitmap=[System.Drawing.Bitmap]::new($rect.Right-$rect.Left,$rect.Bottom-$rect.Top);$draw=[System.Drawing.Graphics]::FromImage($bitmap);$context=$draw.GetHdc()
 try{if(-not[InstallerScreen]::PrintWindow($process.MainWindowHandle,$context,2)){throw 'Cannot capture installer finish page'}}finally{$draw.ReleaseHdc($context);$draw.Dispose()}
 try{$bitmap.Save((Join-Path $Output 'installer-finish.png'),[System.Drawing.Imaging.ImageFormat]::Png)}finally{$bitmap.Dispose()}
 ([System.Windows.Automation.InvokePattern]$finish.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
 if(-not $process.WaitForExit(15000)-or $process.ExitCode -ne 0){throw 'Interactive installer did not finish cleanly'}
 'PASS: interactive EXE finish page has readable folder/firewall/startup guidance and an optional launch checkbox'
} finally {if(-not $process.HasExited){$process.Kill($true)};$process.Dispose()}
