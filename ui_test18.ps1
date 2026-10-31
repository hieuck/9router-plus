Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName System.Windows.Forms

Write-Host "Starting test..."

$process = Get-Process -Name RouterPlus | Select-Object -First 1
if (-not $process) { Write-Host 'RouterPlus process not found'; exit }

Write-Host "Found process: $($process.Id)"

$root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
Write-Host "Got root element"

# Find ProfileList and select hieuck.adobe2022@gmail.com
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
        if ($email -eq "hieuck.adobe2022@gmail.com") {
            Write-Host ("SELECTING Item {0}: Email='{1}'" -f $i, $email)
            $selectionPattern = $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
            if ($selectionPattern) {
                $selectionPattern.Select()
                Write-Host "Selection successful"
            } else {
                Write-Host "SelectionItemPattern not available"
            }
            break
        }
    }
}

# Wait for UI to update
Start-Sleep -Seconds 2

# Find ALL buttons in the entire window
$btnCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
$allButtons = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCondition)

Write-Host "Found $($allButtons.Count) buttons total"

# Find the "Tự động thêm" button - just look at all button names and their bytes
foreach ($btn in $allButtons) {
    $name = $btn.Current.Name
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($name)
    if ($name -like "*Tự*" -or $name -like "*thêm*" -or $name -like "*Auto*" -or $name -like "*Add*") {
        Write-Host ("Button: '{0}' (bytes: {1}) IsEnabled={2}" -f $name, [System.BitConverter]::ToString($bytes), $btn.Current.IsEnabled)
    }
}