Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Get-Process -Name 'RouterPlus' | Select-Object -First 1
$hwnd = $proc.MainWindowHandle

$automation = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

# First, make sure we have hieuck.it@gmail.com selected
$selectedCond = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "SelectedProfileName")
$selectedProfile = $automation.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $selectedCond)

if ($selectedProfile) {
    Write-Host "SelectedProfileName: '" + $selectedProfile.Current.Name + "'"
} else {
    Write-Host "SelectedProfileName NOT found"
}

# Find all buttons with "Tự động thêm"
$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
Write-Host "Total elements: $($all.Count)"

$agnesButtons = @()
for ($i = 0; $i -lt $all.Count; $i++) {
    $el = $all[$i]
    $name = $el.Current.Name
    $autoId = $el.Current.AutomationId
    $className = $el.Current.ClassName
    $controlType = $el.Current.ControlType.ProgrammaticName
    $isEnabled = $el.Current.IsEnabled
    $bounds = $el.Current.BoundingRectangle
    
    if ($name -eq "Tự động thêm" -and $controlType -eq "ControlType.Button") {
        # Check if this button is in an Agnes card (parent has "Agnes AI" or similar)
        $parent = $el.GetCurrentPropertyValue([System.Windows.Automation.AutomationElementIdentifiers]::ParentProperty)
        $parentName = if ($parent) { $parent.Current.Name } else { "" }
        $grandParent = if ($parent) { $parent.GetCurrentPropertyValue([System.Windows.Automation.AutomationElementIdentifiers]::ParentProperty) } else { $null }
        $grandParentName = if ($grandParent) { $grandParent.Current.Name } else { "" }
        
        $agnesButtons += @{
            Index = $i
            Element = $el
            IsEnabled = $isEnabled
            Bounds = $bounds
            ParentName = $parentName
            GrandParentName = $grandParentName
        }
        Write-Host ("Tự động thêm button #$($agnesButtons.Count): Index=$i, Enabled=$isEnabled, Bounds=$bounds, Parent='$parentName', GrandParent='$grandParentName'")
    }
}

# Find the one that's in the Agnes AI card for hieuck.it@gmail.com
# Look for the Agnes card that has "Agnes AI" text
$agnesCardButtons = $agnesButtons | Where-Object { $_.GrandParentName -like "*Agnes AI*" -or $_.ParentName -like "*Agnes AI*" }
Write-Host "Agnes card 'Tự động thêm' buttons: $($agnesCardButtons.Count)"

if ($agnesCardButtons.Count -gt 0) {
    $btn = $agnesCardButtons[0].Element
    Write-Host "Clicking the first Agnes 'Tự động thêm' button..."
    $invokePattern = $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    if ($invokePattern) {
        $invokePattern.Invoke()
        Write-Host "Invoked the button successfully"
    } else {
        Write-Host "No InvokePattern available"
    }
} else {
    Write-Host "No Agnes 'Tự động thêm' button found"
}