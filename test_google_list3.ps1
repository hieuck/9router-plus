Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Get-Process -Name 'RouterPlus' | Select-Object -First 1
$hwnd = $proc.MainWindowHandle

$automation = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)

# Find the Credentials Manager window
$credsWindow = $null
foreach ($elem in $all) {
    if ($elem.Current.Name -like "*Credentials Manager*" -and $elem.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window) {
        $credsWindow = $elem
        Write-Host "Found Credentials Manager window"
        break
    }
}

if ($credsWindow) {
    $children = $credsWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    
    # Find the Google tab
    $googleTab = $null
    foreach ($child in $children) {
        if ($child.Current.AutomationId -eq "GoogleAccountsTab") {
            $googleTab = $child
            break
        }
    }
    
    if ($googleTab) {
        Write-Host "Found Google tab: Name='$($googleTab.Current.Name)' Type='$($googleTab.Current.ControlType)'"
        $pattern = $googleTab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
        if ($pattern) {
            $pattern.Select()
            Write-Host "Selected Google tab"
            Start-Sleep -Seconds 2
        }
    }
    
    # Re-scan from creds window
    $children = $credsWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    
    # Find the GoogleAccountsList
    $googleList = $null
    foreach ($child in $children) {
        if ($child.Current.AutomationId -eq "GoogleAccountsList") {
            $googleList = $child
            Write-Host "Found GoogleAccountsList"
            break
        }
    }
    
    if ($googleList) {
        $listChildren = $googleList.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
        Write-Host "List children count: $($listChildren.Count)"
        for ($i = 0; $i -lt $listChildren.Count; $i++) {
            $child = $listChildren[$i]
            $name = $child.Current.Name
            $autoId = $child.Current.AutomationId
            $controlType = $child.Current.ControlType
            $isEnabled = $child.Current.IsEnabled
            $className = $child.Current.ClassName
            
            if ($name -or $autoId) {
                Write-Host ("  [{0}] Name='{1}' AutoId='{2}' Type='{3}' Enabled={4} Class='{5}'" -f $i, $name, $autoId, $controlType, $isEnabled, $className)
            }
        }
    }
}

Write-Host "Done"