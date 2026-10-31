Add-Type -AssemblyName UIAutomationClient
$proc = Get-Process -Name "RouterPlus" | Select-Object -First 1
if ($proc) {
    $element = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
    
    # Find the Agnes card - look for elements containing "Agnes"
    $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::NameProperty, "Agnes*")
    $agnesElements = $element.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
    Write-Output "Agnes name matches: $($agnesElements.Count)"
    for ($i = 0; $i -lt $agnesElements.Count; $i++) {
        $el = $agnesElements[$i]
        Write-Output ("  [{0}] Name='{1}', AutomationId='{2}', ControlType={3}, IsEnabled={4}" -f $i, $el.Current.Name, $el.Current.AutomationId, $el.Current.ControlType.ProgrammaticName, $el.Current.IsEnabled)
    }
    
    # Also find by AutomationId if any
    $allElements = $element.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    Write-Output "Total elements: $($allElements.Count)"
    for ($i = 0; $i -lt $allElements.Count; $i++) {
        $el = $allElements[$i]
        $name = $el.Current.Name
        $autoId = $el.Current.AutomationId
        if ($name -like "*Agnes*" -or $autoId -like "*Agnes*" -or $autoId -like "*agnes*") {
            Write-Output ("  MATCH [{0}] Name='{1}', AutomationId='{2}', ControlType={3}, IsEnabled={4}" -f $i, $name, $autoId, $el.Current.ControlType.ProgrammaticName, $el.Current.IsEnabled)
        }
    }
} else {
    Write-Output "Process not found"
}