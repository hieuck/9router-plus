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

# Find Agnes card
$allElements = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, 
    [System.Windows.Automation.Condition]::TrueCondition)

$agnesCard = $null
for ($i = 0; $i -lt $allElements.Count; $i++) {
    $el = $allElements[$i]
    if ($el.Current.ControlType -eq [System.Windows.Automation.ControlType]::DataItem -and $el.Current.Name -eq "RouterPlus.App.ViewModels.ProviderCardViewModel") {
        $cardChildren = $el.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
        foreach ($child in $cardChildren) {
            if ($child.Current.Name -eq "Agnes AI") {
                $agnesCard = $el
                break
            }
        }
    }
    if ($agnesCard) { break }
}

# Find ALL buttons within Agnes card and print their exact byte representation
$btnCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
$buttons = $agnesCard.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCondition)
Write-Output "Buttons in Agnes card ($($buttons.Count)):"
for ($i = 0; $i -lt $buttons.Count; $i++) {
    $b = $buttons[$i]
    $name = $b.Current.Name
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($name)
    $hex = ($bytes | ForEach-Object { "{0:X2}" -f $_ }) -join " "
    Write-Output "  [$i] Name='$name' Hex=$hex Enabled=$($b.Current.IsEnabled)"
}