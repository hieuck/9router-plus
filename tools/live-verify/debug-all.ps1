Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Get-Process -Name 'RouterPlus' | Select-Object -First 1
$hwnd = $proc.MainWindowHandle
$automation = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

Write-Host "Main window: Name='$($automation.Current.Name)' Class='$($automation.Current.ClassName)'"

# Find ALL elements in the tree
$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
Write-Host "Total elements: $($all.Count)"

for ($i = 0; $i -lt $all.Count; $i++) {
    $el = $all[$i]
    $name = $el.Current.Name
    $autoId = $el.Current.AutomationId
    $type = $el.Current.ControlType.ProgrammaticName
    if ($name -or $autoId) {
        Write-Host "  [$i] Name='$name' AutoId='$autoId' Type='$type'"
    }
}