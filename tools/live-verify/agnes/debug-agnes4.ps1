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

# Find Agnes text element
$cond = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::NameProperty, "Agnes AI")
$agnesText = $automation.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
if ($agnesText) {
    Write-Host ("Found Agnes text: '{0}'" -f $agnesText.Current.Name)
    $providerCard = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($agnesText)
    if ($providerCard) {
        Write-Host ("  ProviderCard: Name='{0}' Type='{1}'" -f $providerCard.Current.Name, $providerCard.Current.ControlType.ProgrammaticName)
        
        # Now find the "Tu dong them" button within this provider card
        $btnCond = [System.Windows.Automation.AndCondition]::new(
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button),
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::NameProperty, "Tu dong them")
        )
        $autoAddBtn = $providerCard.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
        if ($autoAddBtn) {
            Write-Host ("  Found 'Tu dong them' button: Name='{0}' Enabled={1} Visible={2}" -f $autoAddBtn.Current.Name, $autoAddBtn.Current.IsEnabled, (-not $autoAddBtn.Current.IsOffscreen))
            $patterns = $autoAddBtn.GetSupportedPatterns()
            Write-Host ("  Button Patterns: {0}" -f $patterns.Count)
            foreach ($p in $patterns) {
                Write-Host ("    - {0}" -f $p.ProgrammaticName)
            }
        } else {
            Write-Host "  'Tu dong them' button NOT found in this provider card"
        }
    }
}