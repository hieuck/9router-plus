Add-Type -AssemblyName UIAutomationClient

$proc = Get-Process -Name "RouterPlus" | Select-Object -First 1
if ($proc) {
    $element = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
    
    # Step 1: Find ProfileSearchTextBox and set text
    $searchBox = $element.FindFirst([System.Windows.Automation.TreeScope]::Descendants, 
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "ProfileSearchTextBox"))
    
    $textPattern = $searchBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    $textPattern.SetValue("hieuck.adobe2022@gmail.com")
    
    Start-Sleep -Seconds 3
    
    # Step 2: Find the profile list
    $profileList = $element.FindFirst([System.Windows.Automation.TreeScope]::Descendants, 
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "ProfileList"))
    
    Write-Output "ProfileList found: $($profileList.Current.Name)"
    Write-Output "  ControlType: $($profileList.Current.ControlType.ProgrammaticName)"
    
    # Check children of ProfileList
    $children = $profileList.FindAll([System.Windows.Automation.TreeScope]::Children, 
        [System.Windows.Automation.Condition]::TrueCondition)
    Write-Output "Children count: $($children.Count)"
    for ($i = 0; $i -lt $children.Count; $i++) {
        $child = $children[$i]
        Write-Output "  [$i] Name: '$($child.Current.Name)', ControlType: $($child.Current.ControlType.ProgrammaticName)"
    }
    
    # Check descendants
    $descendants = $profileList.FindAll([System.Windows.Automation.TreeScope]::Descendants, 
        [System.Windows.Automation.Condition]::TrueCondition)
    Write-Output "Descendants count: $($descendants.Count)"
    for ($i = 0; $i -lt $descendants.Count; $i++) {
        $desc = $descendants[$i]
        if (-not [string]::IsNullOrEmpty($desc.Current.Name)) {
            Write-Output "  [$i] Name: '$($desc.Current.Name)', ControlType: $($desc.Current.ControlType.ProgrammaticName)"
        }
    }
    
    # Try to find the profile item by name
    $profileItem = $profileList.FindFirst([System.Windows.Automation.TreeScope]::Descendants, 
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::NameProperty, "hieuck.adobe2022@gmail.com"))
    
    if ($profileItem -ne $null) {
        Write-Output ""
        Write-Output "Profile item found: $($profileItem.Current.Name)"
        Write-Output "  ControlType: $($profileItem.Current.ControlType.ProgrammaticName)"
        Write-Output "  IsEnabled: $($profileItem.Current.IsEnabled)"
        Write-Output "  IsOffscreen: $($profileItem.Current.IsOffscreen)"
        Write-Output "  AutomationId: $($profileItem.Current.AutomationId)"
        
        # Check supported patterns
        $patterns = $profileItem.GetSupportedPatterns()
        Write-Output "  Supported patterns:"
        foreach ($p in $patterns) {
            Write-Output "    - $($p.ProgrammaticName)"
        }
    } else {
        Write-Output "Profile item NOT found by name"
    }
    
    # Also try searching from the main window
    $profileItem2 = $element.FindFirst([System.Windows.Automation.TreeScope]::Descendants, 
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::NameProperty, "hieuck.adobe2022@gmail.com"))
    
    if ($profileItem2 -ne $null) {
        Write-Output ""
        Write-Output "Profile item found from main window: $($profileItem2.Current.Name)"
        Write-Output "  ControlType: $($profileItem2.Current.ControlType.ProgrammaticName)"
    }
} else {
    Write-Output "RouterPlus process not found!"
}