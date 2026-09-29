[CmdletBinding()]
param(
    [string] $Executable = (Join-Path $PSScriptRoot '..\src\P1Z.Subtitler\bin\Release\net8.0-windows\win-x64\p1z.subtitler.exe')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class SubtitlerSmokeNative
{
    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TOOLWINDOW = 0x00000080L;
    public const int WM_HOTKEY = 0x0312;
    public const int HOTKEY_ID = 0x51A7;
    public static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);
    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowExW(IntPtr parent, IntPtr childAfter, string className, string windowName);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hwnd, StringBuilder text, int maximum);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetMonitorInfoW(IntPtr monitor, ref MONITORINFO info);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(IntPtr hwnd, int index);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    [DllImport("user32.dll")]
    private static extern bool PostMessageW(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    public static long GetExtendedStyle(IntPtr hwnd)
    {
        return IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, GWL_EXSTYLE).ToInt64() : GetWindowLong32(hwnd, GWL_EXSTYLE);
    }

    public static IntPtr[] FindWindows(uint processId, string title)
    {
        var matches = new List<IntPtr>();
        EnumWindows(delegate(IntPtr hwnd, IntPtr ignored)
        {
            uint owner;
            GetWindowThreadProcessId(hwnd, out owner);
            if (owner != processId || !IsWindowVisible(hwnd))
                return true;
            var text = new StringBuilder(256);
            GetWindowTextW(hwnd, text, text.Capacity);
            if (string.Equals(text.ToString(), title, StringComparison.Ordinal))
                matches.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return matches.ToArray();
    }

    public static IntPtr FindMessageWindow(string title)
    {
        var direct = FindWindowExW(HWND_MESSAGE, IntPtr.Zero, null, title);
        if (direct != IntPtr.Zero)
            return direct;
        IntPtr result = IntPtr.Zero;
        EnumChildWindows(HWND_MESSAGE, delegate(IntPtr hwnd, IntPtr ignored)
        {
            var text = new StringBuilder(256);
            GetWindowTextW(hwnd, text, text.Capacity);
            if (string.Equals(text.ToString(), title, StringComparison.Ordinal))
            {
                result = hwnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    public static bool PostSubtitleHotkey()
    {
        var target = FindMessageWindow("p1z.subtitler.hotkey");
        return target != IntPtr.Zero && PostMessageW(target, WM_HOTKEY, new IntPtr(HOTKEY_ID), IntPtr.Zero);
    }
}
'@

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw "FAIL: $Message" }
    Write-Host "PASS: $Message"
}

function Wait-ForWindow([uint32] $ProcessId, [int] $TimeoutMs = 2500) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ($watch.ElapsedMilliseconds -lt $TimeoutMs) {
        $windows = [SubtitlerSmokeNative]::FindWindows($ProcessId, 'p1z.subtitler live subtitle')
        if ($windows.Count -ge 1) { return $windows[0] }
        Start-Sleep -Milliseconds 25
    }
    throw 'FAIL: Timed out waiting for the live subtitle overlay.'
}

function Wait-ForForeground([IntPtr] $Window, [int] $TimeoutMs = 800) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ($watch.ElapsedMilliseconds -lt $TimeoutMs) {
        if ([SubtitlerSmokeNative]::GetForegroundWindow() -eq $Window) { return $true }
        Start-Sleep -Milliseconds 15
    }
    return $false
}

function Wait-ForText([IntPtr] $Window, [string] $Expected, [int] $TimeoutMs = 1200) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ($watch.ElapsedMilliseconds -lt $TimeoutMs) {
        $root = [Windows.Automation.AutomationElement]::FromHandle($Window)
        $condition = [Windows.Automation.PropertyCondition]::new(
            [Windows.Automation.AutomationElement]::ControlTypeProperty,
            [Windows.Automation.ControlType]::Text)
        $element = $root.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
        if ($element -and $element.Current.Name -eq $Expected) { return }
        Start-Sleep -Milliseconds 20
    }
    throw "FAIL: Live subtitle did not become '$Expected'."
}

