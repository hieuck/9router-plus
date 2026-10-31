Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Get-Process -Name 'RouterPlus' | Select-Object -First 1
$hwnd = $proc.MainWindowHandle

$automation = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)

# Find the Agnes ProviderCardViewModel - the one with "Agnes AI" text as child
$agnesCard = $null
foreach ($elem in $all) {
    if ($elem.Current.ClassName -eq "RouterPlus.App.ViewModels.ProviderCardViewModel" -and $elem.Current.ControlType -eq [System.Windows.Automation.ControlType]::DataItem) {
        $children = $elem.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
        $hasAgnes = $false
        foreach ($child in $children) {
            if ($child.Current.Name -eq "Agnes AI") {
                $hasAgnes = $true
                break
            }
        }
        if ($hasAgnes) {
            $agnesCard = $elem
            Write-Host "Found Agnes card: Name='{0}' Class='{1}' Type='{2}' Children={3}" -f $elem.Current.Name, $elem.Current.ClassName, $elem.Current.ControlType, $children.Count
            break
        }
    }
}

if ($agnesCard) {
    Write-Host "=== Children of Agnes card ==="
    $children = $agnesCard.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    for ($i = 0; $i -lt $children.Count; $i++) {
        $child = $children[$i]
        $name = $child.Current.Name
        $autoId = $child.Current.AutomationId
        $controlType = $child.Current.ControlType
        $isEnabled = $child.Current.IsEnabled
        $className = $child.Current.ClassName
        
        if ($name -or $autoId -or $className) {
            Write-Host ("  [{0}] Name='{1}' AutoId='{2}' Type='{3}' Enabled={4} Class='{5}'" -f $i, $name, $autoId, $controlType, $isEnabled, $className)
        }
    }
} else {
    Write-Host "Agnes card not found"
}

Write-Host "Done"