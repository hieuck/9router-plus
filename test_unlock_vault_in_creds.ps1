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
    
    # Find the UnlockVaultButton
    $unlockBtn = $null
    foreach ($child in $children) {
        if ($child.Current.AutomationId -eq "UnlockVaultButton") {
            $unlockBtn = $child
            Write-Host "Found UnlockVaultButton: Name='$($child.Current.Name)' Enabled=$($child.Current.IsEnabled)"
            break
        }
    }
    
    if ($unlockBtn) {
        $clickablePoint = $unlockBtn.GetClickablePoint()
        if ($clickablePoint) {
            [System.Windows.Forms.Cursor]::Position = [System.Drawing.Point]::new([int]$clickablePoint.X, [int]$clickablePoint.Y)
            Start-Sleep -Milliseconds 500
            Add-Type -MemberDefinition @"
                [DllImport("user32.dll")]
                public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint cButtons, uint dwExtraInfo);
"@ -Name "Win32" -Namespace "Native"
            [Native.Win32]::mouse_event(0x0002, 0, 0, 0, 0)
            [Native.Win32]::mouse_event(0x0004, 0, 0, 0, 0)
            Write-Host "Clicked UnlockVaultButton"
            Start-Sleep -Seconds 3
        } else {
            Write-Host "No clickable point for UnlockVaultButton"
        }
    } else {
        Write-Host "UnlockVaultButton not found"
    }
}

# Re-scan and check for password dialog
$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)

Write-Host "=== Looking for vault password dialog ==="
foreach ($elem in $all) {
    $name = $elem.Current.Name
    $autoId = $elem.Current.AutomationId
    if ($name -like "*Unlock*" -or $name -like "*Vault*" -or $name -like "*Password*" -or $autoId -like "*Vault*" -or $autoId -like "*Password*") {
        Write-Host "Name='$name' AutoId='$autoId' Type='$($elem.Current.ControlType)' Enabled=$($elem.Current.IsEnabled) Class='$($elem.Current.ClassName)'"
    }
}

Write-Host "Done"