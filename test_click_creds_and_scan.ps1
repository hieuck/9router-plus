Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

$proc = Get-Process -Name 'RouterPlus' | Select-Object -First 1
$hwnd = $proc.MainWindowHandle

$automation = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

# Find and click the Credentials button
$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
$credsBtn = $null
foreach ($elem in $all) {
    if ($elem.Current.AutomationId -eq "CredentialsManagerButton") {
        $credsBtn = $elem
        Write-Host "Found Credentials button"
        break
    }
}

if ($credsBtn) {
    # Try InvokePattern first
    $pattern = $credsBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    if ($pattern) {
        Write-Host "Invoking Credentials button via InvokePattern..."
        $pattern.Invoke()
        Start-Sleep -Seconds 2
    } else {
        Write-Host "No InvokePattern, trying mouse click..."
        $clickablePoint = $credsBtn.GetClickablePoint()
        if ($clickablePoint) {
            [System.Windows.Forms.Cursor]::Position = [System.Drawing.Point]::new([int]$clickablePoint.X, [int]$clickablePoint.Y)
            Start-Sleep -Milliseconds 500
            Add-Type -MemberDefinition @"
                [DllImport("user32.dll")]
                public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint cButtons, uint dwExtraInfo);
"@ -Name "Win32" -Namespace "Native"
            [Native.Win32]::mouse_event(0x0002, 0, 0, 0, 0)
            [Native.Win32]::mouse_event(0x0004, 0, 0, 0, 0)
            Write-Host "Clicked Credentials button via mouse"
            Start-Sleep -Seconds 2
        } else {
            Write-Host "No clickable point for Credentials button"
        }
    }
}

# Re-scan
$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)

# Search for anything with "Credentials" in name
Write-Host "=== Searching for Credentials after click ==="
foreach ($elem in $all) {
    $name = $elem.Current.Name
    $autoId = $elem.Current.AutomationId
    $controlType = $elem.Current.ControlType
    if ($name -like "*Credential*" -or $autoId -like "*Credential*") {
        Write-Host "Name='$name' AutoId='$autoId' Type='$controlType' Class='$($elem.Current.ClassName)' Enabled=$($elem.Current.IsEnabled)"
    }
}

# Also search for windows
Write-Host "=== Windows after click ==="
foreach ($elem in $all) {
    if ($elem.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window) {
        Write-Host "Window: Name='$($elem.Current.Name)' Class='$($elem.Current.ClassName)'"
    }
}

Write-Host "Done"