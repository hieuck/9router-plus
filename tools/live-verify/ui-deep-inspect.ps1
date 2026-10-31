Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Get-Process -Name 'RouterPlus' | Select-Object -First 1
$hwnd = $proc.MainWindowHandle

$automation = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

# Find the profile list
$listCond = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "ProfileList")
$profileList = $automation.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $listCond)

if ($profileList) {
    Write-Host "Found list"
    Write-Host "List bounds: " + $profileList.Current.BoundingRectangle
    Write-Host "List IsEnabled: " + $profileList.Current.IsEnabled
    Write-Host "List IsOffscreen: " + $profileList.Current.IsOffscreen
    
    # Get all descendants of the list
    $allDesc = $profileList.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    Write-Host "Total descendants: " + $allDesc.Count
    
    for ($i = 0; $i -lt $allDesc.Count; $i++) {
        $el = $allDesc[$i]
        $name = $el.Current.Name
        $autoId = $el.Current.AutomationId
        $className = $el.Current.ClassName
        $controlType = $el.Current.ControlType.ProgrammaticName
        $bounds = $el.Current.BoundingRectangle
        $isEnabled = $el.Current.IsEnabled
        $isOffscreen = $el.Current.IsOffscreen
        
        if ($name -like "*hieuck.it*" -or $name -like "*H*" -or $autoId) {
            Write-Host ("[$i] Name='$name' AutoId='$autoId' Class='$className' Type='$controlType' Bounds='$bounds' Enabled=$isEnabled Offscreen=$isOffscreen")
        }
    }
}