Add-Type -AssemblyName UIAutomationClient
$proc = Get-Process -Name "RouterPlus" | Select-Object -First 1
if ($proc) {
    $element = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
    $allElements = $element.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    
    # Find the ProviderCardViewModel for the selected profile
    for ($i = 0; $i -lt $allElements.Count; $i++) {
        $el = $allElements[$i]
        if ($el.Current.Name -eq "RouterPlus.App.ViewModels.ProviderCardViewModel" -and $el.Current.ControlType.ProgrammaticName -eq "ControlType.DataItem") {
            $children = $el.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
            Write-Output "=== ProviderCardViewModel children ($($children.Count)) ==="
            for ($j = 0; $j -lt $children.Count; $j++) {
                $ch = $children[$j]
                Write-Output ("  [{0}] Name='{1}', AutoId='{2}', Type={3}, IsEnabled={4}" -f $j, $ch.Current.Name, $ch.Current.AutomationId, $ch.Current.ControlType.ProgrammaticName, $ch.Current.IsEnabled)
            }
            break
        }
    }
} else {
    Write-Output "Process not found"
}