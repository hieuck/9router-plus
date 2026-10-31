Add-Type -AssemblyName UIAutomationClient

$proc = Get-Process -Name "RouterPlus" | Select-Object -First 1
if ($proc) {
    $element = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
    
    # Step 1: Find ProfileSearchTextBox and set text
    $searchBox = $element.FindFirst([System.Windows.Automation.TreeScope]::Descendants, 
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "ProfileSearchTextBox"))
    
    $textPattern = $searchBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    $textPattern.SetValue("hieuck.adobe2022@gmail.com")
    
    Start-Sleep -Seconds 2
    
    # Step 2: Find the profile list
    $profileList = $element.FindFirst([System.Windows.Automation.TreeScope]::Descendants, 
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "ProfileList"))
    
    # Check if ListBox supports SelectionPattern
    $selectionPattern = $profileList.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern)
    if ($selectionPattern) {
        Write-Output "ProfileList supports SelectionPattern"
        Write-Output "  CanSelectMultiple: $($selectionPattern.Current.CanSelectMultiple)"
        Write-Output "  IsSelectionRequired: $($selectionPattern.Current.IsSelectionRequired)"
    } else {
        Write-Output "ProfileList does NOT support SelectionPattern"
    }
    
    # Check if ListBox supports SelectionPattern2
    $selectionPattern2 = $profileList.GetCurrentPattern([System.Windows.Automation.SelectionPattern2]::Pattern)
    if ($selectionPattern2) {
        Write-Output "ProfileList supports SelectionPattern2"
    } else {
        Write-Output "ProfileList does NOT support SelectionPattern2"
    }
    
    # Find the profile item
    $profileItem = $profileList.FindFirst([System.Windows.Automation.TreeScope]::Descendants, 
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::NameProperty, "hieuck.adobe2022@gmail.com"))
    
    if ($profileItem -ne $null) {
        Write-Output "Profile item found: $($profileItem.Current.Name)"
        Write-Output "  ControlType: $($profileItem.Current.ControlType.ProgrammaticName)"
        Write-Output "  IsEnabled: $($profileItem.Current.IsEnabled)"
        Write-Output "  IsOffscreen: $($profileItem.Current.IsOffscreen)"
        
        # Check supported patterns
        $patterns = $profileItem.GetSupportedPatterns()
        Write-Output "  Supported patterns:"
        foreach ($p in $patterns) {
            Write-Output "    - $($p.ProgrammaticName)"
        }
        
        # Check if it's a ListItem
        if ($profileItem.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem) {
            Write-Output "  This is a ListItem"
            
            # Check SelectionItemPattern
            $selectionItemPattern = $profileItem.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
            if ($selectionItemPattern) {
                Write-Output "    Supports SelectionItemPattern"
                Write-Output "    IsSelected: $($selectionItemPattern.Current.IsSelected)"
            } else {
                Write-Output "    Does NOT support SelectionItemPattern"
            }
        }
        
        # Try to find parent ListBox
        $parent = $profileItem.Current.Parent
        Write-Output "  Parent: $($parent.Current.Name) - $($parent.Current.ControlType.ProgrammaticName)"
    }
    
    # Also check the ProfileList's selected item
    if ($selectionPattern) {
        $selectedItems = $selectionPattern.Current.GetSelection()
        Write-Output "Current selection count: $($selectedItems.Count)"
        foreach ($item in $selectedItems) {
            Write-Output "  Selected: $($item.Current.Name)"
        }
    }
    
    # List all items in the list
    $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
    $visibleProfiles = $profileList.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
    Write-Output "All ListItems in ProfileList (count: $($visibleProfiles.Count)):"
    for ($i = 0; $i -lt $visibleProfiles.Count; $i++) {
        $item = $visibleProfiles[$i]
        Write-Output "  [$i] Name: '$($item.Current.Name)', ControlType: $($item.Current.ControlType.ProgrammaticName)"
        
        # Check patterns for each
        $itemPatterns = $item.GetSupportedPatterns()
        foreach ($p in $itemPatterns) {
            Write-Output "      Pattern: $($p.ProgrammaticName)"
        }
    }
} else {
    Write-Output "RouterPlus process not found!"
}