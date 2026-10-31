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
            
            # Find "Tự động thêm" button (index 22)
            $btn = $children[22]
            Write-Output ("Button: Name='{0}', AutoId='{1}', Type={2}, IsEnabled={3}" -f $btn.Current.Name, $btn.Current.AutomationId, $btn.Current.ControlType.ProgrammaticName, $btn.Current.IsEnabled)
            
            $invoke = $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
            if ($invoke) {
                $invoke.Invoke()
                Write-Output "Clicked 'Tự động thêm'!"
            } else {
                Write-Output "No InvokePattern available"
            }
            break
        }
    }
} else {
    Write-Output "Process not found"
}