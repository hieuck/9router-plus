Add-Type -AssemblyName UIAutomationClient
$proc = Get-Process -Name "RouterPlus" | Select-Object -First 1
if ($proc) {
    $element = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
    $condition = [System.Windows.Automation.ControlType]::Button
    $buttons = $element.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::ControlTypeProperty, $condition))
    Write-Output "Found $($buttons.Count) buttons"
    for ($i = 0; $i -lt $buttons.Count; $i++) {
        $btn = $buttons[$i]
        $name = $btn.Current.Name
        $autoId = $btn.Current.AutomationId
        $enabled = $btn.Current.IsEnabled
        Write-Output ("Button {0}: Name='{1}', AutomationId='{2}', IsEnabled={3}" -f $i, $name, $autoId, $enabled)
    }
} else {
    Write-Output "Process not found"
}