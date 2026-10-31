Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

$process = Get-Process -Name "RouterPlus" | Select-Object -First 1
if (-not $process) {
    Write-Host "RouterPlus not found"
    exit 1
}

$mainWindow = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
if (-not $mainWindow) {
    Write-Host "Main window not found"
    exit 1
}

Write-Host "=== Main Window ==="
Write-Host "Name: $($mainWindow.Current.Name)"
Write-Host "ClassName: $($mainWindow.Current.ClassName)"
Write-Host "ControlType: $($mainWindow.Current.ControlType)"

# Dump all elements with AutomationId
$allElements = $mainWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)

Write-Host "=== Elements with AutomationId ==="
foreach ($elem in $allElements) {
    $aid = $elem.Current.AutomationId
    if ($aid -and $aid.Length -gt 0) {
        Write-Host ("  AutomationId='{0}', Name='{1}', ControlType={2}'" -f $aid, $elem.Current.Name, $elem.Current.ControlType)
    }
}

Write-Host "=== Elements with 'Profile' in Name ==="
foreach ($elem in $allElements) {
    $name = $elem.Current.Name
    if ($name -and $name -like "*Profile*") {
        Write-Host ("  Name='{0}', ControlType={1}, AutomationId='{2}'" -f $name, $elem.Current.ControlType, $elem.Current.AutomationId)
    }
}

Write-Host "=== Elements with 'Search' in Name or AutomationId ==="
foreach ($elem in $allElements) {
    $name = $elem.Current.Name
    $aid = $elem.Current.AutomationId
    if (($name -and $name -like "*Search*") -or ($aid -and $aid -like "*Search*")) {
        Write-Host ("  Name='{0}', ControlType={1}, AutomationId='{2}'" -f $name, $elem.Current.ControlType, $elem.Current.AutomationId)
    }
}

Write-Host "=== Elements with 'hieuck' or 'adobe' ==="
foreach ($elem in $allElements) {
    $name = $elem.Current.Name
    if ($name -and ($name -like "*hieuck*" -or $name -like "*adobe*")) {
        Write-Host ("  Name='{0}', ControlType={1}, AutomationId='{2}'" -f $name, $elem.Current.ControlType, $elem.Current.AutomationId)
    }
}

Write-Host "Done"