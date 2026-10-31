Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Get-Process -Name 'RouterPlus' | Select-Object -First 1
$hwnd = $proc.MainWindowHandle

$automation = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

# Find the search box
$searchCond = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "ProfileSearchTextBox")
$searchBox = $automation.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $searchCond)

if ($searchBox) {
    Write-Host "Found search box"
    $valuePattern = $searchBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    if ($valuePattern) {
        $valuePattern.SetValue("hieuck.it@gmail.com")
        Write-Host "Set search text to hieuck.it@gmail.com"
        Start-Sleep -Seconds 2
    }
} else {
    Write-Host "Search box NOT found"
}

# Now find the profile list and see what's there
$listCond = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "ProfileList")
$profileList = $automation.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $listCond)

if ($profileList) {
    Write-Host "Found profile list"
    $items = $profileList.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
    Write-Host "Profile items count: $($items.Count)"
    for ($i = 0; $i -lt $items.Count; $i++) {
        $item = $items[$i]
        $name = $item.Current.Name
        Write-Host "  [$i] Name='$name'"
    }
} else {
    Write-Host "Profile list NOT found"
}