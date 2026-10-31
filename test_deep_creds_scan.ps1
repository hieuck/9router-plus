Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Get-Process -Name 'RouterPlus' | Select-Object -First 1
$hwnd = $proc.MainWindowHandle

$automation = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

# Open Credentials Manager
$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
$credsBtn = $null
foreach ($elem in $all) {
    if ($elem.Current.AutomationId -eq "CredentialsManagerButton") {
        $credsBtn = $elem
        break
    }
}

if ($credsBtn) {
    $pattern = $credsBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    if ($pattern) { $pattern.Invoke(); Start-Sleep -Seconds 2 }
}

# Re-scan
$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)

# Find the Credentials Manager window
$credsWindow = $null
foreach ($elem in $all) {
    if ($elem.Current.Name -like "*Credentials Manager*" -and $elem.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window) {
        $credsWindow = $elem
        break
    }
}

if ($credsWindow) {
    $children = $credsWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    Write-Host "=== ALL buttons in Credentials Manager ==="
    for ($i = 0; $i -lt $children.Count; $i++) {
        $child = $children[$i]
        if ($child.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button) {
            $name = $child.Current.Name
            $autoId = $child.Current.AutomationId
            $isEnabled = $child.Current.IsEnabled
            $className = $child.Current.ClassName
            Write-Host ("  [{0}] Name='{1}' AutoId='{2}' Enabled={3} Class='{4}'" -f $i, $name, $autoId, $isEnabled, $className)
        }
    }
    
    Write-Host ""
    Write-Host "=== ALL elements with AutomationId ==="
    for ($i = 0; $i -lt $children.Count; $i++) {
        $child = $children[$i]
        $autoId = $child.Current.AutomationId
        if ($autoId) {
            $name = $child.Current.Name
            $controlType = $child.Current.ControlType
            $isEnabled = $child.Current.IsEnabled
            Write-Host ("  [{0}] AutoId='{1}' Name='{2}' Type='{3}' Enabled={4}" -f $i, $autoId, $name, $controlType, $isEnabled)
        }
    }
}

Write-Host "Done"