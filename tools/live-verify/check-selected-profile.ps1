Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$app = Get-Process -Name "RouterPlus" | Where-Object {$_.MainWindowTitle -like "9Router*"}
if ($app) {
    $ae = [System.Windows.Automation.AutomationElement]::FromHandle($app.MainWindowHandle)
    $profileList = $ae.FindFirst([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "ProfileList"))
    if ($profileList) {
        $items = $profileList.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
        for ($i=0; $i -lt $items.Count; $i++) {
            $item = $items[$i]
            $pattern = $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
            if ($pattern) {
                $isSelected = $pattern.Current.IsSelected
                if ($isSelected) {
                    $children = $item.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
                    for ($j=0; $j -lt $children.Count; $j++) {
                        $child = $children[$j]
                        $name = $child.Current.Name
                        if ($name -and $name -ne "") {
                            "SELECTED Item " + $i + " Child " + $j + ": Name=" + $name
                        }
                    }
                }
            }
        }
    } else {
        "ProfileList not found"
    }
} else {
    "App not found"
}