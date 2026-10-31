Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Get-Process -Name 'RouterPlus' | Select-Object -First 1
$hwnd = $proc.MainWindowHandle

$automation = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

# Find all "Tự động thêm" buttons and their ancestry
$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
Write-Host "Total elements: $($all.Count)"

$agnesButtons = @()
for ($i = 0; $i -lt $all.Count; $i++) {
    $el = $all[$i]
    $name = $el.Current.Name
    $autoId = $el.Current.AutomationId
    $className = $el.Current.ClassName
    $controlType = $el.Current.ControlType.ProgrammaticName
    $isEnabled = $el.Current.IsEnabled
    $bounds = $el.Current.BoundingRectangle
    
    if ($name -eq "Tự động thêm" -and $controlType -eq "ControlType.Button") {
        # Walk up the tree to find Agnes context
        $parent = $el.GetCurrentPropertyValue([System.Windows.Automation.AutomationElementIdentifiers]::ParentProperty)
        $path = @()
        $current = $parent
        for ($level = 0; $level -lt 5 -and $current; $level++) {
            $path += $current.Current.Name
            $current = $current.GetCurrentPropertyValue([System.Windows.Automation.AutomationElementIdentifiers]::ParentProperty)
        }
        $agnesButtons += @{
            Index = $i
            Element = $el
            IsEnabled = $isEnabled
            Bounds = $bounds
            Ancestry = $path -join " > "
        }
        Write-Host ("Tự động thêm button #$($agnesButtons.Count): Index=$i, Enabled=$isEnabled, Bounds=$bounds")
        Write-Host "  Ancestry: $($path -join " > ")"
    }
}

Write-Host ""
Write-Host "Total 'Tự động thêm' buttons found: $($agnesButtons.Count)"