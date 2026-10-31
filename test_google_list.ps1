Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Get-Process -Name 'RouterPlus' | Select-Object -First 1
$hwnd = $proc.MainWindowHandle

$automation = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)

# Find the Google tab in Credentials Manager and click it
$googleTab = $null
foreach ($elem in $all) {
    if ($elem.Current.AutomationId -eq "GoogleAccountsTab") {
        $googleTab = $elem
        break
    }
}

if ($googleTab) {
    $pattern = $googleTab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
    if ($pattern) {
        $pattern.Select()
        Write-Host "Selected Google tab"
        Start-Sleep -Seconds 1
    } else {
        Write-Host "SelectionItemPattern not available for Google tab"
    }
}

# Re-scan
$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)

# Find all ListBoxItems in GoogleAccountsList
Write-Host "=== All ListBoxItems ==="
foreach ($elem in $all) {
    if ($elem.Current.ClassName -eq "GoogleAccountRowViewModel") {
        $children = $elem.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
        foreach ($child in $children) {
            if ($child.Current.Name -and $child.Current.Name -like "*@*") {
                Write-Host "Row email: $($child.Current.Name)"
                # Show full row state
                Write-Host "  Row: Name='$($elem.Current.Name)' Class='$($elem.Current.ClassName)' Enabled=$($elem.Current.IsEnabled)"
            }
        }
    }
}

Write-Host "Done"