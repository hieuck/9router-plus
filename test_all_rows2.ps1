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
    
    # Find ALL ListBoxItems / rows
    Write-Host "=== All ListBoxItems ==="
    foreach ($child in $children) {
        $name = $child.Current.Name
        $className = $child.Current.ClassName
        $controlType = $child.Current.ControlType
        if ($controlType -eq [System.Windows.Automation.ControlType]::ListItem -or $className -eq "ListBoxItem") {
            $rowChildren = $child.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
            foreach ($rc in $rowChildren) {
                if ($rc.Current.Name -and $rc.Current.Name -like "*@*") {
                    Write-Host "Row email: $($rc.Current.Name) ClassName='$className' Type='$controlType'"
                    break
                }
            }
        }
    }
}

Write-Host "Done"