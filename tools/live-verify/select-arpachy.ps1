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
        $items = $profileList.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
        
        # Find arpachy85@gmail.com item (has Agnes = Not added)
        for ($i = 0; $i -lt $items.Count; $i++) {
            $el = $items[$i]
            if ($el.Current.Name -eq "arpachy85@gmail.com" -and $el.Current.ControlType.ProgrammaticName -eq "ControlType.Text") {
                Write-Output "Found arpachy85@gmail.com at index $i"
                $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($el)
                if ($parent -and $parent.Current.ControlType.ProgrammaticName -eq "ControlType.ListItem") {
                    Write-Output "Parent is ListItem, trying to click..."
                    $pattern = $parent.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
                    if ($pattern) {
                        $pattern.Select()
                        Write-Output "Selected arpachy85@gmail.com!"
                    }
                }
                break
            }
        }
    }
} else {
    Write-Output "Process not found"
}