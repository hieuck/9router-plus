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

# Find the Agnes card - it's the last one with "Kết nối OAuth" button
$agnesCard = $null
$tuDongThemBtn = $null

for ($i = 0; $i -lt $elements.Count; $i++) {
    $el = $elements[$i]
    $children = $el.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    $hasKetNoiOAuth = $false
    foreach ($child in $children) {
        if ($child.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and $child.Current.Name -eq "Kết nối OAuth") {
            $hasKetNoiOAuth = $true
            break
        }
    }
    if ($hasKetNoiOAuth) {
        Write-Host ("Found Agnes card at index {0}" -f $i)
        $agnesCard = $el
        # Find the "Tự động thêm" button in this card
        foreach ($child in $children) {
            if ($child.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and $child.Current.Name -eq "Tự động thêm") {
                $tuDongThemBtn = $child
                break
            }
        }
        break
    }
}

if ($tuDongThemBtn) {
    Write-Host "Found 'Tự động thêm' button in Agnes card"
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