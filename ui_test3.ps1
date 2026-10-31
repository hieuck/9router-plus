Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName System.Windows.Forms

$process = Get-Process -Name RouterPlus | Select-Object -First 1
if (-not $process) { Write-Host 'RouterPlus process not found'; exit }

$root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)

# Find ProfileList
$profileListCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, 'ProfileList')
$profileList = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $profileListCondition)
if ($profileList) {
    $itemCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
    $items = $profileList.FindAll([System.Windows.Automation.TreeScope]::Descendants, $itemCondition)
    Write-Host ("ProfileList items ({0}):" -f $items.Count)
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
    }
}

# Find the Agnes (88) button and its parent
$btnCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
$allButtons = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCondition)
foreach ($btn in $allButtons) {
    if ($btn.Current.Name -eq "Agnes (88)") {
        Write-Host ("Found 'Agnes (88)' button:")
        Write-Host ("  IsEnabled: {0}" -f $btn.Current.IsEnabled)
        # Get parent
        $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($btn)
        while ($parent) {
            Write-Host ("  Parent: {0} Name='{1}' AutoId='{2}'" -f $parent.Current.ControlType, $parent.Current.Name, $parent.Current.AutomationId)
            $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($parent)
        }
    }
}