function Get-Rect([IntPtr] $Window) {
    $rect = [SubtitlerSmokeNative+RECT]::new()
    if (-not [SubtitlerSmokeNative]::GetWindowRect($Window, [ref]$rect)) { throw 'GetWindowRect failed.' }
    return $rect
}

function Get-RectKey([IntPtr] $Window) {
    $rect = Get-Rect $Window
    return "$($rect.Left),$($rect.Top),$($rect.Right),$($rect.Bottom)"
}

function Get-Center([IntPtr] $Window) {
    $rect = Get-Rect $Window
    return [pscustomobject]@{ X = ($rect.Left + $rect.Right) / 2; Y = ($rect.Top + $rect.Bottom) / 2 }
}

function Get-WindowImageHash([IntPtr] $Window) {
    $rect = Get-Rect $Window
    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    $bitmap = [Drawing.Bitmap]::new($width, $height, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $stream = [IO.MemoryStream]::new()
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, [Drawing.Size]::new($width, $height))
        $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
        return -join ($sha.ComputeHash($stream.ToArray()) | ForEach-Object { $_.ToString('x2') })
    }
    finally {
        $sha.Dispose()
        $stream.Dispose()
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Get-ForegroundPixelCount([IntPtr] $Window) {
    $rect = Get-Rect $Window
    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    $bitmap = [Drawing.Bitmap]::new($width, $height, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, [Drawing.Size]::new($width, $height))
        $count = 0
        for ($y = 0; $y -lt $height; $y++) {
            for ($x = 0; $x -lt $width; $x++) {
                $pixel = $bitmap.GetPixel($x, $y)
                if ($pixel.R -ge 250 -and $pixel.G -ge 250 -and $pixel.B -ge 250) { $count++ }
            }
        }
        return $count
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Save-WindowImage([IntPtr] $Window, [string] $Path) {
    $rect = Get-Rect $Window
    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    $bitmap = [Drawing.Bitmap]::new($width, $height, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, [Drawing.Size]::new($width, $height))
        $bitmap.Save($Path, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Assert-InWorkArea([IntPtr] $Window) {
    $rect = Get-Rect $Window
    $monitor = [SubtitlerSmokeNative]::MonitorFromWindow($Window, 2)
    $info = [SubtitlerSmokeNative+MONITORINFO]::new()
    $info.cbSize = [Runtime.InteropServices.Marshal]::SizeOf($info)
    [void][SubtitlerSmokeNative]::GetMonitorInfoW($monitor, [ref]$info)
    Assert-True ($rect.Left -ge $info.rcWork.Left -and $rect.Top -ge $info.rcWork.Top -and $rect.Right -le $info.rcWork.Right -and $rect.Bottom -le $info.rcWork.Bottom) 'live subtitle stays completely inside the monitor work area'
}

function Send-Text([string] $Text) {
    [Windows.Forms.SendKeys]::SendWait($Text)
    Start-Sleep -Milliseconds 45
}

$targetForm = $null
$app = $null
try {
    $resolvedExecutable = (Resolve-Path -LiteralPath $Executable).Path
    $targetForm = [Windows.Forms.Form]::new()
    $targetForm.Text = 'p1z.subtitler smoke target'
    $targetForm.Width = 900
    $targetForm.Height = 600
    $targetForm.StartPosition = 'CenterScreen'
    $targetInput = [Windows.Forms.TextBox]::new()
    $targetInput.Multiline = $true
    $targetInput.Dock = 'Fill'
    $targetForm.Controls.Add($targetInput)
    $targetForm.Show()
    $targetForm.Activate()
    $targetInput.Focus()
    [Windows.Forms.Application]::DoEvents()
    $targetWindow = $targetForm.Handle
    Assert-True ($targetWindow -ne [IntPtr]::Zero) 'external application window is available'

    $app = Start-Process -FilePath $resolvedExecutable -PassThru
    Start-Sleep -Milliseconds 1800
    Assert-True (-not $app.HasExited) 'tray application stays running without a conventional main window'

    [void][SubtitlerSmokeNative]::SetForegroundWindow($targetWindow)
    $targetInput.Focus()
    [Windows.Forms.SendKeys]::SendWait('^%s')
    $overlay = Wait-ForWindow ([uint32]$app.Id)
    Assert-True ($overlay -ne [IntPtr]::Zero) 'physical Ctrl+Alt+S activates live subtitle mode from another application'
    Assert-True (Wait-ForForeground $overlay) 'live subtitle overlay captures keyboard focus'
    $style = [SubtitlerSmokeNative]::GetExtendedStyle($overlay)
    Assert-True (($style -band [SubtitlerSmokeNative]::WS_EX_TOOLWINDOW) -ne 0) 'overlay remains absent from the taskbar and Alt-Tab list'

    Send-Text 'h'
    Wait-ForText $overlay 'h'
    $hPixels = Get-ForegroundPixelCount $overlay
    $hRect = Get-Rect $overlay
    $firstCenter = Get-Center $overlay
    Send-Text 'e'
    Wait-ForText $overlay 'he'
    $hePixels = Get-ForegroundPixelCount $overlay
    Assert-True ($hePixels -gt $hPixels) 'each typed glyph is painted without waiting for the following keystroke'
    Send-Text 'llo worl'
    Wait-ForText $overlay 'hello worl'
    $longRect = Get-Rect $overlay
    Assert-True (($longRect.Right - $longRect.Left) -gt ($hRect.Right - $hRect.Left)) 'background rectangle grows with the live text'
    $grownCenter = Get-Center $overlay
    Assert-True ([Math]::Abs($firstCenter.X - $grownCenter.X) -le 2 -and [Math]::Abs($firstCenter.Y - $grownCenter.Y) -le 2) 'text growth stays anchored to the session position'
    Send-Text '{BACKSPACE}'
    Wait-ForText $overlay 'hello wor'
    Assert-True ($targetInput.Text.Length -eq 0) 'the underlying application receives none of the active-session typing'
    Assert-InWorkArea $overlay

    $lowercaseSamples = 1..10 | ForEach-Object { $value = Get-RectKey $overlay; Start-Sleep -Milliseconds 38; $value }
    Assert-True (($lowercaseSamples | Select-Object -Unique).Count -eq 1) 'lowercase live text remains stable'
    Send-Text '{ENTER}'
    Start-Sleep -Milliseconds 300
    Assert-True ([SubtitlerSmokeNative]::FindWindows([uint32]$app.Id, 'p1z.subtitler live subtitle').Count -eq 0) 'Enter immediately removes the subtitle and ends the session'
    Assert-True ([SubtitlerSmokeNative]::GetForegroundWindow() -eq $targetWindow) 'Enter returns focus to the previous application'
    Send-Text 'normal'
    [Windows.Forms.Application]::DoEvents()
    Assert-True ($targetInput.Text -eq 'normal') 'normal typing returns to the underlying application after Enter'

    $targetInput.Clear()
    $targetInput.Focus()
    Assert-True ([SubtitlerSmokeNative]::PostSubtitleHotkey()) 'a second hotkey press starts a fresh session'
    $overlay = Wait-ForWindow ([uint32]$app.Id)
    Send-Text 'bro'
    Wait-ForText $overlay 'bro'
    $secondCenter = Get-Center $overlay
    Assert-True ([Math]::Abs($firstCenter.X - $secondCenter.X) -gt 2 -or [Math]::Abs($firstCenter.Y - $secondCenter.Y) -gt 2) 'a new session chooses a new randomized center-region anchor'
    Send-Text ' WHAT'
    Wait-ForText $overlay 'bro WHAT'
    $shakingRects = 1..12 | ForEach-Object { $value = Get-RectKey $overlay; Start-Sleep -Milliseconds 38; $value }
    Assert-True (($shakingRects | Select-Object -Unique).Count -eq 1) 'uppercase shaking never moves the background rectangle'
    $shakingFrames = 1..16 | ForEach-Object { $value = Get-WindowImageHash $overlay; Start-Sleep -Milliseconds 38; $value }
    # A blinking caret alone accounts for two distinct frames, so motion has to beat that.
    Assert-True (($shakingFrames | Select-Object -Unique).Count -gt 2) 'uppercase characters wobble independently inside the stationary rectangle'
    Send-Text '{BACKSPACE 5}'
    Wait-ForText $overlay 'bro'
    Start-Sleep -Milliseconds 80
    $stableAgain = 1..10 | ForEach-Object { $value = Get-RectKey $overlay; Start-Sleep -Milliseconds 38; $value }
    Assert-True (($stableAgain | Select-Object -Unique).Count -eq 1) 'removing uppercase text stops the shake immediately'
    $stableFrames = 1..8 | ForEach-Object { $value = Get-WindowImageHash $overlay; Start-Sleep -Milliseconds 38; $value }
    Assert-True (($stableFrames | Select-Object -Unique).Count -eq 1) 'lowercase glyph rendering remains visually still'
    Assert-True ($targetInput.Text.Length -eq 0) 'the second live session also isolates keyboard input'
    Send-Text '{ESC}'
    Start-Sleep -Milliseconds 300
    Assert-True ([SubtitlerSmokeNative]::FindWindows([uint32]$app.Id, 'p1z.subtitler live subtitle').Count -eq 0) 'Escape discards the live subtitle and returns to idle'
    Assert-True ([SubtitlerSmokeNative]::GetForegroundWindow() -eq $targetWindow) 'Escape restores the previous application focus'

    $targetInput.Clear()
    $targetInput.Focus()
    Assert-True ([SubtitlerSmokeNative]::PostSubtitleHotkey()) 'a third session starts for the final-glyph regression'
    $overlay = Wait-ForWindow ([uint32]$app.Id)
    Send-Text 'please work bro '
    Wait-ForText $overlay 'please work bro '
    $beforeFinalGlyph = Get-ForegroundPixelCount $overlay
    Save-WindowImage $overlay (Join-Path $PSScriptRoot '..\artifacts\before-final-p.png')
    Send-Text 'p'
    Wait-ForText $overlay 'please work bro p'
    $afterFinalGlyph = Get-ForegroundPixelCount $overlay
    Save-WindowImage $overlay (Join-Path $PSScriptRoot '..\artifacts\after-final-p.png')
    Write-Host "Final-p foreground pixels: $beforeFinalGlyph -> $afterFinalGlyph"
    $textElement = [Windows.Automation.AutomationElement]::FromHandle($overlay).FindFirst(
        [Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new(
            [Windows.Automation.AutomationElement]::ControlTypeProperty,
            [Windows.Automation.ControlType]::Text))
    Write-Host "Final-p render status: $($textElement.Current.ItemStatus)"
    Assert-True ($afterFinalGlyph -gt $beforeFinalGlyph) 'the final p in "please work bro p" is painted immediately'
    Send-Text '{ESC}'

    $app.Refresh()
    $cpuBefore = $app.TotalProcessorTime
    Start-Sleep -Milliseconds 1500
    $app.Refresh()
    $idleCpuMs = ($app.TotalProcessorTime - $cpuBefore).TotalMilliseconds
    Assert-True ($idleCpuMs -lt 150) "idle CPU stays low ($([Math]::Round($idleCpuMs, 1)) ms used over 1500 ms)"
    Write-Host 'NATIVE LIVE-TYPING SMOKE PASS'
}
finally {
    if ($app -and -not $app.HasExited) { $app.Kill(); $app.WaitForExit() }
    if ($targetForm) { $targetForm.Close(); $targetForm.Dispose() }
}
