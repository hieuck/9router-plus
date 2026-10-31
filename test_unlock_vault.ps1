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
        Write-Host "Clicked Credentials button"
        Start-Sleep -Seconds 2
    }
}

# Re-scan Credentials Manager
$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)

# Find the Credentials Manager window
$credsWindow = $null
foreach ($elem in $all) {
    if ($elem.Current.Name -like "*Credentials Manager*" -and $elem.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window) {
        $credsWindow = $elem
        Write-Host "Found Credentials Manager window"
        break
    }
}

if ($credsWindow) {
    $children = $credsWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    Write-Host "=== Credentials Manager children ==="
    for ($i = 0; $i -lt $children.Count; $i++) {
        $child = $children[$i]
        $name = $child.Current.Name
        $autoId = $child.Current.AutomationId
        $controlType = $child.Current.ControlType
        $isEnabled = $child.Current.IsEnabled
        $className = $child.Current.ClassName
        
        if ($name -or $autoId) {
            Write-Host ("  [{0}] Name='{1}' AutoId='{2}' Type='{3}' Enabled={4} Class='{5}'" -f $i, $name, $autoId, $controlType, $isEnabled, $className)
        }
    }
}

Write-Host "Done"