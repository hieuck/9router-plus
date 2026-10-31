Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Get-Process -Name 'RouterPlus' | Select-Object -First 1
$hwnd = $proc.MainWindowHandle

$automation = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
Write-Host "Total elements: " $all.Count
for ($i = 0; $i -lt $all.Count; $i++) {
    $el = $all[$i]
    $name = $el.Current.Name
    $autoId = $el.Current.AutomationId
    $controlType = $el.Current.ControlType.ProgrammaticName
    $isEnabled = $el.Current.IsEnabled
    
    if ($name -eq "Tự động thêm") {
        Write-Host "=== Agnes Button Found at index $i ==="
        Write-Host "Name: $name"
        Write-Host "AutoId: $autoId"
        Write-Host "Type: $controlType"
        Write-Host "Enabled: $isEnabled"
        
        # Dump ancestry
        $parent = $el.GetCurrentPropertyValue([System.Windows.Automation.AutomationElementIdentifiers]::ParentProperty)
        $path = @()
        $current = $parent
        for ($level = 0; $level -lt 10 -and $current; $level++) {
            $pname = $current.Current.Name
            $pautoId = $current.Current.AutomationId
            $pcontrolType = $current.Current.ControlType.ProgrammaticName
            $path += "$pname ($pautoId) [$pcontrolType]"
            $current = $current.GetCurrentPropertyValue([System.Windows.Automation.AutomationElementIdentifiers]::ParentProperty)
        }
        Write-Host "Ancestry: " + ($path -join " > ")
    }
}