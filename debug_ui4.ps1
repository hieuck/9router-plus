Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

$process = Get-Process -Name "RouterPlus" | Select-Object -First 1
$mainWindow = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)

# Find the profile textblock and click it
$allElements = $mainWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)

Write-Host "=== ALL TextBlocks with 'Profile' in Name ===" 
foreach ($elem in $allElements) {
    if ($elem.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and $elem.Current.Name -and $elem.Current.Name -like "*Profile*") {
        Write-Host "  Name='$($elem.Current.Name)', AutomationId='$($elem.Current.AutomationId)', IsEnabled=$($elem.Current.IsEnabled)"
        $clickablePoint = $elem.GetClickablePoint()
        if ($clickablePoint.X -ne 0 -or $clickablePoint.Y -ne 0) {
            Write-Host "    Clickable point: $($clickablePoint.X), $($clickablePoint.Y)"
        }
    }
}

# Also look for anything clickable related to profiles
Write-Host "=== ALL elements with ControlType=Button AND 'Profile' in Name ===" 
foreach ($elem in $allElements) {
    if ($elem.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and $elem.Current.Name -and $elem.Current.Name -like "*Profile*") {
        Write-Host "  Name='$($elem.Current.Name)', AutomationId='$($elem.Current.AutomationId)', IsEnabled=$($elem.Current.IsEnabled)"
    }
}

# Look for anything in the left sidebar / profile list area
Write-Host "=== ALL elements with ControlType=List or ControlType=ListItem or ControlType=Tree or ControlType=TreeItem ===" 
foreach ($elem in $allElements) {
    $ct = $elem.Current.ControlType
    if ($ct -eq [System.Windows.Automation.ControlType]::List -or $ct -eq [System.Windows.Automation.ControlType]::ListItem -or $ct -eq [System.Windows.Automation.ControlType]::Tree -or $ct -eq [System.Windows.Automation.ControlType]::TreeItem) {
        Write-Host "  Name='$($elem.Current.Name)', ControlType=$ct, AutomationId='$($elem.Current.AutomationId)', IsEnabled=$($elem.Current.IsEnabled)"
    }
}

# Also dump ALL elements with non-empty names that might be profile items
Write-Host "=== ALL elements that look like profile items (have email in name) ===" 
foreach ($elem in $allElements) {
    if ($elem.Current.Name -and ($elem.Current.Name -like "*@*" -or $elem.Current.Name -like "*gmail*" -or $elem.Current.Name -like "*outlook*")) {
        Write-Host "  Name='$($elem.Current.Name)', ControlType=$($elem.Current.ControlType), AutomationId='$($elem.Current.AutomationId)', IsEnabled=$($elem.Current.IsEnabled)"
    }
}