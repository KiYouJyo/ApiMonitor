#requires -Version 5.1
# Full tray context-menu command smoke: expand overflow, locate icon, right-click,
# press Enter (default item "open ApiMonitor"), assert the main window shows.
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class TrayCmdFull {
    [DllImport("user32.dll")] public static extern IntPtr FindWindow(string c, string t);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);

    public const uint RD = 0x0008, RU = 0x0010, LD = 0x0002, LU = 0x0004;
    public const byte VK_RETURN = 0x0D;
    public const uint KEYEVENTF_KEYUP = 0x2;

    public static string Class(IntPtr h) { var sb = new StringBuilder(256); GetClassName(h, sb, 256); return sb.ToString(); }
}
'@

$btnCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)

function Find-Icon {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
    foreach ($b in $all) {
        if ($b.Current.ClassName -match 'SystemTray.NormalButton' -and $b.Current.Name -match 'ApiMonitor') {
            $r = $b.Current.BoundingRectangle
            return @{ X = [int]$r.X; Y = [int]$r.Y; W = [int]$r.Width; H = [int]$r.Height }
        }
    }
    return $null
}

function Expand-Overflow {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
    foreach ($b in $all) {
        if ($b.Current.ClassName -match 'SystemTray.NormalButton') {
            $r = $b.Current.BoundingRectangle
            [TrayCmdFull]::SetCursorPos([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2))
            Start-Sleep -Milliseconds 150
            [TrayCmdFull]::mouse_event([TrayCmdFull]::LD, 0, 0, 0, [UIntPtr]::Zero)
            [TrayCmdFull]::mouse_event([TrayCmdFull]::LU, 0, 0, 0, [UIntPtr]::Zero)
            return
        }
    }
}

# Hide main window first (close to tray), so "open" visibly restores it.
$ls = Join-Path $env:LOCALAPPDATA 'Packages\ApiMonitor_cx0n152q1hsh2\LocalState'
$s = @{ schemaVersion = 4; mainWindowCloseBehavior = 0; showFirstCloseExplanation = $false; startWithWindows = $false; trayFeatureEnabled = $true }
[System.IO.File]::WriteAllText((Join-Path $ls 'tray-settings.json'), ($s | ConvertTo-Json), (New-Object System.Text.UTF8Encoding($false)))
$p = Get-Process -Name ApiMonitor -ErrorAction SilentlyContinue
if ([TrayCmdFull]::IsWindowVisible($p.MainWindowHandle)) { $p.CloseMainWindow(); Start-Sleep -Seconds 4 }
Write-Host ("main hidden: " + (-not [TrayCmdFull]::IsWindowVisible($p.MainWindowHandle)))

$icon = Find-Icon
if (-not $icon) { Expand-Overflow; Start-Sleep -Milliseconds 900; $icon = Find-Icon }
if (-not $icon) { Write-Host 'FAIL: tray icon not found'; exit 1 }
$cx = $icon.X + [int]($icon.W / 2)
$cy = $icon.Y + [int]($icon.H / 2)
Write-Host "icon center: ($cx,$cy)"

[TrayCmdFull]::SetCursorPos($cx, $cy)
Start-Sleep -Milliseconds 200
[TrayCmdFull]::mouse_event([TrayCmdFull]::RD, 0, 0, 0, [UIntPtr]::Zero)
[TrayCmdFull]::mouse_event([TrayCmdFull]::RU, 0, 0, 0, [UIntPtr]::Zero)

$menu = [IntPtr]::Zero
for ($i = 0; $i -lt 15; $i++) { Start-Sleep -Milliseconds 150; $menu = [TrayCmdFull]::FindWindow("#32768", $null); if ($menu -ne [IntPtr]::Zero) { break } }
if ($menu -eq [IntPtr]::Zero) { Write-Host 'FAIL: menu did not open'; exit 1 }
Write-Host "menu open: $menu"
$fg = [TrayCmdFull]::GetForegroundWindow()
Write-Host ("foreground class: [" + [TrayCmdFull]::Class($fg) + "] is-menu=" + ($fg -eq $menu))

# Press Enter to select the default item ("Open ApiMonitor").
[TrayCmdFull]::keybd_event([TrayCmdFull]::VK_RETURN, 0, 0, [UIntPtr]::Zero)
[TrayCmdFull]::keybd_event([TrayCmdFull]::VK_RETURN, 0, [TrayCmdFull]::KEYEVENTF_KEYUP, [UIntPtr]::Zero)
Start-Sleep -Seconds 3
$p2 = Get-Process -Name ApiMonitor -ErrorAction SilentlyContinue
$visible = [TrayCmdFull]::IsWindowVisible($p2.MainWindowHandle)
Write-Host ("after ENTER, main window visible: $visible (expect True)")
Remove-Item (Join-Path $ls 'tray-settings.json') -Force -ErrorAction SilentlyContinue
Write-Host 'test settings removed'
if (-not $visible) { exit 1 }
