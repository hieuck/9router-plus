Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName System.Windows.Forms

$process = Get-Process -Name RouterPlus | Select-Object -First 1
if (-not $process) { Write-Host 'RouterPlus process not found'; exit }

$root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)

# Find ProfileSearchTextBox
$searchBoxCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, 'ProfileSearchTextBox')
$searchBox = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $searchBoxCondition)
if ($searchBox) {
    $pattern = $searchBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    if ($pattern) {
        $pattern.SetValue("hieuck.adobe2022")
        Write-Host "Set search text to 'hieuck.adobe2022'"
    } else {
        Write-Host "ValuePattern not available on search box"
    }
} else {
    Write-Host 'ProfileSearchTextBox NOT found'
}

# Wait a bit for filtering
Start-Sleep -Seconds 1

# Find ProfileList and check filtered items
$profileListCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, 'ProfileList')
$profileList = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $profileListCondition)
if ($profileList) {
    $itemCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
    $items = $profileList.FindAll([System.Windows.Automation.TreeScope]::Descendants, $itemCondition)
    Write-Host ("Filtered ProfileList items ({0}):" -f $items.Count)
    for ($i=0; $i -lt $items.Count; $i++) {
        $item = $items[$i]
        $children = $item.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
        $email = ""
        foreach ($child in $children) {
            if ($child.Current.Name -like "*@*") {
                $email = $child.Current.Name
            }
        }
        Write-Host ("  Item {0}: Email='{1}'" -f $i, $email)
        if ($email -eq "hieuck.adobe2022@gmail.com") {
            Write-Host ("    -> SELECTING THIS ITEM")
            $selectionPattern = $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
            if ($selectionPattern) {
                $selectionPattern.Select()
                Write-Host "    Selection successful"
            } else {
                Write-Host "    SelectionItemPattern not available"
            }
        }
    }
}

# Now search for Agnes provider card in the selected profile
Start-Sleep -Seconds 1

# Find all elements with "Agnes" in name (should find the card now)
$condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::NameProperty, 'Agnes')
$elements = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
Write-Host ("Elements with Name='Agnes': $($elements.Count)")
foreach ($el in $elements) {
    Write-Host ("  - {0} Name='{1}' AutoId='{2}'" -f $el.Current.ControlType, $el.Current.Name, $el.Current.AutomationId)
    # Get children of this element
    $children = $el.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($child in $children) {
        if ($child.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button) {
            Write-Host ("    Button: '{0}' IsEnabled: {1}" -f $child.Current.Name, $child.Current.IsEnabled)
        }
    }
}