Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Get-Process -Name 'RouterPlus' | Select-Object -First 1
$hwnd = $proc.MainWindowHandle

$automation = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)

# Search for Unlock Vault button in main UI
Write-Host "=== Searching for Unlock Vault in main UI ==="
foreach ($elem in $all) {
    $name = $elem.Current.Name
    $autoId = $elem.Current.AutomationId
    if ($name -like "*Unlock*" -or $name -like "*unlock*" -or $name -like "*Mở*" -or $autoId -like "*Unlock*" -or $autoId -like "*unlock*" -or $autoId -like "*Vault*" -or $autoId -like "*vault*") {
        Write-Host "Name='$name' AutoId='$autoId' Type='$($elem.Current.ControlType)' Enabled=$($elem.Current.IsEnabled) Class='$($elem.Current.ClassName)'"
    }
}

# Also check if there's a vault status indicator in main UI
Write-Host "=== Searching for vault status in main UI ==="
foreach ($elem in $all) {
    $name = $elem.Current.Name
    $autoId = $elem.Current.AutomationId
    if ($name -like "*vault*" -or $name -like "*Vault*" -or $name -like "*locked*" -or $name -like "*Locked*") {
        Write-Host "Name='$name' AutoId='$autoId' Type='$($elem.Current.ControlType)' Enabled=$($elem.Current.IsEnabled) Class='$($elem.Current.ClassName)'"
    }
}

Write-Host "Done"