Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

$processes = Get-Process -Name "RouterPlus"
Write-Host "Found $($processes.Count) RouterPlus processes"
foreach ($p in $processes) {
    Write-Host "  PID: $($p.Id), MainWindowHandle: $($p.MainWindowHandle), MainWindowTitle: '$($p.MainWindowTitle)'"
}

foreach ($process in $processes) {
    if ($process.MainWindowHandle -eq [IntPtr]::Zero) { continue }
    
    $mainWindow = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    if (-not $mainWindow) { continue }
    
    Write-Host "=== Window PID $($process.Id) ==="
    Write-Host "Name: '$($mainWindow.Current.Name)'"
    Write-Host "ClassName: $($mainWindow.Current.ClassName)"
    Write-Host "ControlType: $($mainWindow.Current.ControlType)"
    
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
    
    Write-Host "=== Elements with 'Agnes' in Name ==="
    foreach ($elem in $allElements) {
        $name = $elem.Current.Name
        if ($name -and $name -like "*Agnes*") {
            Write-Host ("  Name='{0}', ControlType={1}, AutomationId='{2}'" -f $name, $elem.Current.ControlType, $elem.Current.AutomationId)
        }
    }
    
    Write-Host "=== Elements with 'Tự động' in Name ==="
    foreach ($elem in $allElements) {
        $name = $elem.Current.Name
        if ($name -and $name -like "*Tự động*") {
            Write-Host ("  Name='{0}', ControlType={1}, AutomationId='{2}'" -f $name, $elem.Current.ControlType, $elem.Current.AutomationId)
        }
    }
    
    Write-Host ""
}
Write-Host "Done"