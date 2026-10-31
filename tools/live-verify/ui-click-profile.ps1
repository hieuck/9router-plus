Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Get-Process -Name 'RouterPlus' | Select-Object -First 1
$hwnd = $proc.MainWindowHandle

$automation = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

# Find the profile list
$listCond = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "ProfileList")
$profileList = $automation.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $listCond)

if ($profileList) {
    $items = $profileList.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
    for ($i = 0; $i -lt $items.Count; $i++) {
        $item = $items[$i]
        $itemName = $item.Current.Name
        if ($itemName -like "*hieuck.it*") {
            Write-Host ">>> Clicking profile at index " + $i
            $invokePattern = $item.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
            if ($invokePattern) {
                $invokePattern.Invoke()
                Write-Host "Invoked the profile"
            } else {
                $selPattern = $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
                if ($selPattern) {
                    $selPattern.Select()
                    Write-Host "Selected the profile via SelectionItemPattern"
                } else {
                    Write-Host "No InvokePattern or SelectionItemPattern"
                }
            }
            Start-Sleep -Seconds 2
        }
    }
}

# Check SelectedProfileName again
$selectedCond = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "SelectedProfileName")
$selectedProfile = $automation.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $selectedCond)

if ($selectedProfile) {
    Write-Host "SelectedProfileName: '" + $selectedProfile.Current.Name + "'"
} else {
    Write-Host "SelectedProfileName NOT found"
}

# Also check StatusText
$statusCond = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "StatusText")
$statusText = $automation.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $statusCond)

if ($statusText) {
    Write-Host "StatusText: '" + $statusText.Current.Name + "'"
} else {
    Write-Host "StatusText NOT found"
}