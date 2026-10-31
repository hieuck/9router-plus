Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName System.Windows.Forms

$process = Get-Process -Name RouterPlus | Select-Object -First 1
if (-not $process) { Write-Host 'RouterPlus process not found'; exit }

$root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)

# Find the "Agnes (8)" button and explore its surroundings
$btnCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
$allButtons = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCondition)
foreach ($btn in $allButtons) {
    if ($btn.Current.Name -eq "Agnes (8)") {
        Write-Host ("Found 'Agnes (8)' button:")
        Write-Host ("  IsEnabled: {0}" -f $btn.Current.IsEnabled)
        # Get parent and siblings
        $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($btn)
        Write-Host ("  Parent: {0} Name='{1}'" -f $parent.Current.ControlType, $parent.Current.Name)
        
        # Get siblings of parent
        $grandParent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($parent)
        if ($grandParent) {
            $children = $grandParent.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
            Write-Host ("  GrandParent children ({0}):" -f $children.Count)
            foreach ($child in $children) {
                Write-Host ("    Child: {0} Name='{1}' AutoId='{2}'" -f $child.Current.ControlType, $child.Current.Name, $child.Current.AutomationId)
                # Recurse into this child
                $grandChildren = $child.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
                foreach ($gc in $grandChildren) {
                    if ($gc.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -or $gc.Current.Name -like "*Agnes*" -or $gc.Current.Name -like "*Tự*" -or $gc.Current.Name -like "*thêm*") {
                        Write-Host ("      Descendant: {0} Name='{1}' IsEnabled={2}" -f $gc.Current.ControlType, $gc.Current.Name, $gc.Current.IsEnabled)
                    }
                }
            }
        }
        break
    }
}

# Also search for any element containing "Tự động" or "thêm"
$nameCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::NameProperty, "Tự động thêm")
$elements = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $nameCondition)
Write-Host ("`nElements with Name='Tự động thêm': $($elements.Count)")
foreach ($el in $elements) {
    Write-Host ("  - {0} Name='{1}' IsEnabled={2} AutoId='{3}'" -f $el.Current.ControlType, $el.Current.Name, $el.Current.IsEnabled, $el.Current.AutomationId)
}