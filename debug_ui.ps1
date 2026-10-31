Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$process = Get-Process -Name "RouterPlus" | Select-Object -First 1
$mainWindow = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)

$allElements = $mainWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)

Write-Host "=== ALL elements with ControlType=ListItem ===" 
foreach ($elem in $allElements) {
    if ($elem.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem) {
        Write-Host "  Name='$($elem.Current.Name)', AutomationId='$($elem.Current.AutomationId)', IsEnabled=$($elem.Current.IsEnabled)"
    }
}

Write-Host "=== ALL elements with Name containing hieuck ===" 
foreach ($elem in $allElements) {
    if ($elem.Current.Name -and $elem.Current.Name -like "*hieuck*") {
        Write-Host "  Name='$($elem.Current.Name)', ControlType=$($elem.Current.ControlType), AutomationId='$($elem.Current.AutomationId)', IsEnabled=$($elem.Current.IsEnabled)"
    }
}

Write-Host "=== ALL elements with Name containing Profile ===" 
foreach ($elem in $allElements) {
    if ($elem.Current.Name -and $elem.Current.Name -like "*Profile*") {
        Write-Host "  Name='$($elem.Current.Name)', ControlType=$($elem.Current.ControlType), AutomationId='$($elem.Current.AutomationId)', IsEnabled=$($elem.Current.IsEnabled)"
    }
}

Write-Host "=== ALL elements with AutomationId=ProfileList ===" 
foreach ($elem in $allElements) {
    if ($elem.Current.AutomationId -eq "ProfileList") {
        $children = $elem.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
        Write-Host "  Found ProfileList: Name='$($elem.Current.Name)', ControlType=$($elem.Current.ControlType), Children=$($children.Count)"
    }
}

Write-Host "=== ALL elements with Name containing Agnes ===" 
foreach ($elem in $allElements) {
    if ($elem.Current.Name -and $elem.Current.Name -like "*Agnes*") {
        Write-Host "  Name='$($elem.Current.Name)', ControlType=$($elem.Current.ControlType), AutomationId='$($elem.Current.AutomationId)', IsEnabled=$($elem.Current.IsEnabled)"
    }
}

Write-Host "=== ALL buttons with Name containing 'Tự động' ===" 
foreach ($elem in $allElements) {
    if ($elem.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and $elem.Current.Name -and $elem.Current.Name -like "*Tự động*") {
        Write-Host "  Name='$($elem.Current.Name)', AutomationId='$($elem.Current.AutomationId)', IsEnabled=$($elem.Current.IsEnabled)"
        $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($elem)
        for ($i = 0; $i -lt 10 -and $parent; $i++) {
            Write-Host ("  Parent {0}: Name='{1}', ControlType={2}, AutomationId='{3}'" -f $i, $parent.Current.Name, $parent.Current.ControlType, $parent.Current.AutomationId)
            $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($parent)
        }
    }
}