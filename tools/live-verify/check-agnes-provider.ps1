Add-Type -AssemblyName UIAutomationClient
$proc = Get-Process -Name "RouterPlus" | Select-Object -First 1
if ($proc) {
    $element = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
    $allElements = $element.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    
    # Find all ProfileProviderStatusViewModel items
    for ($i = 0; $i -lt $allElements.Count; $i++) {
        $el = $allElements[$i]
        if ($el.Current.Name -eq "RouterPlus.App.ViewModels.ProfileProviderStatusViewModel" -and $el.Current.ControlType.ProgrammaticName -eq "ControlType.DataItem") {
            # Get children
            $children = $el.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
            for ($j = 0; $j -lt $children.Count; $j++) {
                $ch = $children[$j]
                $name = $ch.Current.Name
                if ($name -like "*Agnes*") {
                    Write-Output ("[{0}] Agnes Provider: {1}" -f $i, $name)
                }
            }
        }
    }
    
    # Also check the selected profile area
    Write-Output "`n=== Selected Profile Area ==="
    for ($i = 0; $i -lt $allElements.Count; $i++) {
        $el = $allElements[$i]
        if ($el.Current.AutomationId -eq "SelectedProfileName") {
            Write-Output ("Selected: {0}" -f $el.Current.Name)
        }
    }
} else {
    Write-Output "Process not found"
}