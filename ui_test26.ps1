Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName System.Windows.Forms

Write-Host "Starting monitoring..."

$process = Get-Process -Name RouterPlus | Select-Object -First 1
if (-not $process) { Write-Host 'RouterPlus process not found'; exit }

Write-Host "Found process: $($process.Id)"

$root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
Write-Host "Got root element"

# Find StatusText
$statusCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::NameProperty, 'StatusText')
$statusElements = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $statusCondition)
Write-Host ("StatusText elements: $($statusElements.Count)")
foreach ($el in $statusElements) {
    Write-Host ("  Name='{0}' ControlType={1}" -f $el.Current.Name, $el.Current.ControlType)
}

# Find log text area
$logCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Document)
$logElements = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $logCondition)
Write-Host ("Document elements: $($logElements.Count)")
foreach ($el in $logElements) {
    $textPattern = $el.GetCurrentPattern([System.Windows.Automation.TextPattern]::Pattern)
    if ($textPattern) {
        Write-Host ("  Document: {0} chars" -f $textPattern.DocumentRange.GetText(-1).Length)
    }
}

# Find all Text elements
$textCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
$textElements = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textCondition)
Write-Host ("Text elements: $($textElements.Count)")
foreach ($el in $textElements) {
    if ($el.Current.Name -like "*Status*" -or $el.Current.Name -like "*status*" -or $el.Current.Name -like "*Log*" -or $el.Current.Name -like "*log*") {
        Write-Host ("  Text: '{0}'" -f $el.Current.Name)
    }
}