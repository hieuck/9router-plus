Add-Type -AssemblyName UIAutomationClient
$proc = Get-Process -Name "RouterPlus" | Select-Object -First 1
if ($proc) {
    $element = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
    $allElements = $element.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    
    # Find the Agnes button (index 15) and its tree
    $agnesBtn = $allElements[15]
    Write-Output "=== Agnes Button (15) ==="
    Write-Output ("Name='{0}', AutoId='{1}', ControlType={2}, IsEnabled={3}, ClassName='{4}'" -f $agnesBtn.Current.Name, $agnesBtn.Current.AutomationId, $agnesBtn.Current.ControlType.ProgrammaticName, $agnesBtn.Current.IsEnabled, $agnesBtn.Current.ClassName)
    
    # Get parent using TreeWalker
    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    $parent = $walker.GetParent($agnesBtn)
    if ($parent) {
        Write-Output "Parent: Name='{0}', AutoId='{1}', ControlType={2}" -f $parent.Current.Name, $parent.Current.AutomationId, $parent.Current.ControlType.ProgrammaticName
        $children = $parent.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
        Write-Output "Parent's children: $($children.Count)"
        for ($j = 0; $j -lt $children.Count; $j++) {
            $ch = $children[$j]
            Write-Output ("  Child {0}: Name='{1}', AutoId='{2}', ControlType={3}, IsEnabled={4}" -f $j, $ch.Current.Name, $ch.Current.AutomationId, $ch.Current.ControlType.ProgrammaticName, $ch.Current.IsEnabled)
        }
    }
    
    # Check the Text element (16) too
    $agnesText = $allElements[16]
    Write-Output "`n=== Agnes Text (16) ==="
    Write-Output ("Name='{0}', AutoId='{1}', ControlType={2}'" -f $agnesText.Current.Name, $agnesText.Current.AutomationId, $agnesText.Current.ControlType.ProgrammaticName)
    $parent2 = $walker.GetParent($agnesText)
    if ($parent2) {
        Write-Output "Parent: Name='{0}', AutoId='{1}', ControlType={2}" -f $parent2.Current.Name, $parent2.Current.AutomationId, $parent2.Current.ControlType.ProgrammaticName
        $children2 = $parent2.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
        Write-Output "Parent's children: $($children2.Count)"
        for ($j = 0; $j -lt $children2.Count; $j++) {
            $ch = $children2[$j]
            Write-Output ("  Child {0}: Name='{1}', AutoId='{2}', ControlType={3}, IsEnabled={4}" -f $j, $ch.Current.Name, $ch.Current.AutomationId, $ch.Current.ControlType.ProgrammaticName, $ch.Current.IsEnabled)
        }
    }
    
    # Check Agnes AI text (311)
    $agnesAI = $allElements[311]
    Write-Output "`n=== Agnes AI Text (311) ==="
    Write-Output ("Name='{0}', AutoId='{1}', ControlType={2}'" -f $agnesAI.Current.Name, $agnesAI.Current.AutomationId, $agnesAI.Current.ControlType.ProgrammaticName)
    $parent3 = $walker.GetParent($agnesAI)
    if ($parent3) {
        Write-Output "Parent: Name='{0}', AutoId='{1}', ControlType={2}" -f $parent3.Current.Name, $parent3.Current.AutomationId, $parent3.Current.ControlType.ProgrammaticName
        $children3 = $parent3.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
        Write-Output "Parent's children: $($children3.Count)"
        for ($j = 0; $j -lt $children3.Count; $j++) {
            $ch = $children3[$j]
            Write-Output ("  Child {0}: Name='{1}', AutoId='{2}', ControlType={3}, IsEnabled={4}" -f $j, $ch.Current.Name, $ch.Current.AutomationId, $ch.Current.ControlType.ProgrammaticName, $ch.Current.IsEnabled)
        }
    }
} else {
    Write-Output "Process not found"
}