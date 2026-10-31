Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$app = Get-Process -Name "RouterPlus" | Where-Object {$_.MainWindowTitle -like "9Router*"}
if ($app) {
    $ae = [System.Windows.Automation.AutomationElement]::FromHandle($app.MainWindowHandle)
    $profileList = $ae.FindFirst([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "ProfileList"))
    if ($profileList) {
        $items = $profileList.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
        $item = $items[9]  # hieuck.adobe2022@gmail.com
        $pattern = $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
        if ($pattern) {
            $pattern.Select()
            "Selected Item 9 (hieuck.adobe2022@gmail.com)"
        } else {
            "No SelectionItemPattern"
        }
    } else {
        "ProfileList not found"
    }
} else {
    "App not found"
}