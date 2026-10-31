Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

$process = Get-Process -Name "RouterPlus" | Select-Object -First 1
$mainWindow = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)

# First, try to find the profile textblock and click it
$allElements = $mainWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)

Write-Host "=== Elements with 'adobe2022' in Name ===" 
foreach ($elem in $allElements) {
    if ($elem.Current.Name -and $elem.Current.Name -like "*adobe2022*") {
        Write-Host "  Name='$($elem.Current.Name)', ControlType=$($elem.Current.ControlType), AutomationId='$($elem.Current.AutomationId)', IsEnabled=$($elem.Current.IsEnabled)"
        # Try to click it
        $clickablePoint = $elem.GetClickablePoint()
        if ($clickablePoint.X -ne 0 -or $clickablePoint.Y -ne 0) {
            Write-Host "  Clickable point: $($clickablePoint.X), $($clickablePoint.Y)"
        }
    }
}

# Let's also dump the whole tree under ProfileList to see what's there
$profileList = $mainWindow.FindFirst(
    [System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "ProfileList")
)

if ($profileList) {
    Write-Host "=== ProfileList FULL tree (Descendants) ==="
    $descendants = $profileList.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($elem in $descendants) {
        $name = $elem.Current.Name
        if ($name -and $name.Length -gt 0) {
            Write-Host "  Name='$($elem.Current.Name)', ControlType=$($elem.Current.ControlType), AutomationId='$($elem.Current.AutomationId)', IsEnabled=$($elem.Current.IsEnabled)"
        }
    }
}

# Also check the SearchTextBox value
$searchTextBox = $mainWindow.FindFirst(
    [System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "ProfileSearchTextBox")
)
if ($searchTextBox) {
    $valuePattern = $searchTextBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    if ($valuePattern) {
        Write-Host "SearchBox current value: '$($valuePattern.Current.Value)'"
    }
}