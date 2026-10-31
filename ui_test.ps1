Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName System.Windows.Forms

$process = Get-Process -Name RouterPlus | Select-Object -First 1
if (-not $process) { Write-Host 'RouterPlus process not found'; exit }

Write-Host "Found RouterPlus process: $($process.Id)"

$root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
if (-not $root) { Write-Host 'Could not get root element'; exit }

Write-Host "Root element: $($root.Current.Name) $($root.Current.ControlType)"

# Find ProfileSearchTextBox
$searchBoxCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, 'ProfileSearchTextBox')
$searchBox = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $searchBoxCondition)
if ($searchBox) {
    Write-Host "Found ProfileSearchTextBox: $($searchBox.Current.Name) $($searchBox.Current.ControlType)"
} else {
    Write-Host 'ProfileSearchTextBox NOT found'
}

# Find ProfileList
$profileListCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, 'ProfileList')
$profileList = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $profileListCondition)
if ($profileList) {
    Write-Host "Found ProfileList: $($profileList.Current.Name) $($profileList.Current.ControlType)"
} else {
    Write-Host 'ProfileList NOT found'
}

# Find all ListBoxItems in ProfileList
if ($profileList) {
    $itemCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
    $items = $profileList.FindAll([System.Windows.Automation.TreeScope]::Descendants, $itemCondition)
    Write-Host "Found $($items.Count) ListBoxItems:"
    foreach ($item in $items) {
        Write-Host "  - $($item.Current.Name) (AutomationId: $($item.Current.AutomationId))"
    }
}

# Find Agnes card
$agnesCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::NameProperty, 'Agnes')
$agnesCard = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $agnesCondition)
if ($agnesCard) {
    Write-Host "Found Agnes card: $($agnesCard.Current.Name) $($agnesCard.Current.ControlType)"
    
    # Find 'Tự động thêm' button - use contains search instead
    $allButtons = $agnesCard.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button))
    Write-Host "Found $($allButtons.Count) buttons in Agnes card:"
    foreach ($btn in $allButtons) {
        Write-Host "  - Button: '$($btn.Current.Name)' IsEnabled: $($btn.Current.IsEnabled)"
    }
} else {
    Write-Host 'Agnes card NOT found'
}