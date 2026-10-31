Add-Type @'
using System;
using System.Runtime.InteropServices;
public class Win32 {
    [DllImport("user32.dll")] public static extern bool EnumThreadWindows(int dwThreadId, EnumThreadDelegate lpfn, IntPtr lParam);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);
    public delegate bool EnumThreadDelegate(IntPtr hWnd, IntPtr lParam);
}
'@

$process = Get-Process -Id 37772
Write-Host "Process: " $process.Id
Write-Host "MainWindowHandle: " $process.MainWindowHandle
Write-Host "MainWindowTitle: " $process.MainWindowTitle
Write-Host "Threads: " $process.Threads.Count

$windows = @()
foreach ($thread in $process.Threads) {
    [Win32]::EnumThreadWindows($thread.Id, {
        param($hWnd, $lParam)
        $len = [Win32]::GetWindowTextLength($hWnd)
        $sb = New-Object System.Text.StringBuilder($len + 1)
        [Win32]::GetWindowText($hWnd, $sb, $len + 1) | Out-Null
        $class = New-Object System.Text.StringBuilder(256)
        [Win32]::GetClassName($hWnd, $class, 256) | Out-Null
        $visible = [Win32]::IsWindowVisible($hWnd)
        $global:windows += [pscustomobject]@{Hwnd=$hWnd; Title=$sb.ToString(); Class=$class.ToString(); Visible=$visible}
        return $true
    }, [IntPtr]::Zero) | Out-Null
}

$windows | Where-Object {$_.Visible -eq $true} | Format-Table Hwnd, Title, Class, Visible -AutoSize