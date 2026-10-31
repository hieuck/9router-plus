Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName System.Windows.Forms

Write-Host "Starting test..."

$process = Get-Process -Name RouterPlus | Select-Object -First 1
if (-not $process) { Write-Host 'RouterPlus process not found'; exit }

Write-Host "Found process: $($process.Id)"

$root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
Write-Host "Got root element"

# Find ALL buttons in the entire window
$btnCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
$allButtons = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCondition)

Write-Host "Found $($allButtons.Count) buttons total"

# Find the "Tự động thêm" button - just look at all button names and their bytes
foreach ($btn in $allButtons) {
    $name = $btn.Current.Name
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($name)
    if ($name -like "*Tự*" -or $name -like "*thêm*" -or $name -like "*Auto*" -or $name -like "*Add*") {
        Write-Host ("Button: '{0}' (bytes: {1}) IsEnabled={2}" -f $name, [System.BitConverter]::ToString($bytes), $btn.Current.IsEnabled)
    }
}