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
    
    if ($name -like "*Credentials Manager*" -or $autoId -eq "CredentialsManagerCloseButton") {
        Write-Host "=== Credentials Manager Window Found at index $i ==="
        # Get all children of this window
        $children = $el.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
        Write-Host "Children count: " $children.Count
        for ($j = 0; $j -lt $children.Count; $j++) {
            $child = $children[$j]
            $cname = $child.Current.Name
            $cautoId = $child.Current.AutomationId
            $ccontrolType = $child.Current.ControlType.ProgrammaticName
            $cisEnabled = $child.Current.IsEnabled
            
            if ($cname -or $cautoId) {
                Write-Host "  [$j] Name='$cname' AutoId='$cautoId' Type='$ccontrolType' Enabled=$cisEnabled"
            }
        }
    }
}