Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Get-Process -Name 'RouterPlus' | Select-Object -First 1
$hwnd = $proc.MainWindowHandle

$automation = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

$listCond = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "ProfileList")
$profileList = $automation.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $listCond)

if ($profileList) {
    $children = $profileList.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
    Write-Host "Direct children count: " $children.Count
    for ($i = 0; $i -lt $children.Count; $i++) {
        $el = $children[$i]
        Write-Host ("[" + $i + "] Name=" + $el.Current.Name + " Class=" + $el.Current.ClassName + " Type=" + $el.Current.ControlType.ProgrammaticName + " Bounds=" + $el.Current.BoundingRectangle + " Enabled=" + $el.Current.IsEnabled + " Offscreen=" + $el.Current.IsOffscreen)
        $grandChildren = $el.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
        for ($j = 0; $j -lt $grandChildren.Count; $j++) {
            $gc = $grandChildren[$j]
            Write-Host ("   [" + $j + "] Name=" + $gc.Current.Name + " Class=" + $gc.Current.ClassName + " Type=" + $gc.Current.ControlType.ProgrammaticName + " Bounds=" + $gc.Current.BoundingRectangle)
        }
    }
}