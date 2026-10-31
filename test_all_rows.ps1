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
    
    # Find ALL rows with GoogleAccountRowViewModel
    Write-Host "=== All GoogleAccountRowViewModel rows ==="
    foreach ($child in $children) {
        if ($child.Current.ClassName -eq "GoogleAccountRowViewModel") {
            $rowChildren = $child.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
            foreach ($rc in $rowChildren) {
                if ($rc.Current.Name -and $rc.Current.Name -like "*@*") {
                    Write-Host "Row email: $($rc.Current.Name) - HasCredentials=$($child.Current.HasCredentials) IsEditing=$($child.Current.IsEditing)"
                    break
                }
            }
        }
    }
}

Write-Host "Done"