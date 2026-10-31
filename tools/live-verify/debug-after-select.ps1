Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName System.Windows.Forms

$proc = Get-Process -Name "RouterPlus" | Select-Object -First 1
$automation = [System.Windows.Automation.AutomationElement]::FromHandle(47258290)

# Set search text
$searchBox = $automation.FindFirst([System.Windows.Automation.TreeScope]::Descendants, 
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "ProfileSearchTextBox"))
$textPattern = $searchBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
$textPattern.SetValue("hieuck.adobe2022@gmail.com")
Start-Sleep -Seconds 3

# Select profile
$profileList = $automation.FindFirst([System.Windows.Automation.TreeScope]::Descendants, 
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "ProfileList"))
$children = $profileList.FindAll([System.Windows.Automation.TreeScope]::Children, 
    [System.Windows.Automation.Condition]::TrueCondition)
$listItem = $children[0]
$selectionItemPattern = $listItem.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
$selectionItemPattern.Select()
Start-Sleep -Seconds 3

# Now dump all elements
Write-Host "=== AFTER PROFILE SELECTION ==="
$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
Write-Host "Total elements: $($all.Count)"

for ($i = 0; $i -lt $all.Count; $i++) {
    $el = $all[$i]
    $name = $el.Current.Name
    $autoId = $el.Current.AutomationId
    $type = $el.Current.ControlType.ProgrammaticName
    if ($name -and ($name -like "*Agnes*" -or $name -like "*agnes*" -or $name -like "*AG*" -or $type -eq "ControlType.DataItem")) {
        Write-Host "  [$i] Name='$name' AutoId='$autoId' Type='$type'"
    }
}