Add-Type -AssemblyName UIAutomationClient

$proc = Get-Process -Name "RouterPlus" | Select-Object -First 1
if ($proc) {
    $element = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
    
    # Step 1: Find ProfileSearchTextBox and set text
    $searchBox = $element.FindFirst([System.Windows.Automation.TreeScope]::Descendants, 
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "ProfileSearchTextBox"))
    
    $textPattern = $searchBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    Write-Output "Current search box value: $($textPattern.Current.Value)"
    $textPattern.SetValue("hieuck.adobe2022@gmail.com")
    Start-Sleep -Seconds 1
    Write-Output "After set value: $($textPattern.Current.Value)"
    
    Start-Sleep -Seconds 3
    
    # Find the profile list
    $profileList = $element.FindFirst([System.Windows.Automation.TreeScope]::Descendants, 
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "ProfileList"))
    
    Write-Output "ProfileList: $($profileList.Current.Name) - $($profileList.Current.ControlType.ProgrammaticName)"
    
    # Try VirtualizedItemPattern or find items via scrolling
    $virtualizedPattern = $profileList.GetCurrentPattern([System.Windows.Automation.VirtualizedItemPattern]::Pattern)
    if ($virtualizedPattern) {
        Write-Output "ProfileList supports VirtualizedItemPattern"
    } else {
        Write-Output "ProfileList does NOT support VirtualizedItemPattern"
    }
    
    # Check ScrollPattern
    $scrollPattern = $profileList.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
    if ($scrollPattern) {
        Write-Output "ProfileList supports ScrollPattern"
        Write-Output "  HorizontallyScrollable: $($scrollPattern.Current.HorizontallyScrollable)"
        Write-Output "  VerticallyScrollable: $($scrollPattern.Current.VerticallyScrollable)"
        Write-Output "  HorizontalScrollPercent: $($scrollPattern.Current.HorizontalScrollPercent)"
        Write-Output "  VerticalScrollPercent: $($scrollPattern.Current.VerticalScrollPercent)"
    } else {
        Write-Output "ProfileList does NOT support ScrollPattern"
    }
    
    # Try to scroll down and find items
    if ($scrollPattern) {
        $scrollPattern.SetScrollPercent(-1, 0)  # Scroll to top
        Start-Sleep -Seconds 1
        
        $children = $profileList.FindAll([System.Windows.Automation.TreeScope]::Children, 
            [System.Windows.Automation.Condition]::TrueCondition)
        Write-Output "Children after scroll to top: $($children.Count)"
        for ($i = 0; $i -lt $children.Count; $i++) {
            $child = $children[$i]
            Write-Output "  [$i] Name: '$($child.Current.Name)', ControlType: $($child.Current.ControlType.ProgrammaticName)"
        }
        
        # Scroll down
        $scrollPattern.SetScrollPercent(-1, 50)
        Start-Sleep -Seconds 1
        
        $children = $profileList.FindAll([System.Windows.Automation.TreeScope]::Children, 
            [System.Windows.Automation.Condition]::TrueCondition)
        Write-Output "Children after scroll to 50%: $($children.Count)"
        for ($i = 0; $i -lt $children.Count; $i++) {
            $child = $children[$i]
            Write-Output "  [$i] Name: '$($child.Current.Name)', ControlType: $($child.Current.ControlType.ProgrammaticName)"
        }
    }
    
    # Search from main window for any element with the profile name
    Write-Output ""
    Write-Output "Searching from main window for 'hieuck.adobe2022@gmail.com'..."
    $allElements = $element.FindAll([System.Windows.Automation.TreeScope]::Descendants, 
        [System.Windows.Automation.Condition]::TrueCondition)
    Write-Output "Total elements: $($allElements.Count)"
    
    for ($i = 0; $i -lt $allElements.Count; $i++) {
        $el = $allElements[$i]
        if ($el.Current.Name -like "*hieuck.adobe2022*") {
            Write-Output "  FOUND: Name='$($el.Current.Name)', ControlType=$($el.Current.ControlType.ProgrammaticName), AutoId=$($el.Current.AutomationId)"
        }
    }
    
    # Also search for "adobe2022"
    Write-Output ""
    Write-Output "Searching for 'adobe2022'..."
    for ($i = 0; $i -lt $allElements.Count; $i++) {
        $el = $allElements[$i]
        if ($el.Current.Name -like "*adobe2022*") {
            Write-Output "  FOUND: Name='$($el.Current.Name)', ControlType=$($el.Current.ControlType.ProgrammaticName), AutoId=$($el.Current.AutomationId)"
        }
    }
} else {
    Write-Output "RouterPlus process not found!"
}