Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName System.Windows.Forms

Write-Host "Starting test..."

$process = Get-Process -Name RouterPlus | Select-Object -First 1
if (-not $process) { Write-Host 'RouterPlus process not found'; exit }

Write-Host "Found process: $($process.Id)"

$root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
Write-Host "Got root element"

# Find the ProviderCardViewModel for Agnes (the one with "Tự động thêm" button)
$condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::NameProperty, 'RouterPlus.App.ViewModels.ProviderCardViewModel')
$elements = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)

Write-Host "Found $($elements.Count) ProviderCardViewModel elements"

$found = $false
foreach ($el in $elements) {
    # Check if this card has "Tự động thêm" button
    $children = $el.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    $hasTuDongThem = $false
    $tuDongThemBtn = $null
    foreach ($child in $children) {
        if ($child.Current.Name -eq "Tự động thêm") {
            $hasTuDongThem = $true
            $tuDongThemBtn = $child
            break
        }
    }
    
    if ($hasTuDongThem) {
        Write-Host "Found Agnes card with 'Tự động thêm' button"
        Write-Host ("  Button IsEnabled: {0}" -f $tuDongThemBtn.Current.IsEnabled)
        $found = $true
        
        # Invoke the button
        $invokePattern = $tuDongThemBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        if ($invokePattern) {
            $invokePattern.Invoke()
            Write-Host "Invoked 'Tự động thêm' button successfully!"
        } else {
            Write-Host "InvokePattern not available on button"
        }
        break
    }
}

if (-not $found) {
    Write-Host "No card with 'Tự động thêm' button found"
}