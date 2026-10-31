Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName System.Windows.Forms

$process = Get-Process -Name RouterPlus | Select-Object -First 1
if (-not $process) { Write-Host 'RouterPlus process not found'; exit }

$root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)

# Find the ProviderCardViewModel for Agnes
$condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::NameProperty, 'RouterPlus.App.ViewModels.ProviderCardViewModel')
$elements = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
Write-Host ("ProviderCardViewModel elements: $($elements.Count)")
foreach ($el in $elements) {
    Write-Host ("  - Name='{0}' AutoId='{1}'" -f $el.Current.Name, $el.Current.AutomationId)
    $children = $el.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($child in $children) {
        if ($child.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -or $child.Current.Name -like "*Tự*" -or $child.Current.Name -like "*thêm*" -or $child.Current.Name -like "*Auto*" -or $child.Current.Name -like "*Add*") {
            Write-Host ("    Button/Control: {0} Name='{1}' IsEnabled={2} AutoId='{3}'" -f $child.Current.ControlType, $child.Current.Name, $child.Current.IsEnabled, $child.Current.AutomationId)
        }
    }
}

# Also check the specific "Agnes AI" text element parent
$textCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
$textElements = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textCondition)
foreach ($el in $textElements) {
    if ($el.Current.Name -eq "Agnes AI") {
        $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($el)
        Write-Host ("`nAgnes AI parent: {0} Name='{1}' AutoId='{2}'" -f $parent.Current.ControlType, $parent.Current.Name, $parent.Current.AutomationId)
        $children = $parent.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
        foreach ($child in $children) {
            if ($child.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button) {
                Write-Host ("  Button: '{0}' IsEnabled={1} AutoId='{2}'" -f $child.Current.Name, $child.Current.IsEnabled, $child.Current.AutomationId)
            }
        }
    }
}