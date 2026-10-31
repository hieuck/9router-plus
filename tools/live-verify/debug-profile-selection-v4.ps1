Add-Type -AssemblyName UIAutomationClient

$proc = Get-Process -Name "RouterPlus" | Select-Object -First 1
if ($proc) {
    $element = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
    
    # Find the profile list
    $profileList = $element.FindFirst([System.Windows.Automation.TreeScope]::Descendants, 
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "ProfileList"))
    
    # Get the ListItem (there's only one visible)
    $children = $profileList.FindAll([System.Windows.Automation.TreeScope]::Children, 
        [System.Windows.Automation.Condition]::TrueCondition)
    
    if ($children.Count -gt 0) {
        $listItem = $children[0]
        Write-Output "ListItem: $($listItem.Current.Name) - $($listItem.Current.ControlType.ProgrammaticName)"
        
        # Check its children
        $listItemChildren = $listItem.FindAll([System.Windows.Automation.TreeScope]::Children, 
            [System.Windows.Automation.Condition]::TrueCondition)
        Write-Output "ListItem children count: $($listItemChildren.Count)"
        for ($i = 0; $i -lt $listItemChildren.Count; $i++) {
            $child = $listItemChildren[$i]
            Write-Output "  [$i] Name: '$($child.Current.Name)', ControlType: $($child.Current.ControlType.ProgrammaticName), AutoId: $($child.Current.AutomationId)"
            
            # Check grandchildren
            $grandChildren = $child.FindAll([System.Windows.Automation.TreeScope]::Children, 
                [System.Windows.Automation.Condition]::TrueCondition)
            for ($j = 0; $j -lt $grandChildren.Count; $j++) {
                $gc = $grandChildren[$j]
                Write-Output "    [$j] Name: '$($gc.Current.Name)', ControlType: $($gc.Current.ControlType.ProgrammaticName), AutoId: $($gc.Current.AutomationId)"
                
                # Check great-grandchildren
                $ggChildren = $gc.FindAll([System.Windows.Automation.TreeScope]::Children, 
                    [System.Windows.Automation.Condition]::TrueCondition)
                for ($k = 0; $k -lt $ggChildren.Count; $k++) {
                    $ggc = $ggChildren[$k]
                    Write-Output "      [$k] Name: '$($ggc.Current.Name)', ControlType: $($ggc.Current.ControlType.ProgrammaticName), AutoId: $($ggc.Current.AutomationId)"
                }
            }
        }
        
        # Check if the ListItem supports SelectionItemPattern
        $selectionItemPattern = $listItem.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
        if ($selectionItemPattern) {
            Write-Output ""
            Write-Output "ListItem supports SelectionItemPattern"
            Write-Output "  IsSelected: $($selectionItemPattern.Current.IsSelected)"
            Write-Output "  SelectionContainer: $($selectionItemPattern.Current.SelectionContainer.Current.Name)"
            $selectionItemPattern.Select()
            Write-Output "  Called Select()"
        } else {
            Write-Output "ListItem does NOT support SelectionItemPattern"
        }
        
        # Try to find the profile name within the ListItem
        $textElements = $listItem.FindAll([System.Windows.Automation.TreeScope]::Descendants, 
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text))
        Write-Output ""
        Write-Output "Text elements in ListItem:"
        for ($i = 0; $i -lt $textElements.Count; $i++) {
            $textEl = $textElements[$i]
            Write-Output "  [$i] Text: '$($textEl.Current.Name)'"
        }
    }
    
    # Now check the ProfileList's SelectionPattern again
    $selectionPattern = $profileList.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern)
    if ($selectionPattern) {
        $selectedItems = $selectionPattern.Current.GetSelection()
        Write-Output ""
        Write-Output "After potential selection, selection count: $($selectedItems.Count)"
        foreach ($item in $selectedItems) {
            Write-Output "  Selected: $($item.Current.Name)"
        }
    }
} else {
    Write-Output "RouterPlus process not found!"
}