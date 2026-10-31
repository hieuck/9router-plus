Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Get-Process -Name 'RouterPlus' | Select-Object -First 1
$hwnd = $proc.MainWindowHandle

$automation = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

# Find the profile list
$listCond = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "ProfileList")
$profileList = $automation.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $listCond)

if ($profileList) {
    $items = $profileList.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
    Write-Host "Profile items count: $($items.Count)"
    for ($i = 0; $i -lt $items.Count; $i++) {
        $item = $items[$i]
        $itemName = $item.Current.Name
        Write-Host ("Item " + $i + ": Name='" + $itemName + "' Class='" + $item.Current.ClassName + "' Type='" + $item.Current.ControlType.ProgrammaticName + "'")
        
        # Dump children
        $children = $item.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
        for ($j = 0; $j -lt $children.Count; $j++) {
            $child = $children[$j]
            $name = $child.Current.Name
            $autoId = $child.Current.AutomationId
            if ($name -or $autoId) {
                Write-Host ("  Child " + $j + ": Name='" + $name + "' AutoId='" + $autoId + "' Class='" + $child.Current.ClassName + "'")
            }
        }
        
        # Check if this is the right profile (contains hieuck.it)
        if ($itemName -like "*hieuck.it*") {
            Write-Host ">>> FOUND TARGET PROFILE at index " + $i
            $selPattern = $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
            if ($selPattern) {
                $selPattern.Select()
                Write-Host "Selected the profile"
            } else {
                Write-Host "No SelectionItemPattern"
            }
        }
    }
} else {
    Write-Host "Profile list NOT found"
}