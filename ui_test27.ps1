Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName System.Windows.Forms

Write-Host "Starting monitoring..."

$process = Get-Process -Name RouterPlus | Select-Object -First 1
if (-not $process) { Write-Host 'RouterPlus process not found'; exit }

Write-Host "Found process: $($process.Id)"

$root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
Write-Host "Got root element"

# Find all Text elements with their names
$textCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
$textElements = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textCondition)
Write-Host ("Text elements: $($textElements.Count)")
foreach ($el in $textElements) {
    $name = $el.Current.Name
    if ($name -ne "" -and $name.Length -gt 5) {
        Write-Host ("  Text: '{0}'" -f $name.Substring(0, [Math]::Min(100, $name.Length)))
    }
}