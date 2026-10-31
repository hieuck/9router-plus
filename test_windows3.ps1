Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Get-Process -Name 'RouterPlus' | Select-Object -First 1

Add-Type -MemberDefinition @"
    [DllImport("user32.dll")]
    public static extern bool EnumThreadWindows(int dwThreadId, EnumThreadDelegate lpfn, IntPtr lParam);
    
    [DllImport("user32.dll")]
    public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);
    
    [DllImport("user32.dll")]
    public static extern int GetWindowTextLength(IntPtr hWnd);
    
    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);
    
    public delegate bool EnumThreadDelegate(IntPtr hWnd, IntPtr lParam);
"@ -Name "Win32" -Namespace "Native"

$hwnds = @()
foreach ($thread in $proc.Threads) {
    $list = @()
    $callback = [Native.Win32+EnumThreadDelegate] {
        param($hWnd, $lParam)
        if ([Native.Win32]::IsWindowVisible($hWnd)) {
            $length = [Native.Win32]::GetWindowTextLength($hWnd)
            if ($length -gt 0) {
                $sb = New-Object System.Text.StringBuilder($length + 1)
                [Native.Win32]::GetWindowText($hWnd, $sb, $sb.Capacity) | Out-Null
                $list += @{ Hwnd = $hWnd; Title = $sb.ToString() }
            }
        }
        return $true
    }
    [Native.Win32]::EnumThreadWindows($thread.Id, $callback, [IntPtr]::Zero)
    $hwnds += $list
}

Write-Host "=== All visible windows in RouterPlus process ==="
foreach ($w in $hwnds) {
    Write-Host "HWND=$($w.Hwnd) Title='$($w.Title)'"
}

Write-Host "Done"