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

# Re-scan
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
    
    # Find the row for hieuck.it@gmail.com (as ListBoxItem)
    $targetRow = $null
    foreach ($child in $children) {
        if ($child.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and $child.Current.ClassName -eq "ListBoxItem") {
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
        # Find the Google login button (🚀) in this row
        $rowChildren = $targetRow.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
        $loginBtn = $null
        foreach ($rc in $rowChildren) {
            if ($rc.Current.AutomationId -eq "GoogleLoginRowButton" -and $rc.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button) {
                $loginBtn = $rc
                Write-Host "Found Google login button: Name='$($rc.Current.Name)' Enabled=$($rc.Current.IsEnabled)"
                break
            }
        }
        
        if ($loginBtn) {
            $pattern = $loginBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
            if ($pattern) {
                Write-Host "Invoking Google login button..."
                $pattern.Invoke()
                Start-Sleep -Seconds 3
            } else {
                Write-Host "No InvokePattern for login button"
            }
        }
    }
}

# Re-scan and check status
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
    Write-Host "=== Status after login attempt ==="
    for ($i = 0; $i -lt $children.Count; $i++) {
        $child = $children[$i]
        $name = $child.Current.Name
        $autoId = $child.Current.AutomationId
        if ($autoId -eq "CredentialsManagerStatus") {
            Write-Host ("  [{0}] Name='{1}' AutoId='{2}'" -f $i, $name, $autoId)
        }
    }
}

Write-Host "Done"