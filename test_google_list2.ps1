Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Get-Process -Name 'RouterPlus' | Select-Object -First 1
$hwnd = $proc.MainWindowHandle

$automation = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)

# Find the Google tab in Credentials Manager
$googleTab = $null
foreach ($elem in $all) {
    if ($elem.Current.AutomationId -eq "GoogleAccountsTab") {
        $googleTab = $elem
        break
    }
}

if ($googleTab) {
    $pattern = $googleTab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
    if ($pattern) {
        $pattern.Select()
        Write-Host "Selected Google tab"
        Start-Sleep -Seconds 2
    }
}

# Re-scan
$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)

# Find the GoogleAccountsList
$googleList = $null
foreach ($elem in $all) {
    if ($elem.Current.AutomationId -eq "GoogleAccountsList") {
        $googleList = $elem
        Write-Host "Found GoogleAccountsList: Name='$($elem.Current.Name)' Type='$($elem.Current.ControlType)' Class='$($elem.Current.ClassName)'"
        break
    }
}

if ($googleList) {
    $children = $googleList.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    Write-Host "Children count: $($children.Count)"
    for ($i = 0; $i -lt $children.Count; $i++) {
        $child = $children[$i]
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

Write-Host "Done"