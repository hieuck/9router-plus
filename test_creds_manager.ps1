Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Get-Process -Name 'RouterPlus' | Select-Object -First 1
$hwnd = $proc.MainWindowHandle

$automation = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)

# Search for anything with "Credentials" in name
Write-Host "=== Searching for Credentials ==="
foreach ($elem in $all) {
    $name = $elem.Current.Name
    $autoId = $elem.Current.AutomationId
    if ($name -like "*Credential*" -or $autoId -like "*Credential*") {
        Write-Host "Name='$name' AutoId='$autoId' Type='$($elem.Current.ControlType)' Class='$($elem.Current.ClassName)' Enabled=$($elem.Current.IsEnabled)"
    }
}

Write-Host "Done"