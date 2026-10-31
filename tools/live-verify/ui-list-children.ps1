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
    # Get direct children only
    $children = $profileList.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
    Write-Host "Direct children count: " + $children.Count
    
    for ($i = 0; $i -lt $children.Count; $i++) {
        $el = $children[$i]
        $name = $el.Current.Name
        $autoId = $el.Current.AutomationId
        $className = $el.Current.ClassName
        $controlType = $el.Current.ControlType.ProgrammaticName
        $bounds = $el.Current.BoundingRectangle
        $isEnabled = $el.Current.IsEnabled
        $isOffscreen = $el.Current.IsOffscreen
        $patterns = $el.GetSupportedPatterns()
        $patNames = @()
        foreach ($p in $patterns) {
            $patNames += $p.ProgrammaticName
        }
        Write-Host ("[$i] Name='$name' AutoId='$autoId' Class='$className' Type='$controlType' Bounds='$bounds' Enabled=$isEnabled Offscreen=$isOffscreen Patterns=$($patNames -join ',')")
        
        # Get children of this child
        $grandChildren = $el.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
        for ($j = 0; $j -lt $grandChildren.Count; $j++) {
            $gc = $grandChildren[$j]
            $gname = $gc.Current.Name
            $gautoId = $gc.Current.AutomationId
            $gclassName = $gc.Current.ClassName
            $gcontrolType = $gc.Current.ControlType.ProgrammaticName
            $gbounds = $gc.Current.BoundingRectangle
            Write-Host ("   [$j] Name='$gname' AutoId='$gautoId' Class='$gclassName' Type='$gcontrolType' Bounds='$gbounds'")
        }
    }
}