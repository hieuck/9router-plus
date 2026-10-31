Add-Type -AssemblyName UIAutomationClient

$proc = Get-Process -Name "RouterPlus" | Select-Object -First 1
$automation = [System.Windows.Automation.AutomationElement]::FromHandle(47258290)

# Set search text and select profile
$searchBox = $automation.FindFirst([System.Windows.Automation.TreeScope]::Descendants, 
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "ProfileSearchTextBox"))
$textPattern = $searchBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
$textPattern.SetValue("hieuck.adobe2022@gmail.com")
Start-Sleep -Seconds 3

$profileList = $automation.FindFirst([System.Windows.Automation.TreeScope]::Descendants, 
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "ProfileList"))
$children = $profileList.FindAll([System.Windows.Automation.TreeScope]::Children, 
    [System.Windows.Automation.Condition]::TrueCondition)
$listItem = $children[0]
$selectionItemPattern = $listItem.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
$selectionItemPattern.Select()
Start-Sleep -Seconds 3

# Find Agnes card (index 87 from previous dump)
$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
$agnesCard = $all[87]  # ProviderCardViewModel for Agnes

Write-Host "Agnes card: Name='$($agnesCard.Current.Name)' Type='$($agnesCard.Current.ControlType.ProgrammaticName)'"

# Find all children of Agnes card
$children = $agnesCard.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
Write-Host "Agnes card children count: $($children.Count)"
for ($i = 0; $i -lt $children.Count; $i++) {
    $child = $children[$i]
    $name = $child.Current.Name
    $type = $child.Current.ControlType.ProgrammaticName
    $patterns = $child.GetSupportedPatterns()
    $patternNames = @()
    foreach ($p in $patterns) { $patternNames += $p.ProgrammaticName }
    Write-Host "  [$i] Name='$name' Type='$type' Patterns=$($patternNames -join ', ')"
}