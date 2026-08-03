#requires -Version 5.1
<#
  Tray context-menu smoke test (real Win32, Windows 11 notification overflow):
  1. Expand the notification-area overflow panel (click "Show hidden icons");
  2. Locate the ApiMonitor tray icon button inside the panel via UIA;
  3. Right-click its center;
  4. Wait for the popup menu (#32768), read its rect;
  5. Assert the menu is near the icon/cursor (not the screen top-left);
  6. Assert the menu can be closed with ESC.
#>
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class TrayScan2 {
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
    [DllImport("user32.dll")]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int X, int Y);
    [DllImport("user32.dll")]
    public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    public const uint MOUSEEVENTF_LEFTUP = 0x0004;
    public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    public const uint MOUSEEVENTF_RIGHTUP = 0x0010;

    public static string Title(IntPtr h) {
        var sb = new StringBuilder(256);
        GetWindowText(h, sb, 256);
        return sb.ToString();
    }

    public static void Click(int x, int y, uint down, uint up) {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(150);
        mouse_event(down, 0, 0, 0, UIntPtr.Zero);
        mouse_event(up, 0, 0, 0, UIntPtr.Zero);
    }
}
'@

# 1. Click "Show hidden icons" (SystemTray.NormalButton) to expand the overflow panel.
$root = [System.Windows.Automation.AutomationElement]::RootElement
$btnCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
$all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
$overflow = $null
foreach ($b in $all) {
    if ($b.Current.ClassName -match 'SystemTray.NormalButton') {
        $r = $b.Current.BoundingRectangle
        $overflow = @{ X = [int]$r.X; Y = [int]$r.Y; W = [int]$r.Width; H = [int]$r.Height }
        break
    }
}
if (-not $overflow) { Write-Host 'FAIL: overflow button not found'; exit 1 }
Write-Host "overflow button: ($($overflow.X),$($overflow.Y)) $($overflow.W)x$($overflow.H)"
[TrayScan2]::Click($overflow.X + 32, $overflow.Y + 48, [TrayScan2]::MOUSEEVENTF_LEFTDOWN, [TrayScan2]::MOUSEEVENTF_LEFTUP)
Start-Sleep -Milliseconds 800

# 2. Find the ApiMonitor tray icon button in the overflow panel.
$iconBtn = $null
for ($attempt = 0; $attempt -lt 5; $attempt++) {
    $root2 = [System.Windows.Automation.AutomationElement]::RootElement
    $all2 = $root2.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
    foreach ($b in $all2) {
        $nm = $b.Current.Name
        if ($nm -match 'ApiMonitor' -and $b.Current.ClassName -notmatch 'TaskList' -and $b.Current.ClassName -notmatch 'tool__head') {
            $r = $b.Current.BoundingRectangle
            $iconBtn = @{ X = [int]$r.X; Y = [int]$r.Y; W = [int]$r.Width; H = [int]$r.Height; Name = $nm; Class = $b.Current.ClassName }
            break
        }
    }
    if ($iconBtn) { break }
    Start-Sleep -Milliseconds 500
}
if (-not $iconBtn) {
    Write-Host 'FAIL: ApiMonitor tray icon not found in overflow panel'
    exit 1
}
Write-Host "tray icon button: name=[$($iconBtn.Name)] class=[$($iconBtn.Class)] at ($($iconBtn.X),$($iconBtn.Y)) $($iconBtn.W)x$($iconBtn.H)"

# 3. Right-click the icon center.
$cx = $iconBtn.X + [int]($iconBtn.W / 2)
$cy = $iconBtn.Y + [int]($iconBtn.H / 2)
[TrayScan2]::Click($cx, $cy, [TrayScan2]::MOUSEEVENTF_RIGHTDOWN, [TrayScan2]::MOUSEEVENTF_RIGHTUP)

# 4. Wait for the popup menu.
$menu = [IntPtr]::Zero
for ($i = 0; $i -lt 20; $i++) {
    Start-Sleep -Milliseconds 150
    $menu = [TrayScan2]::FindWindow("#32768", $null)
    if ($menu -ne [IntPtr]::Zero) { break }
}
if ($menu -eq [IntPtr]::Zero) { Write-Host 'FAIL: menu did not appear'; exit 1 }
Start-Sleep -Milliseconds 300
$mrect = New-Object TrayScan2+RECT
[void][TrayScan2]::GetWindowRect($menu, [ref]$mrect)
$title = [TrayScan2]::Title($menu)
Write-Host "menu: title=[$title] rect=($($mrect.Left),$($mrect.Top))-($($mrect.Right),$($mrect.Bottom))"

# 5. Assert position: near icon, not at screen top-left.
$menuCx = [int](($mrect.Left + $mrect.Right) / 2)
$menuCy = [int](($mrect.Top + $mrect.Bottom) / 2)
$dist = [math]::Sqrt([math]::Pow(($menuCx - $cx), 2) + [math]::Pow(($menuCy - $cy), 2))
Write-Host "icon center: ($cx,$cy)  menu center: ($menuCx,$menuCy)  distance: $([int]$dist)px"
if ($mrect.Left -lt 30 -and $mrect.Top -lt 30) {
    Write-Host 'FAIL: menu at screen top-left'
    exit 1
}
if ($dist -gt 700) {
    Write-Host "FAIL: menu too far from icon ($([int]$dist)px)"
    exit 1
}
Write-Host 'PASS: menu near tray icon, not at top-left'

# 6. ESC closes the menu.
Add-Type -AssemblyName System.Windows.Forms
[System.Windows.Forms.SendKeys]::SendWait('{ESC}')
Start-Sleep -Milliseconds 500
$after = [TrayScan2]::FindWindow("#32768", $null)
if ($after -ne [IntPtr]::Zero) { Write-Host 'FAIL: ESC did not close the menu'; exit 1 }
Write-Host 'PASS: ESC closed the menu'
