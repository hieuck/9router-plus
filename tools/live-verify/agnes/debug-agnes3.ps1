Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Get-Process -Name 'RouterPlus' | Select-Object -First 1
$hwnd = $proc.MainWindowHandle
$automation = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

# Search for profile
$searchBoxCond = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "ProfileSearchTextBox")
$searchBox = $automation.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $searchBoxCond)
if ($searchBox) {
    $valuePattern = $searchBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    if ($valuePattern) {
        $valuePattern.SetValue("hieuck.adobe2022@gmail.com")
    }
}
Start-Sleep -Seconds 2

$listCond = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "ProfileList")
$profileList = $automation.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $listCond)
if ($profileList) {
    $items = $profileList.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
    for ($i = 0; $i -lt $items.Count; $i++) {
        $item = $items[$i]
        $itemName = $item.Current.Name
        if ($itemName -eq "hieuck.adobe2022@gmail.com") {
            $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($item)
            if ($parent) {
                $selPattern = $parent.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
                if ($selPattern) { $selPattern.Select() }
            }
        }
    }
}
Start-Sleep -Seconds 2

# Find all buttons
$btnCond = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
$buttons = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
Write-Host ("All buttons: {0}" -f $buttons.Count)
for ($i = 0; $i -lt $buttons.Count; $i++) {
    $b = $buttons[$i]
    if ($b.Current.Name) {
        Write-Host ("  [{0}] Name='{1}' Enabled={2} Visible={3}" -f $i, $b.Current.Name, $b.Current.IsEnabled, (-not $b.Current.IsOffscreen))
        # Check parent
        $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($b)
        if ($parent) {
            Write-Host ("      Parent: Name='{0}' Type='{1}'" -f $parent.Current.Name, $parent.Current.ControlType.ProgrammaticName)
        }
    }
}

# Also find elements with "Agnes" in name
$cond = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::NameProperty, "Agnes*")
$agnesElements = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
Write-Host ("`nAgnes* elements: {0}" -f $agnesElements.Count)
for ($i = 0; $i -lt $agnesElements.Count; $i++) {
    $el = $agnesElements[$i]
    Write-Host ("  [{0}] Name='{1}' Type='{2}'" -f $i, $el.Current.Name, $el.Current.ControlType.ProgrammaticName)
    $patterns = $el.GetSupportedPatterns()
    foreach ($p in $patterns) {
        Write-Host ("      Pattern: {0}" -f $p.ProgrammaticName)
    }
}