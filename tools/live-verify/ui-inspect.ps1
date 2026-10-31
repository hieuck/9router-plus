Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Get-Process -Name 'RouterPlus' | Select-Object -First 1
$hwnd = $proc.MainWindowHandle
Write-Host "HWND: $hwnd"

$automation = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
Write-Host "Root: $($automation.Current.Name)"

$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
Write-Host "Total elements: $($all.Count)"

# Dump all elements with automationId and name
for ($i = 0; $i -lt $all.Count; $i++) {
    $el = $all[$i]
    $name = $el.Current.Name
    $autoId = $el.Current.AutomationId
    $className = $el.Current.ClassName
    $controlType = $el.Current.ControlType.ProgrammaticName
    if ($name -or $autoId) {
        Write-Host "[$i] Name='$name' AutoId='$autoId' Class='$className' Type='$controlType'"
    }
}