Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Get-Process -Name 'RouterPlus' | Select-Object -First 1
$hwnd = $proc.MainWindowHandle

$automation = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

# Check the Agnes "Tự động thêm" button state
$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
for ($i = 0; $i -lt $all.Count; $i++) {
    $el = $all[$i]
    $name = $el.Current.Name
    $autoId = $el.Current.AutomationId
    $controlType = $el.Current.ControlType.ProgrammaticName
    $isEnabled = $el.Current.IsEnabled
    
    if ($name -eq "Tự động thêm" -and $controlType -eq "ControlType.Button") {
        Write-Host ("Tự động thêm button at index " + $i + ": Enabled=" + $isEnabled)
        # Also dump ancestry
        $parent = $el.GetCurrentPropertyValue([System.Windows.Automation.AutomationElementIdentifiers]::ParentProperty)
        $path = @()
        $current = $parent
        for ($level = 0; $level -lt 5 -and $current; $level++) {
            $path += $current.Current.Name
            $current = $current.GetCurrentPropertyValue([System.Windows.Automation.AutomationElementIdentifiers]::ParentProperty)
        }
        Write-Host "  Ancestry: " + ($path -join " > ")
    }
}