Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

$proc = Get-Process -Name 'RouterPlus' | Select-Object -First 1
$hwnd = $proc.MainWindowHandle

$automation = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

# Open Credentials Manager
$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
$credsBtn = $null
foreach ($elem in $all) {
    if ($elem.Current.AutomationId -eq "CredentialsManagerButton") {
        $credsBtn = $elem
        break
    }
}

if ($credsBtn) {
    $pattern = $credsBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    if ($pattern) { $pattern.Invoke(); Start-Sleep -Seconds 2 }
}

# Re-scan and find the Edit button for hieuck.it@gmail.com row
$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)

# Find the Credentials Manager window
$credsWindow = $null
foreach ($elem in $all) {
    if ($elem.Current.Name -like "*Credentials Manager*" -and $elem.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window) {
        $credsWindow = $elem
        break
    }
}

if ($credsWindow) {
    $children = $credsWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    
    # Find the row for hieuck.it@gmail.com
    $targetRow = $null
    foreach ($child in $children) {
        if ($child.Current.ClassName -eq "GoogleAccountRowViewModel") {
            $rowChildren = $child.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
            foreach ($rc in $rowChildren) {
                if ($rc.Current.Name -eq "hieuck.it@gmail.com") {
                    $targetRow = $child
                    Write-Host "Found hieuck.it@gmail.com row"
                    break
                }
            }
            if ($targetRow) { break }
        }
    }
    
    if ($targetRow) {
        # Find Edit button in this row
        $rowChildren = $targetRow.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
        $editBtn = $null
        foreach ($rc in $rowChildren) {
            if ($rc.Current.Name -eq "Edit" -and $rc.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and $rc.Current.IsEnabled) {
                $editBtn = $rc
                Write-Host "Found Edit button in row"
                break
            }
        }
        
        if ($editBtn) {
            $pattern = $editBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
            if ($pattern) {
                Write-Host "Invoking Edit button..."
                $pattern.Invoke()
                Start-Sleep -Seconds 2
            } else {
                Write-Host "No InvokePattern for Edit button"
            }
        }
    }
}

# Re-scan and check if password/TOTP fields are now enabled
$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
$credsWindow = $null
foreach ($elem in $all) {
    if ($elem.Current.Name -like "*Credentials Manager*" -and $elem.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window) {
        $credsWindow = $elem
        break
    }
}

if ($credsWindow) {
    $children = $credsWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    Write-Host "=== After clicking Edit ==="
    for ($i = 0; $i -lt $children.Count; $i++) {
        $child = $children[$i]
        $name = $child.Current.Name
        $autoId = $child.Current.AutomationId
        $controlType = $child.Current.ControlType
        $isEnabled = $child.Current.IsEnabled
        $className = $child.Current.ClassName
        
        if ($autoId -like "*GooglePassword*" -or $autoId -like "*GoogleTotp*" -or $name -eq "Save" -or $name -eq "Edit") {
            Write-Host ("  [{0}] Name='{1}' AutoId='{2}' Type='{3}' Enabled={4} Class='{5}'" -f $i, $name, $autoId, $controlType, $isEnabled, $className)
        }
    }
}

Write-Host "Done"