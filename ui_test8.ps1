Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName System.Windows.Forms

$process = Get-Process -Name RouterPlus | Select-Object -First 1
if (-not $process) { Write-Host 'RouterPlus process not found'; exit }

$root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)

# Look for the provider cards area - they might be in a different container
# Let's find all elements with "ProfileProvider" in name
$condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::NameProperty, "*ProfileProvider*")
$elements = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
Write-Host ("Elements with 'ProfileProvider' in name: $($elements.Count)")
foreach ($el in $elements) {
    Write-Host ("  - {0} Name='{1}' AutoId='{2}'" -f $el.Current.ControlType, $el.Current.Name, $el.Current.AutomationId)
}

# Look for Expander or Expander-like elements
$expanderCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Custom)
$customElements = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $expanderCondition)
Write-Host ("`nCustom elements: $($customElements.Count)")
foreach ($el in $customElements) {
    if ($el.Current.Name -like "*Profile*" -or $el.Current.Name -like "*Provider*" -or $el.Current.Name -like "*Card*") {
        Write-Host ("  - Name='{0}' AutoId='{1}'" -f $el.Current.Name, $el.Current.AutomationId)
        $children = $el.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
        foreach ($child in $children) {
            Write-Host ("    Child: {0} Name='{1}'" -f $child.Current.ControlType, $child.Current.Name)
        }
    }
}

# Find all Text elements with "Agnes"
$textCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
$textElements = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textCondition)
foreach ($el in $textElements) {
    if ($el.Current.Name -like "*Agnes*" -or $el.Current.Name -like "*Tự*" -or $el.Current.Name -like "*thêm*") {
        Write-Host ("Text: '{0}'" -f $el.Current.Name)
        $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($el)
        Write-Host ("  Parent: {0} Name='{1}'" -f $parent.Current.ControlType, $parent.Current.Name)
    }
}