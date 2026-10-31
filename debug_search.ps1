Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

$profileToolHwnd = [IntPtr]207558060
$mainWindow = [System.Windows.Automation.AutomationElement]::FromHandle($profileToolHwnd)
if (-not $mainWindow) {
    Write-Host "Main window not found"
    exit 1
}

Write-Host "=== Before search: All ProfileList descendants ==="
$profileList = $mainWindow.FindFirst(
    [System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "ProfileList")
)

if ($profileList) {
    $allDescendants = $profileList.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($elem in $allDescendants) {
        $name = $elem.Current.Name
        if ($name -and $name.Length -gt 0) {
            Write-Host ("  Name='{0}', ControlType={1}, AutomationId='{2}'" -f $name, $elem.Current.ControlType, $elem.Current.AutomationId)
        }
    }
} else {
    Write-Host "ProfileList not found"
}

Write-Host ""
Write-Host "=== All ListItems in window ==="
$allElements = $mainWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
foreach ($elem in $allElements) {
    if ($elem.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem) {
        Write-Host ("  ListItem: Name='{0}', AutomationId='{1}'" -f $elem.Current.Name, $elem.Current.AutomationId)
    }
}

Write-Host ""
Write-Host "=== All TextBlocks in window ==="
foreach ($elem in $allElements) {
    if ($elem.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text) {
        $name = $elem.Current.Name
        if ($name -and $name.Length -gt 0) {
            Write-Host ("  Text: '{0}', AutomationId='{1}'" -f $name, $elem.Current.AutomationId)
        }
    }
}

Write-Host ""
Write-Host "=== Check if profile exists in Chrome profiles list ==="
foreach ($elem in $allElements) {
    $name = $elem.Current.Name
    if ($name -and $name -like "*hieuck*") {
        Write-Host ("  Found hieuck: '{0}', ControlType={1}, AutomationId='{2}'" -f $name, $elem.Current.ControlType, $elem.Current.AutomationId)
    }
}