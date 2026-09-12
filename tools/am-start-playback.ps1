# Drives Apple Music's UI (UIA) to start real playback: foregrounds the window,
# double-clicks a song row, then the caller invokes PlayButtonElement on the album page.
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Windows.Forms
$root=[System.Windows.Automation.AutomationElement]::RootElement
$am=$root.FindFirst('Children',(New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ClassNameProperty,'WinUIDesktopWin32WindowClass')))
if(-not $am){ 'no AM window'; exit 1 }
# bring window to front
Add-Type '[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool SetForegroundWindow(System.IntPtr h); [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool ShowWindow(System.IntPtr h,int c);' -Name W32 -Namespace W
[W.W32]::ShowWindow($am.Current.NativeWindowHandle,9)|Out-Null
[W.W32]::SetForegroundWindow($am.Current.NativeWindowHandle)|Out-Null
Start-Sleep -Milliseconds 800
# find a song ListItem in the "best new songs" grid (has a non-empty rect, narrow height)
$items=$am.FindAll('Descendants',(New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::ListItem)))
$target=$null
foreach($i in $items){
  $r=$i.Current.BoundingRectangle
  if(-not $r.IsEmpty -and $r.Height -lt 60 -and $r.Y -gt 1400 -and $r.X -gt -1240){ $target=$i; break }
}
if(-not $target){ foreach($i in $items){ $r=$i.Current.BoundingRectangle; if(-not $r.IsEmpty -and $r.Height -lt 60 -and -not $i.Current.AutomationId){ $target=$i; break } } }
if(-not $target){ 'no song item'; exit 1 }
$r=$target.Current.BoundingRectangle
"target: $($target.Current.Name) $r"
# hover to reveal play button
[System.Windows.Forms.Cursor]::Position=New-Object System.Drawing.Point([int]($r.X+30),[int]($r.Y+$r.Height/2))
Start-Sleep -Milliseconds 900
$pb=$am.FindFirst('Descendants',(New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty,'PlayButtonElement')))
if($pb){
  try{ $pb.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); 'invoked PlayButtonElement'; exit 0 }catch{ "invoke failed: $($_.Exception.Message)" }
}
# fallback: double-click the item
Add-Type '[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern void mouse_event(int f,int x,int y,int d,int e);' -Name U -Namespace W
[W.U]::mouse_event(2,0,0,0,0); [W.U]::mouse_event(4,0,0,0,0)
Start-Sleep -Milliseconds 120
[W.U]::mouse_event(2,0,0,0,0); [W.U]::mouse_event(4,0,0,0,0)
'double-clicked item'
