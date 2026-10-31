Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName System.Windows.Forms

$process = Get-Process -Name RouterPlus | Select-Object -First 1
if (-not $process) { Write-Host 'RouterPlus process not found'; exit }

$root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)

# Find ProfileList - show ALL items without filtering
$profileListCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, 'ProfileList')
$profileList = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $profileListCondition)
if ($profileList) {
    $itemCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
    $items = $profileList.FindAll([System.Windows.Automation.TreeScope]::Descendants, $itemCondition)
    Write-Host ("ALL ProfileList items ({0}):" -f $items.Count)
    for ($i=0; $i -lt $items.Count; $i++) {
        $item = $items[$i]
        $children = $item.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
        $email = ""
        $name = ""
        foreach ($child in $children) {
            if ($child.Current.Name -like "*@*") {
                $email = $child.Current.Name
            }
            if ($child.Current.Name -notlike "*@*" -and $child.Current.Name -ne "" -and $child.Current.Name -ne "›" -and $child.Current.Name -notmatch "^\d+$" -and $child.Current.Name -notmatch "^[A-Z]$") {
                $name = $child.Current.Name
            }
        }
        Write-Host ("  Item {0}: Name='{1}' Email='{2}'" -f $i, $name, $email)
    }
}