Add-Type -AssemblyName UIAutomationClient
$proc = Get-Process -Name "RouterPlus" | Select-Object -First 1
if ($proc) {
    $element = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
    $allElements = $element.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    
    Write-Output "Total elements: $($allElements.Count)"
    for ($i = 0; $i -lt $allElements.Count; $i++) {
        $el = $allElements[$i]
        $name = $el.Current.Name
        $autoId = $el.Current.AutomationId
        $ctrlType = $el.Current.ControlType.ProgrammaticName
        if ($name -like "*hieuck*" -or $name -like "*gmail*" -or $name -like "*Profile*" -or $autoId -like "*Profile*" -or $ctrlType -eq "ControlType.ListItem" -or $ctrlType -eq "ControlType.ComboBox") {
            Write-Output ("[{0}] Name='{1}', AutoId='{2}', ControlType={3}, IsEnabled={4}" -f $i, $name, $autoId, $ctrlType, $el.Current.IsEnabled)
        }
    }
} else {
    Write-Output "Process not found"
}