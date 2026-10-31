Add-Type -AssemblyName UIAutomationClient

$proc = Get-Process -Name "RouterPlus" | Select-Object -First 1
if (-not $proc) { Write-Output "Process not found"; exit }

Write-Output "=== MONITORING STARTED ==="
Write-Output "Time: $(Get-Date -Format 'HH:mm:ss')"

$element = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
$allElements = $element.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)

# Find StatusText
for ($i = 0; $i -lt $allElements.Count; $i++) {
    if ($allElements[$i].Current.AutomationId -eq "StatusText") {
        Write-Output ("Initial StatusText: {0}" -f $allElements[$i].Current.Name)
        break
    }
}

# Also check for ProviderCardViewModel to see Agnes status
for ($i = 0; $i -lt $allElements.Count; $i++) {
    $el = $allElements[$i]
    if ($el.Current.Name -eq "RouterPlus.App.ViewModels.ProviderCardViewModel" -and $el.Current.ControlType.ProgrammaticName -eq "ControlType.DataItem") {
        $children = $el.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
        for ($j = 0; $j -lt $children.Count; $j++) {
            $ch = $children[$j]
            if ($ch.Current.Name -eq "Agnes AI" -or $ch.Current.Name -eq "Not added" -or $ch.Current.Name -eq "Online" -or $ch.Current.Name -eq "ApiKey") {
                Write-Output ("Initial Agnes status: {0}" -f $ch.Current.Name)
            }
        }
        break
    }
}

# Monitor for 240 seconds (4 minutes)
$startTime = Get-Date
$endTime = $startTime.AddSeconds(240)
$lastStatus = ""
$lastAgnesStatus = ""

while ((Get-Date) -lt $endTime) {
    $now = Get-Date -Format 'HH:mm:ss'
    
    # Refresh elements
    $element = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
    $allElements = $element.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    
    # Check StatusText
    for ($i = 0; $i -lt $allElements.Count; $i++) {
        if ($allElements[$i].Current.AutomationId -eq "StatusText") {
            $status = $allElements[$i].Current.Name
            if ($status -ne $lastStatus) {
                Write-Output ("[{0}] StatusText: {1}" -f $now, $status)
                $lastStatus = $status
            }
            break
        }
    }
    
    # Check Agnes status in ProviderCardViewModel
    for ($i = 0; $i -lt $allElements.Count; $i++) {
        $el = $allElements[$i]
        if ($el.Current.Name -eq "RouterPlus.App.ViewModels.ProviderCardViewModel" -and $el.Current.ControlType.ProgrammaticName -eq "ControlType.DataItem") {
            $children = $el.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
            for ($j = 0; $j -lt $children.Count; $j++) {
                $ch = $children[$j]
                if ($ch.Current.Name -eq "Agnes AI" -or $ch.Current.Name -eq "Not added" -or $ch.Current.Name -eq "Online" -or $ch.Current.Name -eq "ApiKey" -or $ch.Current.Name -eq "Offline") {
                    if ($ch.Current.Name -ne $lastAgnesStatus) {
                        Write-Output ("[{0}] Agnes status: {1}" -f $now, $ch.Current.Name)
                        $lastAgnesStatus = $ch.Current.Name
                    }
                }
            }
            break
        }
    }
    
    Start-Sleep -Seconds 2
}

Write-Output "=== MONITORING ENDED at $(Get-Date -Format 'HH:mm:ss') ==="