Add-Type -AssemblyName UIAutomationClient
$proc = Get-Process -Name "RouterPlus" | Select-Object -First 1
if ($proc) {
    $element = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
    $allElements = $element.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    
    # Find ProfileList
    $profileList = $null
    for ($i = 0; $i -lt $allElements.Count; $i++) {
        if ($allElements[$i].Current.AutomationId -eq "ProfileList") {
            $profileList = $allElements[$i]
            break
        }
    }
    
    if ($profileList) {
        Write-Output "Found ProfileList"
        $items = $profileList.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
        Write-Output "Items in list: $($items.Count)"
        for ($i = 0; $i -lt $items.Count; $i++) {
            $el = $items[$i]
            $name = $el.Current.Name
            $autoId = $el.Current.AutomationId
            $ctrlType = $el.Current.ControlType.ProgrammaticName
            if ($name -like "*hieuck*" -or $name -like "*it*") {
                Write-Output ("  [{0}] Name='{1}', AutoId='{2}', Type={3}" -f $i, $name, $autoId, $ctrlType)
            }
        }
    }
    
    # Also search all elements for hieuck.it
    Write-Output "`n=== All hieuck.it matches ==="
    for ($i = 0; $i -lt $allElements.Count; $i++) {
        $el = $allElements[$i]
        $name = $el.Current.Name
        if ($name -like "*hieuck.it*") {
            Write-Output ("[{0}] Name='{1}', AutoId='{2}', Type={3}" -f $i, $name, $el.Current.AutomationId, $el.Current.ControlType.ProgrammaticName)
        }
    }
} else {
    Write-Output "Process not found"
}