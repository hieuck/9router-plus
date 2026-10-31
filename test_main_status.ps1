Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Get-Process -Name 'RouterPlus' | Select-Object -First 1
$hwnd = $proc.MainWindowHandle

$automation = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)

# Search for status text in main UI
Write-Host "=== Searching for status in main UI ==="
foreach ($elem in $all) {
    $name = $elem.Current.Name
    $autoId = $elem.Current.AutomationId
    if ($autoId -eq "StatusText" -or $name -like "*sync*" -or $name -like "*Sync*" -or $name -like "*provider*" -or $name -like "*Provider*") {
        Write-Host "Name='$name' AutoId='$autoId' Type='$($elem.Current.ControlType)' Enabled=$($elem.Current.IsEnabled) Class='$($elem.Current.ClassName)'"
    }
}

Write-Host "Done"