Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Get-Process -Name 'RouterPlus' | Select-Object -First 1
$hwnd = $proc.MainWindowHandle

$automation = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

# Find Credentials Manager window
$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
for ($i = 0; $i -lt $all.Count; $i++) {
    $el = $all[$i]
    $name = $el.Current.Name
    $autoId = $el.Current.AutomationId
    $controlType = $el.Current.ControlType.ProgrammaticName
    $isEnabled = $el.Current.IsEnabled
    
    if ($name -eq "Save" -and $autoId -eq "" -and $controlType -eq "ControlType.Button" -and $isEnabled -eq $true) {
        # Check if this is the Save button in the Google row
        $parent = $el.GetCurrentPropertyValue([System.Windows.Automation.AutomationElementIdentifiers]::ParentProperty)
        $path = @()
        $current = $parent
        for ($level = 0; $level -lt 5 -and $current; $level++) {
            $path += $current.Current.Name
            $current = $current.GetCurrentPropertyValue([System.Windows.Automation.AutomationElementIdentifiers]::ParentProperty)
        }
        Write-Host "Found Save button at index $i, Ancestry: " + ($path -join " > ")
        
        # Try to click it
        $pattern = $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        if ($pattern) {
            Write-Host "Invoking Save button..."
            $pattern.Invoke()
            Write-Host "Done."
        } else {
            Write-Host "No InvokePattern available"
        }
    }
}