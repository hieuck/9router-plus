Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName System.Windows.Forms

Write-Host "Starting monitoring..."

$process = Get-Process -Name RouterPlus | Select-Object -First 1
if (-not $process) { Write-Host 'RouterPlus process not found'; exit }

Write-Host "Found process: $($process.Id)"

$root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
Write-Host "Got root element"

# Find the Activity log text element
$textCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
$textElements = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textCondition)
foreach ($el in $textElements) {
    if ($el.Current.Name -eq "Activity log" -or $el.Current.Name -eq "Sao chép log") {
        $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($el)
        Write-Host ("Found '{0}' parent: {1} Name='{2}'" -f $el.Current.Name, $parent.Current.ControlType, $parent.Current.Name)
        $grandParent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($parent)
        if ($grandParent) {
            $children = $grandParent.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
            foreach ($child in $children) {
                if ($child.Current.ControlType -eq [System.Windows.Automation.ControlType]::Document -or $child.Current.ControlType -eq [System.Windows.Automation.ControlType]::Edit -or $child.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text) {
                    $textPattern = $child.GetCurrentPattern([System.Windows.Automation.TextPattern]::Pattern)
                    if ($textPattern) {
                        $text = $textPattern.DocumentRange.GetText(-1)
                        Write-Host ("  Log content ({0} chars): {1}" -f $text.Length, $text.Substring(0, [Math]::Min(500, $text.Length)))
                    }
                }
            }
        }
    }
}