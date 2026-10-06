<#
  The installed app driven the way a person uses it: Setup.exe installs it, real hotkeys and
  keys reach it, and what lands on disk, on the clipboard and on screen is checked. Uninstalling
  ends the run. A screenshot of each step goes to the shots folder.

  Needs an interactive desktop, as CI's Windows runners have, and Windows PowerShell, whose
  clipboard access runs on a single-threaded apartment:

    powershell -File drive.ps1 -Setup Tinysnap-win-x64-Setup.exe -Next next -Shots shots

  -Next is a folder holding a newer build's feed and package, to update to.
#>
param(
    [Parameter(Mandatory)] [string] $Setup,
    [string] $Next = '',
    [string] $Shots = 'shots'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing, UIAutomationClient, UIAutomationTypes
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class Desk
{
    public class Win
    {
        public IntPtr Handle;
        public string Title;
        public int X, Y, Width, Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Box { public int Left, Top, Right, Bottom; }

    private delegate bool EnumProc(IntPtr window, IntPtr data);

    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, int x, int y, uint data, UIntPtr extra);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, IntPtr data);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int max);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Box box);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out Box box, int size);

    private const uint KeyUp = 0x2, Move = 0x1, LeftDown = 0x2, LeftUp = 0x4, Absolute = 0x8000;

    /// <summary>The visible top level windows of one process, front to back.</summary>
    public static List<Win> Windows(int process)
    {
        var found = new List<Win>();
        EnumWindows(delegate (IntPtr window, IntPtr data)
        {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            if (owner != process || !IsWindowVisible(window)) return true;
            var title = new StringBuilder(512);
            GetWindowText(window, title, title.Capacity);
            // What shows on screen, without the invisible borders a window is resized by.
            Box box;
            if (DwmGetWindowAttribute(window, 9, out box, Marshal.SizeOf(typeof(Box))) != 0) GetWindowRect(window, out box);
            found.Add(new Win { Handle = window, Title = title.ToString(), X = box.Left, Y = box.Top,
                                Width = box.Right - box.Left, Height = box.Bottom - box.Top });
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static string Title(IntPtr window)
    {
        var title = new StringBuilder(512);
        GetWindowText(window, title, title.Capacity);
        return title.ToString();
    }

    /// <summary>Holds each key down in order, then lets them go in reverse, as fingers would.</summary>
    public static void Keys(params byte[] keys)
    {
        foreach (var key in keys) { keybd_event(key, 0, 0, UIntPtr.Zero); System.Threading.Thread.Sleep(30); }
        for (var i = keys.Length - 1; i >= 0; i--) { keybd_event(keys[i], 0, KeyUp, UIntPtr.Zero); System.Threading.Thread.Sleep(30); }
    }

    /// <summary>The pointer to a pixel on the primary monitor.</summary>
    public static void MoveTo(int x, int y)
    {
        var width = GetSystemMetrics(0);
        var height = GetSystemMetrics(1);
        mouse_event(Move | Absolute, x * 65535 / (width - 1), y * 65535 / (height - 1), 0, UIntPtr.Zero);
    }

    public static void Down() { mouse_event(LeftDown, 0, 0, 0, UIntPtr.Zero); }

    public static void Up() { mouse_event(LeftUp, 0, 0, 0, UIntPtr.Zero); }

    /// <summary>A right click where the pointer is.</summary>
    public static void RightClick() { mouse_event(0x8, 0, 0, 0, UIntPtr.Zero); mouse_event(0x10, 0, 0, 0, UIntPtr.Zero); }

    /// <summary>A left drag in small steps, as a hand would make it.</summary>
    public static void Drag(int fromX, int fromY, int toX, int toY)
    {
        MoveTo(fromX, fromY);
        System.Threading.Thread.Sleep(150);
        Down();
        for (var step = 1; step <= 12; step++)
        {
            System.Threading.Thread.Sleep(25);
            MoveTo(fromX + (toX - fromX) * step / 12, fromY + (toY - fromY) * step / 12);
        }
        System.Threading.Thread.Sleep(150);
        Up();
    }

    /// <summary>True when another app holds the combination: registering it fails.</summary>
    public static bool HotkeyTaken(uint modifiers, uint key)
    {
        if (!RegisterHotKey(IntPtr.Zero, 0xBEEF, modifiers, key)) return true;
        UnregisterHotKey(IntPtr.Zero, 0xBEEF);
        return false;
    }

    /// <summary>Holds a combination for this script, as the screen snip in Explorer holds Print
    /// Screen on a Windows 11 desktop, until let go.</summary>
    public static bool Hold(uint modifiers, uint key) { return RegisterHotKey(IntPtr.Zero, 0xB0B0, modifiers, key); }

    public static void LetGo() { UnregisterHotKey(IntPtr.Zero, 0xB0B0); }

    /// <summary>Brings a window forward even when this process is not in front: a tapped Alt
    /// counts as input, which lets the next call take the foreground.</summary>
    public static bool Focus(IntPtr window)
    {
        keybd_event(0x12, 0, 0, UIntPtr.Zero);
        keybd_event(0x12, 0, KeyUp, UIntPtr.Zero);
        return SetForegroundWindow(window);
    }
}
'@
[void][Desk]::SetProcessDPIAware()

$Ctrl, $Shift = 0x11, 0x10
$ModControl, $ModShift = 0x2, 0x4

$install = Join-Path $env:LOCALAPPDATA 'Tinysnap'
$exe = Join-Path $install 'current\Tinysnap.exe'
$library = Join-Path $env:APPDATA 'Tinysnap\Library'
$saves = Join-Path $env:USERPROFILE 'Pictures\Screenshots'
$startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Tinysnap.lnk'
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Tinysnap'
$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
New-Item -ItemType Directory -Force $Shots | Out-Null

$results = New-Object System.Collections.Generic.List[string]
$script:failed = $false
$script:shot = 0

# A check is a block that returns nothing when it holds, or what went wrong.
function Check([string] $name, [scriptblock] $check)
{
    try { $problem = & $check } catch { $problem = "$($_.Exception.GetType().Name): $($_.Exception.Message)" }
    if ($problem) { $script:failed = $true; $line = "FAILED  ${name}: $problem" } else { $line = "ok      $name" }
    $results.Add($line)
    Write-Host $line
}

# The first thing the block returns within the time, or nothing.
function Until([scriptblock] $condition, [double] $seconds = 15)
{
    $deadline = (Get-Date).AddSeconds($seconds)
    do
    {
        try { $value = & $condition } catch { $value = $null }
        if ($value) { return $value }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    return $null
}

# Until, timed from now to the answer at a twentieth of a second, the time kept with the
# results, so every run shows how quickly a capture appears.
function Timed([string] $what, [scriptblock] $condition, [double] $seconds = 15, [System.Diagnostics.Stopwatch] $since = $null)
{
    # From $since when given, the moment before a key press: holding and letting go of the keys
    # takes longer than the app does.
    $clock = if ($since) { $since } else { [System.Diagnostics.Stopwatch]::StartNew() }
    $deadline = (Get-Date).AddSeconds($seconds)
    do
    {
        try { $value = & $condition } catch { $value = $null }
        if ($value)
        {
            $line = "time    ${what}: $($clock.ElapsedMilliseconds) ms"
            $results.Add($line)
            Write-Host $line
            return $value
        }
        Start-Sleep -Milliseconds 50
    } while ((Get-Date) -lt $deadline)
    return $null
}

function Grab
{
    $bitmap = New-Object System.Drawing.Bitmap $screen.Width, $screen.Height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($screen.Left, $screen.Top, 0, 0, $bitmap.Size)
    $graphics.Dispose()
    return $bitmap
}

function Shot([string] $name)
{
    $script:shot++
    $bitmap = Grab
    $bitmap.Save((Join-Path $Shots ('{0}-{1}.png' -f $script:shot, $name)), [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
}

# Where a window shows, if any of it is off the monitor's work area.
function OffScreen($window)
{
    $work = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
    $now = [Desk]::Windows($script:app.Id) | Where-Object Handle -eq $window.Handle | Select-Object -First 1
    if (-not $now) { return 'gone' }
    if ($now.X -lt $work.Left -or $now.Y -lt $work.Top -or $now.X + $now.Width -gt $work.Right -or $now.Y + $now.Height -gt $work.Bottom)
    {
        '{0} by {1} at {2},{3} on a {4} by {5} work area' -f $now.Width, $now.Height, $now.X, $now.Y, $work.Width, $work.Height
    }
}

# A window's corners, just inside it, as they showed and as the screen showed without it: a
# see-through corner shows what is behind, where one drawn black or grey does not.
function Opaque($window, $with, $without)
{
    $inset = 2
    foreach ($corner in @(@($window.X, $window.Y), @(($window.X + $window.Width - 1), $window.Y),
                          @($window.X, ($window.Y + $window.Height - 1)), @(($window.X + $window.Width - 1), ($window.Y + $window.Height - 1))))
    {
        $x = [Math]::Min([Math]::Max($corner[0] + ($(if ($corner[0] -eq $window.X) { $inset } else { -$inset })), 0), $screen.Width - 1)
        $y = [Math]::Min([Math]::Max($corner[1] + ($(if ($corner[1] -eq $window.Y) { $inset } else { -$inset })), 0), $screen.Height - 1)
        $shown, $behind = $with.GetPixel($x, $y), $without.GetPixel($x, $y)
        $far = [Math]::Max([Math]::Abs($shown.R - $behind.R), [Math]::Max([Math]::Abs($shown.G - $behind.G), [Math]::Abs($shown.B - $behind.B)))
        if ($far -gt 24) { return "at $x,$y it shows $($shown.Name) over $($behind.Name)" }
    }
}

function Press { [Desk]::Keys([byte[]]$args) }

function Editors { @([Desk]::Windows($script:app.Id) | Where-Object { $_.Title -like 'Capture at*' }) }

# The overlay: the window of Tinysnap's that covers the whole monitor and is no editor.
function Overlay
{
    [Desk]::Windows($script:app.Id) |
        Where-Object { $_.Title -notlike 'Capture at*' -and $_.Width -ge $screen.Width -and $_.Height -ge $screen.Height } |
        Select-Object -First 1
}

function SavedPngs { @(Get-ChildItem $saves -Filter *.png -ErrorAction SilentlyContinue | ForEach-Object FullName) }

function LibraryEntries { @(Get-ChildItem $library -Directory -ErrorAction SilentlyContinue) }

function PngSize([string] $path)
{
    $image = [System.Drawing.Image]::FromFile($path)
    try { return '{0}x{1}' -f $image.Width, $image.Height } finally { $image.Dispose() }
}

# The editor in front for keys to reach it. Whether it came forward by itself is its own check.
function Front($window)
{
    if ([Desk]::GetForegroundWindow() -ne $window.Handle) { [void][Desk]::Focus($window.Handle) }
    Start-Sleep -Milliseconds 300
}

# Saves the front editor with Ctrl+S and returns the new file.
function SaveFront
{
    $before = SavedPngs
    Press $Ctrl 0x53
    Until { SavedPngs | Where-Object { $before -notcontains $_ } | Select-Object -First 1 } 10
}

function ClipboardText { try { [System.Windows.Forms.Clipboard]::GetText() } catch { $null } }

# The results to a file beside the shots and to the run's summary page, and the exit code.
# The marks are built from code points: Windows PowerShell reads this file as ANSI.
function Finish
{
    $results | Set-Content (Join-Path $Shots 'results.txt')
    if ($env:GITHUB_STEP_SUMMARY)
    {
        $pass, $fail = [char]0x2705, [char]0x274C
        $lines = $results | ForEach-Object {
            if ($_ -like 'ok*') { "- $pass $($_.Substring(8))" }
            elseif ($_ -like 'time*') { "- $($_.Substring(8))" }
            else { "- $fail $($_.Substring(8))" }
        }
        (@('### Installed app, end to end', '') + $lines) -join "`n" | Add-Content $env:GITHUB_STEP_SUMMARY -Encoding UTF8
    }
    exit [int]$script:failed
}

# 1. Installing

Check 'Setup.exe installs silently' {
    $setup = Start-Process $Setup -ArgumentList '--silent' -PassThru
    $null = $setup.Handle
    if (-not $setup.WaitForExit(180000)) { return 'still running after 3 minutes' }
    if ($setup.ExitCode -ne 0) { return "exited with $($setup.ExitCode)" }
    if (-not (Test-Path $exe)) { return "nothing at $exe" }
}
Check 'Installed apps lists it' {
    $name = (Get-ItemProperty $uninstallKey -ErrorAction SilentlyContinue).DisplayName
    if ($name -ne 'Tinysnap') { "listed as '$name'" }
}
Check 'the Start menu has it' { if (-not (Test-Path $startMenu)) { "no $startMenu" } }
Check 'Open with offers it for PNG and JPEG' {
    foreach ($extension in '.png', '.jpg', '.jpeg')
    {
        $progIds = Get-Item "HKCU:\Software\Classes\$extension\OpenWithProgids" -ErrorAction SilentlyContinue
        if (-not $progIds -or $progIds.GetValueNames() -notcontains 'Tinysnap.Picture') { return "not for $extension" }
    }
    $command = (Get-ItemProperty 'HKCU:\Software\Classes\Tinysnap.Picture\shell\open\command').'(default)'
    if ($command -notlike "*$exe*") { "opens with $command" }
}
if (-not (Test-Path $exe)) { Finish }
Write-Host "Screen $($screen.Width) by $($screen.Height)"

# 2. Something to capture: a window of dark text on white, in a process of its own.

$target = @'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$form = New-Object Windows.Forms.Form
$form.FormBorderStyle = 'None'; $form.StartPosition = 'Manual'; $form.BackColor = 'White'; $form.Opacity = 0.97
$form.Location = New-Object Drawing.Point(80, 80); $form.Size = New-Object Drawing.Size(860, 200)
$label = New-Object Windows.Forms.Label
$label.Text = 'Tinysnap reads this line'; $label.Font = New-Object Drawing.Font('Segoe UI', 36)
$label.ForeColor = 'Black'; $label.AutoSize = $true; $label.Location = New-Object Drawing.Point(40, 60)
$form.Controls.Add($label)
[Windows.Forms.Application]::Run($form)
'@
$page = Start-Process powershell -PassThru -ArgumentList '-NoProfile', '-EncodedCommand', ([Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($target)))
[void](Until { [Desk]::Windows($page.Id).Count } 20)

# 3. Starting, with a key for Capture Window, which has none until the person gives it one

$preferences = Join-Path $env:APPDATA 'Tinysnap\preferences.json'
New-Item -ItemType Directory -Force (Split-Path $preferences) | Out-Null
'{"hotkeys": {"window": {"keyCode": 87, "modifiers": ["control", "shift"]}}}' | Set-Content $preferences -Encoding ASCII
# Windows 11 holds Print Screen for its screen snip, and lets go only when Explorer restarts. The
# runner has no screen snip, so this script holds the key in its place, and Tinysnap must take it anyway.
$printScreenSetting = (Get-ItemProperty 'HKCU:\Control Panel\Keyboard' -ErrorAction SilentlyContinue).PrintScreenKeyForSnippingEnabled
Check 'Print Screen is held elsewhere before Tinysnap starts' { if (-not [Desk]::Hold(0, 0x2C)) { 'something else already holds it' } }
$since = [System.Diagnostics.Stopwatch]::StartNew()
$script:app = Start-Process $exe -PassThru
[void](Timed 'starting to holding its hotkeys' { [Desk]::HotkeyTaken($ModControl + $ModShift, 0x31) } 30 $since)
Check 'Tinysnap starts and holds its hotkeys' {
    if (-not (Until { [Desk]::HotkeyTaken($ModControl + $ModShift, 0x31) } 30)) { return 'Ctrl+Shift+1 is free' }
    if (-not [Desk]::HotkeyTaken($ModControl + $ModShift, 0x57)) { return 'Ctrl+Shift+W is free' }
    if (-not [Desk]::HotkeyTaken($ModControl + $ModShift, 0x4F)) { return 'Ctrl+Shift+O is free' }
}
Start-Sleep -Seconds 1
Shot 'started'

# 3a. The tray icon: out in the taskbar, a left click for an area, a right click for the menu

$Element = [System.Windows.Automation.AutomationElement]
$Tree = [System.Windows.Automation.TreeScope]
function Named($root, [string] $name)
{
    $root.FindFirst($Tree::Descendants, (New-Object System.Windows.Automation.PropertyCondition($Element::NameProperty, $name)))
}
$taskbar = $Element::RootElement.FindFirst($Tree::Children,
    (New-Object System.Windows.Automation.PropertyCondition($Element::ClassNameProperty, 'Shell_TrayWnd')))
$icon = Until { Named $taskbar 'Tinysnap' } 15
Check 'the tray icon shows in the taskbar, not behind the arrow' { if (-not $icon) { 'not in the taskbar' } }
if ($icon)
{
    $r = $icon.Current.BoundingRectangle
    $x, $y = [int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2)
    Check 'the tray icon stands out from the taskbar' {
        $bitmap = Grab
        $lo, $hi = 255, 0
        for ($px = [int]$r.X; $px -lt [int]($r.X + $r.Width); $px++)
        {
            for ($py = [int]$r.Y; $py -lt [int]($r.Y + $r.Height); $py++)
            {
                $p = $bitmap.GetPixel($px, $py)
                $l = ($p.R * 299 + $p.G * 587 + $p.B * 114) / 1000
                if ($l -lt $lo) { $lo = $l }
                if ($l -gt $hi) { $hi = $l }
            }
        }
        $bitmap.Dispose()
        if ($hi - $lo -lt 100) { "its lightest and darkest pixels differ by only $([int]($hi - $lo))" }
    }
    [Desk]::MoveTo($x, $y); Start-Sleep -Milliseconds 200; [Desk]::Down(); [Desk]::Up()
    $overlay = Until { Overlay } 10
    Check 'a left click on the tray icon captures an area' { if (-not $overlay) { 'no overlay' } }
    Shot 'tray-click'
    Press 0x1B
    [void](Until { -not (Overlay) } 5)
    Start-Sleep -Milliseconds 500
    [Desk]::MoveTo($x, $y); Start-Sleep -Milliseconds 200; [Desk]::RightClick()
    $menu = Until { [Desk]::Windows($script:app.Id) | ForEach-Object { Named ($Element::FromHandle($_.Handle)) 'Capture Fullscreen' } | Select-Object -First 1 } 10
    Shot 'tray-menu'
    Check 'a right click on the tray icon opens the menu' { if (-not $menu) { 'no menu' } }
    # Away from the menu, on the page window, as a person dismisses a menu.
    [Desk]::MoveTo(500, 150); Start-Sleep -Milliseconds 200; [Desk]::Down(); [Desk]::Up()
    $gone = Until { -not ([Desk]::Windows($script:app.Id) | ForEach-Object { Named ($Element::FromHandle($_.Handle)) 'Capture Fullscreen' } | Select-Object -First 1) } 5
    Check 'a click elsewhere closes the tray menu' { if (-not $gone) { 'still open' } }
    Start-Sleep -Milliseconds 500
}

# 4. Fullscreen: capture, keep, copy and close; capture again, save and close

$since = [System.Diagnostics.Stopwatch]::StartNew()
Press $Ctrl $Shift 0x31
$editor = Timed 'Ctrl+Shift+1 to an editor' { Editors | Select-Object -First 1 } 15 $since
Check 'Ctrl+Shift+1 opens the screen in an editor' { if (-not $editor) { 'no editor' } }
if ($editor)
{
    Check 'the editor comes to the front' {
        $front = [Desk]::GetForegroundWindow()
        if ($front -ne $editor.Handle) { "in front: '$([Desk]::Title($front))'" }
    }
    Start-Sleep -Seconds 1
    Shot 'fullscreen-editor'
    Check 'the editor for the whole screen fits on it' { OffScreen $editor }
    Check 'the capture is kept in the library' {
        $entry = LibraryEntries | Select-Object -First 1
        if (-not $entry) { return "nothing in $library" }
        foreach ($file in 'original.png', 'image.png', 'edits.json')
        {
            if (-not (Test-Path (Join-Path $entry.FullName $file))) { return "no $file in $($entry.Name)" }
        }
    }
    Check 'the library lists the capture where a screen reader finds it' {
        $Element = [System.Windows.Automation.AutomationElement]
        $button = $Element::FromHandle($editor.Handle).FindFirst('Descendants',
            (New-Object System.Windows.Automation.PropertyCondition($Element::NameProperty, 'Library')))
        if (-not $button) { return 'no Library button' }
        $since = [System.Diagnostics.Stopwatch]::StartNew()
        $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        $window = Timed 'the Library button to its window, the first time' { [Desk]::Windows($script:app.Id) | Where-Object Title -eq 'Library' | Select-Object -First 1 } 10 $since
        if (-not $window) { return 'no Library window' }
        $items = New-Object System.Windows.Automation.PropertyCondition($Element::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
        $tiles = Until { @($Element::FromHandle($window.Handle).FindAll('Descendants', $items) | Where-Object { $_.Current.Name -like 'Capture at*' }) } 5
        $Element::FromHandle($window.Handle).GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
        if (-not $tiles) { 'no capture among its list items' }
    }
    Front $editor
    # The layers list, by its name, as a screen reader finds it. The rail's switch shares
    # the name, so only a list counts.
    function LayersList {
        $Element = [System.Windows.Automation.AutomationElement]
        $named = New-Object System.Windows.Automation.PropertyCondition($Element::NameProperty, 'Layers')
        @($Element::FromHandle($editor.Handle).FindAll('Descendants', $named)) |
            Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::List } | Select-Object -First 1
    }
    Press $Ctrl $Shift 0x4C
    Check 'Ctrl+Shift+L opens the layers list' { if (-not (Until { LayersList } 5)) { 'no list named Layers' } }
    Press $Ctrl $Shift 0x4C
    Check 'Ctrl+Shift+L again closes it' { if (-not (Until { -not (LayersList) } 5)) { 'still open' } }
    # A rectangle, then an oval beside it, in the middle of the capture; Ctrl+[ sends the
    # oval, still chosen, below the rectangle, which the list reads out top first.
    $midX = $editor.X + [int]($editor.Width / 2)
    $midY = $editor.Y + [int]($editor.Height / 2)
    Press 0x52
    [Desk]::Drag($midX - 140, $midY - 60, $midX - 30, $midY + 30)
    Press 0x4F
    [Desk]::Drag($midX + 30, $midY - 60, $midX + 140, $midY + 30)
    Press $Ctrl 0xDB
    Press $Ctrl $Shift 0x4C
    Check 'Ctrl+[ sends the oval below the rectangle, as the layers list reads' {
        $list = Until { LayersList } 5
        if (-not $list) { return 'no list named Layers' }
        $items = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                                                                          [System.Windows.Automation.ControlType]::ListItem)
        $names = @($list.FindAll('Descendants', $items) | ForEach-Object { $_.Current.Name })
        if (($names -join ',') -ne 'Rectangle 1,Oval 1') { "rows read $($names -join ', ')" }
    }
    Press $Ctrl $Shift 0x4C
    [System.Windows.Forms.Clipboard]::Clear()
    Press $Ctrl 0x43
    # Once, in run 37192797212, no PNG arrived although the editor closed, which it does only
    # after the copy: so a miss says what the clipboard held instead, for next time.
    Check 'Ctrl+C copies it as a PNG and as a bitmap' {
        $script:held = @()
        $formats = Until { $script:held = @([System.Windows.Forms.Clipboard]::GetDataObject().GetFormats()); if ($script:held -contains 'PNG') { $script:held } } 10
        if (-not $formats) { return "no PNG on the clipboard after 10 s; it held: $(if ($script:held) { $script:held -join ', ' } else { 'nothing' })" }
        if (-not [System.Windows.Forms.Clipboard]::ContainsImage()) { "no bitmap among $($formats -join ', ')" }
    }
    Check 'Ctrl+C closes the editor without asking' { if (-not (Until { (Editors).Count -eq 0 } 5)) { 'still open' } }
}

Press $Ctrl $Shift 0x31
$editor = Until { Editors | Select-Object -First 1 }
if ($editor)
{
    Start-Sleep -Milliseconds 500
    Front $editor
    $saved = SaveFront
    Check 'Ctrl+S saves a PNG the size of the screen' {
        if (-not $saved) { return "nothing new in $saves" }
        $size = PngSize $saved
        if ($size -ne ('{0}x{1}' -f $screen.Width, $screen.Height)) { "saved $size" }
    }
    Check 'Ctrl+S closes the editor without asking' { if (-not (Until { (Editors).Count -eq 0 } 5)) { 'still open' } }
}

# 5. An area: the overlay, a drag, the editor, the saved size

Check 'Tinysnap leaves the Print Screen setting as it was' {
    $value = (Get-ItemProperty 'HKCU:\Control Panel\Keyboard' -ErrorAction SilentlyContinue).PrintScreenKeyForSnippingEnabled
    if ($value -ne $printScreenSetting) { "the setting holds '$value', not '$printScreenSetting'" }
}
$since = [System.Diagnostics.Stopwatch]::StartNew()
Press 0x2C
$overlay = Timed 'Print Screen to the overlay' { Overlay } 10 $since
Check 'Print Screen covers the screen with the area overlay' { if (-not $overlay) { 'no overlay' } }
if ($overlay)
{
    Start-Sleep -Milliseconds 500
    [Desk]::MoveTo(100, 100)
    Start-Sleep -Milliseconds 150
    [Desk]::Down()
    foreach ($step in 1..12) { Start-Sleep -Milliseconds 25; [Desk]::MoveTo(100 + 400 * $step / 12, 100 + 150 * $step / 12) }
    Start-Sleep -Milliseconds 300
    Shot 'area-overlay'
    [Desk]::Up()
    $editor = Timed 'releasing an area to its editor' { Editors | Select-Object -First 1 }
    Check 'dragging out an area opens it in an editor' { if (-not $editor) { 'no editor' } }
    if ($editor)
    {
        Start-Sleep -Seconds 1
        Shot 'area-editor'
        Check 'the editor for a small area fits on the screen' { OffScreen $editor }
        Front $editor
        $script:area = SaveFront
        Check 'the saved area is the 400 by 150 pixels dragged' {
            if (-not $script:area) { return "nothing new in $saves" }
            $size = PngSize $script:area
            if ($size -ne '400x150') { "saved $size" }
        }
        [void](Until { (Editors).Count -eq 0 } 5)
    }
}
# The press arrives as a real hotkey, so the overlay has the keyboard: Esc closes it.
Press 0x2C
$again = Until { Overlay } 10
if ($again) { Start-Sleep -Milliseconds 300; Press 0x1B }
Check 'Esc closes the overlay Print Screen opened, so it has the keyboard' {
    if (-not $again) { return 'no overlay' }
    if (-not (Until { -not (Overlay) } 5)) { 'still open' }
}
[Desk]::LetGo()

# 5b. A window: Capture Window, a click on the window, just that window saved

Press $Ctrl $Shift 0x57
$overlay = Until { Overlay } 10
Check 'Capture Window covers the screen ready to pick a window' { if (-not $overlay) { 'no overlay' } }
if ($overlay)
{
    Start-Sleep -Milliseconds 500
    [Desk]::MoveTo(510, 180)
    Start-Sleep -Milliseconds 300
    Shot 'window-pick'
    [Desk]::Down(); [Desk]::Up()
    $editor = Until { Editors | Select-Object -First 1 }
    Check 'clicking a window opens just that window in an editor' { if (-not $editor) { 'no editor' } }
    if ($editor)
    {
        Start-Sleep -Milliseconds 500
        Front $editor
        $picked = SaveFront
        Check 'the saved window is the 860 by 200 pixels of the window clicked' {
            if (-not $picked) { return "nothing new in $saves" }
            $size = PngSize $picked
            if ($size -ne '860x200') { "saved $size" }
        }
        if ((Editors).Count -gt 0) { Press $Ctrl 0x57 }
        [void](Until { (Editors).Count -eq 0 } 5)
    }
}

# 6. Text: the overlay again, and the words in the area on the clipboard

[System.Windows.Forms.Clipboard]::Clear()
$plain = Grab
$since = [System.Diagnostics.Stopwatch]::StartNew()
Press $Ctrl $Shift 0x4F
$overlay = Timed 'Ctrl+Shift+O to the overlay' { Overlay } 10 $since
Check 'Ctrl+Shift+O covers the screen to pick text' { if (-not $overlay) { 'no overlay' } }
if ($overlay)
{
    Start-Sleep -Milliseconds 500
    [Desk]::Drag(70, 70, 950, 290)
    $text = Timed 'an area of text to the clipboard' { $t = ClipboardText; if ($t -match 'reads this line') { $t } } 20
    $toast = Until { [Desk]::Windows($script:app.Id) | Where-Object Title -eq 'Text copied' | Select-Object -First 1 } 5
    Start-Sleep -Milliseconds 500
    $toast = [Desk]::Windows($script:app.Id) | Where-Object Title -eq 'Text copied' | Select-Object -First 1
    $withToast = Grab
    Shot 'text-read'
    Check 'the text in the area is read and copied' { if (-not $text) { "clipboard holds '$(ClipboardText)'" } }
    Check 'a toast says so, its rounded corners see-through' { if (-not $toast) { 'no toast' } else { Opaque $toast $withToast $plain } }
}

# 7. Opening a picture with Tinysnap while it runs: the running copy opens it

if ($script:area)
{
    $name = Split-Path -Leaf $script:area
    $second = Start-Process $exe -ArgumentList ('"{0}"' -f $script:area) -PassThru
    $opened = Until { [Desk]::Windows($script:app.Id) | Where-Object Title -eq $name | Select-Object -First 1 } 15
    Check 'opening a PNG with Tinysnap opens it in the running copy' { if (-not $opened) { "no editor titled $name" } }
    Check 'the second copy quits' { if (-not $second.WaitForExit(10000)) { 'still running' } }
    if ($opened)
    {
        Start-Sleep -Seconds 1
        Shot 'opened-file'
        Front $opened
        Press $Ctrl 0x50
        $pin = Until { [Desk]::Windows($script:app.Id) | Where-Object Title -eq 'Pinned capture' | Select-Object -First 1 } 5
        Check 'Ctrl+P pins it and closes the editor' {
            if (-not $pin) { return 'no pin' }
            if ([Desk]::Windows($script:app.Id) | Where-Object Title -eq $name) { 'the editor stayed' }
        }
        Start-Sleep -Milliseconds 500
        $pin = [Desk]::Windows($script:app.Id) | Where-Object Title -eq 'Pinned capture' | Select-Object -First 1
        $withPin = Grab
        Shot 'pinned'
    }
}

# 8. Quitting, the captures kept for after uninstalling

$kept = (LibraryEntries).Count
Stop-Process -Id $script:app.Id -Force -ErrorAction SilentlyContinue
if ($pin)
{
    Start-Sleep -Seconds 1
    $unpinned = Grab
    Check 'the shadow round a pin is see-through' { Opaque $pin $withPin $unpinned }
}
Stop-Process -Id $page.Id -Force -ErrorAction SilentlyContinue

# 9. Updating itself: a newer build in a folder stands in for the next release. Started with nothing
# open, Tinysnap downloads it, quits, and comes back as the new version, saying so.

if ($Next)
{
    $nextVersion = (Get-Content (Join-Path $Next 'releases.win-x64.json') -Raw | ConvertFrom-Json).Assets[0].Version
    $env:TINYSNAP_UPDATE_FEED = (Resolve-Path $Next).Path
    # The first start looked already, which would put the next look a week away.
    Remove-Item (Join-Path $env:APPDATA 'Tinysnap\last-update-check') -ErrorAction SilentlyContinue
    $old = Start-Process $exe -PassThru
    $null = $old.Handle
    $updated = Timed "starting to updated to $nextVersion" {
        Get-Process Tinysnap -ErrorAction SilentlyContinue | Where-Object Id -ne $old.Id |
            ForEach-Object { [Desk]::Windows($_.Id) } | Where-Object Title -eq "Updated to Tinysnap $nextVersion" | Select-Object -First 1
    } 120
    Remove-Item Env:TINYSNAP_UPDATE_FEED
    # Found the moment it exists, before its first frame is drawn.
    Start-Sleep -Milliseconds 700
    Shot 'updated'
    Check 'with nothing open, Tinysnap updates itself, starts again and says so' {
        if (-not $updated) { "no 'Updated to Tinysnap $nextVersion' from a new copy within 2 minutes" }
    }
    Check 'the copy that downloaded the update quit for it' { if (-not $old.HasExited) { 'still running' } }
    Get-Process Tinysnap -ErrorAction SilentlyContinue | Stop-Process -Force
}

# 10. Uninstalling

Check 'uninstalling removes the app, its Start menu entry and its Open with entries' {
    $update = Start-Process (Join-Path $install 'Update.exe') -ArgumentList '--silent', 'uninstall' -PassThru
    $null = $update.Handle
    if (-not $update.WaitForExit(120000)) { return 'still running after 2 minutes' }
    if (-not (Until { -not (Test-Path $exe) } 30)) { return "$exe is still there" }
    if (Test-Path $uninstallKey) { return 'still in Installed apps' }
    if (Test-Path $startMenu) { return 'still in the Start menu' }
    if (Test-Path 'HKCU:\Software\Classes\Tinysnap.Picture') { return 'still offered in Open with' }
}
Check 'uninstalling keeps the library' {
    if ($kept -eq 0) { return "the library was empty before, at $library" }
    $left = (LibraryEntries).Count
    if ($left -ne $kept) { "$kept entries before, $left after" }
}

Finish
