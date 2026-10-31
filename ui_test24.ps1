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

$buttonCount = 0
foreach ($child in $children) {
    if ($child.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button) {
        $buttonCount++
        Write-Host ("Button {0}: Name='{1}' IsEnabled={2}" -f $buttonCount, $child.Current.Name, $child.Current.IsEnabled)
        if ($buttonCount -eq 3) {  # The 3rd button should be "Tự động thêm" (after ↻ and ×)
            $tuDongThemBtn = $child
            Write-Host ("  -> This is the 3rd button, invoking...")
            $invokePattern = $tuDongThemBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
            if ($invokePattern) {
                $invokePattern.Invoke()
                Write-Host "Invoked button successfully!"
            } else {
                Write-Host "InvokePattern not available"
            }
        }
    }
}