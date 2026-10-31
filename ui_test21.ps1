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

for ($i = 0; $i -lt $elements.Count; $i++) {
    $el = $elements[$i]
    $children = $el.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    Write-Host ("Card {0}:" -f $i)
    foreach ($child in $children) {
        if ($child.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button) {
            Write-Host ("  Button: '{0}' IsEnabled={1}" -f $child.Current.Name, $child.Current.IsEnabled)
        }
        if ($child.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text) {
            if ($child.Current.Name -like "*Agnes*" -or $child.Current.Name -like "*AI*") {
                Write-Host ("  Text: '{0}'" -f $child.Current.Name)
            }
        }
    }
}