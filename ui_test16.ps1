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

# Find the "Tự động thêm" button by byte comparison
$targetBytes = [System.Text.Encoding]::UTF8.GetBytes("Tự động thêm")
Write-Host "Target bytes: $([System.BitConverter]::ToString($targetBytes))"

foreach ($btn in $allButtons) {
    $name = $btn.Current.Name
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($name)
    if ($bytes.Length -eq $targetBytes.Length) {
        $match = $true
        for ($i=0; $i -lt $bytes.Length; $i++) {
            if ($bytes[$i] -ne $targetBytes[$i]) { $match = $false; break }
        }
        if ($match) {
            Write-Host "FOUND 'Tự động thêm' button!"
            Write-Host ("  IsEnabled: {0}" -f $btn.Current.IsEnabled)
            
            # Invoke the button
            $invokePattern = $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
            if ($invokePattern) {
                $invokePattern.Invoke()
                Write-Host "Invoked 'Tự động thêm' button successfully!"
            } else {
                Write-Host "InvokePattern not available on button"
            }
            break
        }
    }
}