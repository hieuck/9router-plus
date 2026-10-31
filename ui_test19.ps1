Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName System.Windows.Forms

Write-Host "Starting test..."

$process = Get-Process -Name RouterPlus | Select-Object -First 1
if (-not $process) { Write-Host 'RouterPlus process not found'; exit }

Write-Host "Found process: $($process.Id)"

$root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
Write-Host "Got root element"

# Find ProfileList - check which item is selected
$profileListCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, 'ProfileList')
$profileList = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $profileListCondition)
if ($profileList) {
    $itemCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
    $items = $profileList.FindAll([System.Windows.Automation.TreeScope]::Descendants, $itemCondition)
    for ($i=0; $i -lt $items.Count; $i++) {
        $item = $items[$i]
        $children = $item.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
        $email = ""
        foreach ($child in $children) {
            if ($child.Current.Name -like "*@*") {
                $email = $child.Current.Name
            }
        }
        $isSelected = $item.Current.IsSelectionItemSelectionEnabled -and $item.Current.IsSelected
        Write-Host ("Item {0}: Email='{1}' IsSelected={2}" -f $i, $email, $item.Current.IsSelected)
    }
}

# Wait a bit
Start-Sleep -Seconds 2

# Find ProviderCardViewModel elements again
$condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::NameProperty, 'RouterPlus.App.ViewModels.ProviderCardViewModel')
$elements = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)

Write-Host ("`nFound $($elements.Count) ProviderCardViewModel elements")

foreach ($el in $elements) {
    $children = $el.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($child in $children) {
        if ($child.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button) {
            $name = $child.Current.Name
            $bytes = [System.Text.Encoding]::UTF8.GetBytes($name)
            Write-Host ("  Button: '{0}' (bytes: {1}) IsEnabled={2}" -f $name, [System.BitConverter]::ToString($bytes), $child.Current.IsEnabled)
        }
    }
}