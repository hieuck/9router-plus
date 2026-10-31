Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName System.Windows.Forms

Write-Host "Starting test..."

$process = Get-Process -Name RouterPlus | Select-Object -First 1
if (-not $process) { Write-Host 'RouterPlus process not found'; exit }

Write-Host "Found process: $($process.Id)"

$root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
Write-Host "Got root element"

# Find ProviderCardViewModel elements
$condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::NameProperty, 'RouterPlus.App.ViewModels.ProviderCardViewModel')
$elements = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)

Write-Host ("Found $($elements.Count) ProviderCardViewModel elements")

# Card 0 is the Agnes card (has "Agnes AI" text and "Tự động thêm" button)
$agnesCard = $elements[0]
$children = $agnesCard.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)

$targetBytes = [System.Text.Encoding]::UTF8.GetBytes("Tự động thêm")

$tuDongThemBtn = $null
foreach ($child in $children) {
    if ($child.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button) {
        $name = $child.Current.Name
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($name)
        if ($bytes.Length -eq $targetBytes.Length) {
            $match = $true
            for ($i=0; $i -lt $bytes.Length; $i++) {
                if ($bytes[$i] -ne $targetBytes[$i]) { $match = $false; break }
            }
            if ($match) {
                $tuDongThemBtn = $child
                break
            }
        }
    }
}

if ($tuDongThemBtn) {
    Write-Host "Found 'Tự động thêm' button in Agnes card (Card 0)"
    Write-Host ("  IsEnabled: {0}" -f $tuDongThemBtn.Current.IsEnabled)
    
    # Invoke the button
    $invokePattern = $tuDongThemBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    if ($invokePattern) {
        $invokePattern.Invoke()
        Write-Host "Invoked 'Tự động thêm' button successfully!"
    } else {
        Write-Host "InvokePattern not available on button"
    }
} else {
    Write-Host "Could not find 'Tự động thêm' button in Agnes card"
}