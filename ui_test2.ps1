Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName System.Windows.Forms

$process = Get-Process -Name RouterPlus | Select-Object -First 1
if (-not $process) { Write-Host 'RouterPlus process not found'; exit }

$root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)

# Find all elements with "Agnes" in name
$condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::NameProperty, 'Agnes')
$elements = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
Write-Host "Elements with Name='Agnes': $($elements.Count)"
foreach ($el in $elements) {
    Write-Host ("  - {0} Name='{1}' AutoId='{2}'" -f $el.Current.ControlType, $el.Current.Name, $el.Current.AutomationId)
}

# Find all buttons and check their names
$btnCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
$allButtons = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCondition)
Write-Host "All buttons ($($allButtons.Count)):"
foreach ($btn in $allButtons) {
    $name = $btn.Current.Name
    if ($name -like "*Agnes*" -or $name -like "*Tự*" -or $name -like "*động*" -or $name -like "*thêm*" -or $name -like "*Auto*" -or $name -like "*Add*") {
        Write-Host ("  - Button: '{0}' IsEnabled: {1} AutoId: {2}" -f $name, $btn.Current.IsEnabled, $btn.Current.AutomationId)
    }
}

# Find all elements containing "provider" or "Provider"
$providerCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::NameProperty, 'Provider')
$providers = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $providerCondition)
Write-Host "Elements with 'Provider' in name ($($providers.Count)):"
foreach ($el in $providers) {
    Write-Host ("  - {0} Name='{1}' AutoId='{2}'" -f $el.Current.ControlType, $el.Current.Name, $el.Current.AutomationId)
}

# Find all ListBoxItems again but get their children
$profileListCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, 'ProfileList')
$profileList = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $profileListCondition)
if ($profileList) {
    $itemCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
    $items = $profileList.FindAll([System.Windows.Automation.TreeScope]::Descendants, $itemCondition)
    Write-Host ("ProfileList items ({0}) - exploring first 3:" -f $items.Count)
    for ($i=0; $i -lt [Math]::Min(3, $items.Count); $i++) {
        $item = $items[$i]
        Write-Host ("  Item {0}: {1} AutoId={2}" -f $i, $item.Current.Name, $item.Current.AutomationId)
        # Get children
        $children = $item.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
        foreach ($child in $children) {
            Write-Host ("    Child: {0} Name='{1}' AutoId='{2}'" -f $child.Current.ControlType, $child.Current.Name, $child.Current.AutomationId)
        }
    }
}