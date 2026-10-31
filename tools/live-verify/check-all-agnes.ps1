Add-Type -AssemblyName UIAutomationClient
$proc = Get-Process -Name "RouterPlus" | Select-Object -First 1
if ($proc) {
    $element = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
    $allElements = $element.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    
    # Find ProfileList
    $profileList = $null
    for ($i = 0; $i -lt $allElements.Count; $i++) {
        if ($allElements[$i].Current.AutomationId -eq "ProfileList") {
            $profileList = $allElements[$i]
            break
        }
    }
    
    if ($profileList) {
        $items = $profileList.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
        
        # Check each profile that has an email
        $profilesToCheck = @(
            "arpachy85@gmail.com",
            "dungbanemok@gmail.com",
            "gocmobi@gmail.com", 
            "hieuck.2022@gmail.com",
            "hieuck.2023@gmail.com",
            "hieuck.adobe2021@gmail.com",
            "hieuck.adobe2022@gmail.com",
            "hieuck.anhdv@gmail.com",
            "hieuck.apk@gmail.com"
        )
        
        foreach ($email in $profilesToCheck) {
            for ($i = 0; $i -lt $items.Count; $i++) {
                $el = $items[$i]
                if ($el.Current.Name -eq $email -and $el.Current.ControlType.ProgrammaticName -eq "ControlType.Text") {
                    $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($el)
                    if ($parent -and $parent.Current.ControlType.ProgrammaticName -eq "ControlType.ListItem") {
                        $pattern = $parent.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
                        if ($pattern) {
                            $pattern.Select()
                            Start-Sleep -Milliseconds 500
                            
                            # Re-find elements after selection
                            $element2 = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
                            $allElements2 = $element2.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
                            
                            for ($k = 0; $k -lt $allElements2.Count; $k++) {
                                $el2 = $allElements2[$k]
                                if ($el2.Current.Name -eq "RouterPlus.App.ViewModels.ProviderCardViewModel" -and $el2.Current.ControlType.ProgrammaticName -eq "ControlType.DataItem") {
                                    $children = $el2.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
                                    $agnesStatus = ""
                                    for ($j = 0; $j -lt $children.Count; $j++) {
                                        $ch = $children[$j]
                                        if ($ch.Current.Name -like "*Agnes*" -and ($ch.Current.Name -eq "Agnes AI" -or $ch.Current.Name -eq "AG")) {
                                            # Check next element for status
                                            if ($j+1 -lt $children.Count) {
                                                $agnesStatus = $children[$j+1].Current.Name
                                            }
                                        }
                                        if ($ch.Current.Name -eq "Online" -or $ch.Current.Name -eq "Not added" -or $ch.Current.Name -eq "Offline") {
                                            $agnesStatus = $ch.Current.Name
                                        }
                                    }
                                    Write-Output ("{0}: Agnes = {1}" -f $email, $agnesStatus)
                                    break
                                }
                            }
                        }
                    }
                    break
                }
            }
        }
    }
} else {
    Write-Output "Process not found"
}