Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Get-Process -Name 'RouterPlus' | Select-Object -First 1
$hwnd = $proc.MainWindowHandle

$automation = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
for ($i = 0; $i -lt $all.Count; $i++) {
    $el = $all[$i]
    $name = $el.Current.Name
    $autoId = $el.Current.AutomationId
    $controlType = $el.Current.ControlType.ProgrammaticName
    $isEnabled = $el.Current.IsEnabled
    
    if ($name -eq "Agnes (8)" -and $controlType -eq "ControlType.Button" -and $isEnabled -eq $true) {
        Write-Host "Found Agnes card button at index $i"
        
        # Try different patterns
        $patterns = @(
            [System.Windows.Automation.InvokePattern]::Pattern,
            [System.Windows.Automation.SelectionItemPattern]::Pattern,
            [System.Windows.Automation.TogglePattern]::Pattern,
            [System.Windows.Automation.ExpandCollapsePattern]::Pattern
        )
        
        foreach ($p in $patterns) {
            try {
                $pattern = $el.GetCurrentPattern($p)
                if ($pattern) {
                    Write-Host "Found pattern: $($p.ProgrammaticName)"
                    if ($p -eq [System.Windows.Automation.InvokePattern]::Pattern) { $pattern.Invoke() }
                    elseif ($p -eq [System.Windows.Automation.SelectionItemPattern]::Pattern) { $pattern.Select() }
                    elseif ($p -eq [System.Windows.Automation.TogglePattern]::Pattern) { $pattern.Toggle() }
                    elseif ($p -eq [System.Windows.Automation.ExpandCollapsePattern]::Pattern) { $pattern.Expand() }
                    Write-Host "Done."
                    break
                }
            } catch {
                Write-Host "Pattern $($p.ProgrammaticName) not available"
            }
        }
        
        # Also try getting clickable point
        try {
            $clickablePoint = $el.GetClickablePoint()
            Write-Host "Clickable point: X=$($clickablePoint.X), Y=$($clickablePoint.Y)"
        } catch {
            Write-Host "No clickable point"
        }
    }
}