Add-Type -AssemblyName 'UIAutomationClient'
Add-Type -AssemblyName 'UIAutomationTypes'

# Find the RouterPlus window
$wins = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
$window = $null
foreach ($w in $wins) {
    if ($w.Current.Name -like '*RouterPlus*' -or $w.Current.Name -like '*9Router*' -or $w.Current.ProcessId -eq (Get-Process -Name 'RouterPlus' -ErrorAction SilentlyContinue).Id) {
        $window = $w
        break
    }
}
if (-not $window) {
    Write-Host 'ERROR: RouterPlus window not found'
    exit 1
}
Write-Host "Found window: $($window.Current.Name) PID: $($window.Current.ProcessId)"

# Find the ProfileList
$list = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, 
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "ProfileList"))
if (-not $list) {
    Write-Host 'ERROR: ProfileList not found'
    exit 1
}
Write-Host "Found ProfileList"

# Find the list item for hieuck.adobe2022@gmail.com
$items = $list.FindAll([System.Windows.Automation.TreeScope]::Descendants, 
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem))

$targetItem = $null
foreach ($item in $items) {
    $children = $item.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($child in $children) {
        if ($child.Current.Name -eq 'hieuck.adobe2022@gmail.com') {
            $targetItem = $item
            break
        }
    }
    if ($targetItem) { break }
}

if (-not $targetItem) {
    Write-Host 'ERROR: Profile hieuck.adobe2022@gmail.com not found in list'
    exit 1
}
Write-Host "Found target profile item"

# Select the item
$selectionPattern = $targetItem.GetCurrentPattern([System.Windows.Automation.SelectionItemPatternIdentifiers]::Pattern)
if (-not $selectionPattern) {
    Write-Host 'ERROR: SelectionItemPattern not available'
    exit 1
}
$selectionPattern.Select()
Write-Host 'Selected profile: hieuck.adobe2022@gmail.com'

Start-Sleep -Seconds 3

# Verify selection by checking SelectedProfileName
$selectedName = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, 
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "SelectedProfileName"))
if ($selectedName) {
    Write-Host "Verified selection: $($selectedName.Current.Name)"
} else {
    Write-Host 'WARNING: Could not verify selection'
}