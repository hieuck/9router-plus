Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

$proc = Get-Process -Name 'RouterPlus' | Select-Object -First 1
$hwnd = $proc.MainWindowHandle

$automation = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

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
    
    # Find the row for hieuck.it@gmail.com
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
        $rowChildren = $targetRow.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
        
        Write-Host "=== Row children after successful login ==="
        for ($i = 0; $i -lt $rowChildren.Count; $i++) {
            $rc = $rowChildren[$i]
            $name = $rc.Current.Name
            $autoId = $rc.Current.AutomationId
            $controlType = $rc.Current.ControlType
            $isEnabled = $rc.Current.IsEnabled
            $className = $rc.Current.ClassName
            
            Write-Host ("  [{0}] Name='{1}' AutoId='{2}' Type='{3}' Enabled={4} Class='{5}'" -f $i, $name, $autoId, $controlType, $isEnabled, $className)
        }
    }
}

# Also check for UnlockVaultButton
Write-Host ""
Write-Host "=== Checking for UnlockVaultButton ==="
foreach ($elem in $all) {
    $name = $elem.Current.Name
    $autoId = $elem.Current.AutomationId
    if ($autoId -eq "UnlockVaultButton" -or $name -like "*Unlock*" -or $name -like "*unlock*") {
        Write-Host "Name='$name' AutoId='$autoId' Type='$($elem.Current.ControlType)' Enabled=$($elem.Current.IsEnabled)"
    }
}

Write-Host "Done"