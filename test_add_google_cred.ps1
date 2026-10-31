Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

$proc = Get-Process -Name 'RouterPlus' | Select-Object -First 1
$hwnd = $proc.MainWindowHandle

$automation = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

# Find and click the Credentials button
$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
$credsBtn = $null
foreach ($elem in $all) {
    if ($elem.Current.AutomationId -eq "CredentialsManagerButton") {
        $credsBtn = $elem
        Write-Host "Found Credentials button"
        break
    }
}

if ($credsBtn) {
    $clickablePoint = $credsBtn.GetClickablePoint()
    if ($clickablePoint) {
        [System.Windows.Forms.Cursor]::Position = [System.Drawing.Point]::new([int]$clickablePoint.X, [int]$clickablePoint.Y)
        Start-Sleep -Milliseconds 500
        Add-Type -MemberDefinition @"
            [DllImport("user32.dll")]
            public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint cButtons, uint dwExtraInfo);
"@ -Name "Win32" -Namespace "Native"
        [Native.Win32]::mouse_event(0x0002, 0, 0, 0, 0)
        [Native.Win32]::mouse_event(0x0004, 0, 0, 0, 0)
        Write-Host "Clicked Credentials button"
        Start-Sleep -Seconds 2
    }
}

# Re-scan
$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)

# Find the Google tab in Credentials Manager and click it
$googleTab = $null
foreach ($elem in $all) {
    if ($elem.Current.AutomationId -eq "GoogleAccountsTab") {
        $googleTab = $elem
        Write-Host "Found Google tab"
        break
    }
}

if ($googleTab) {
    $clickablePoint = $googleTab.GetClickablePoint()
    if ($clickablePoint) {
        [System.Windows.Forms.Cursor]::Position = [System.Drawing.Point]::new([int]$clickablePoint.X, [int]$clickablePoint.Y)
        Start-Sleep -Milliseconds 500
        [Native.Win32]::mouse_event(0x0002, 0, 0, 0, 0)
        [Native.Win32]::mouse_event(0x0004, 0, 0, 0, 0)
        Write-Host "Clicked Google tab"
        Start-Sleep -Seconds 1
    }
}

# Re-scan
$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)

# Find the row for hieuck.it@gmail.com in the Google accounts list
$targetRow = $null
foreach ($elem in $all) {
    $name = $elem.Current.Name
    if ($name -eq "hieuck.it@gmail.com") {
        $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($elem)
        for ($level = 0; $level -lt 5 -and $parent; $level++) {
            if ($parent.Current.ClassName -eq "GoogleAccountRowViewModel") {
                $targetRow = $parent
                Write-Host "Found hieuck.it@gmail.com row: Name='$($parent.Current.Name)' Class='$($parent.Current.ClassName)'"
                break
            }
            $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($parent)
        }
        if ($targetRow) { break }
    }
}

if ($targetRow) {
    # Find the Edit button in this row
    $children = $targetRow.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    $editBtn = $null
    foreach ($child in $children) {
        if ($child.Current.Name -eq "Edit" -and $child.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button) {
            $editBtn = $child
            Write-Host "Found Edit button in row"
            break
        }
    }
    
    if ($editBtn) {
        $clickablePoint = $editBtn.GetClickablePoint()
        if ($clickablePoint) {
            [System.Windows.Forms.Cursor]::Position = [System.Drawing.Point]::new([int]$clickablePoint.X, [int]$clickablePoint.Y)
            Start-Sleep -Milliseconds 500
            [Native.Win32]::mouse_event(0x0002, 0, 0, 0, 0)
            [Native.Win32]::mouse_event(0x0004, 0, 0, 0, 0)
            Write-Host "Clicked Edit button"
            Start-Sleep -Seconds 1
        }
    }
    
    # Now find the password and TOTP fields and fill them
    $all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    
    # Find the password editor in the row
    foreach ($elem in $all) {
        if ($elem.Current.AutomationId -eq "GooglePasswordEditor" -and $elem.Current.IsEnabled) {
            $pattern = $elem.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
            if ($pattern) {
                $pattern.SetValue("YOUR_PASSWORD_HERE")
                Write-Host "Entered password"
            }
        }
    }
    
    # Find the TOTP editor
    foreach ($elem in $all) {
        if ($elem.Current.AutomationId -eq "GoogleTotpEditor" -and $elem.Current.IsEnabled) {
            $pattern = $elem.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
            if ($pattern) {
                $pattern.SetValue("YOUR_TOTP_HERE")
                Write-Host "Entered TOTP"
            }
        }
    }
    
    # Click Save
    $all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($elem in $all) {
        if ($elem.Current.Name -eq "Save" -and $elem.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and $elem.Current.IsEnabled) {
            $clickablePoint = $elem.GetClickablePoint()
            if ($clickablePoint) {
                [System.Windows.Forms.Cursor]::Position = [System.Drawing.Point]::new([int]$clickablePoint.X, [int]$clickablePoint.Y)
                Start-Sleep -Milliseconds 500
                [Native.Win32]::mouse_event(0x0002, 0, 0, 0, 0)
                [Native.Win32]::mouse_event(0x0004, 0, 0, 0, 0)
                Write-Host "Clicked Save button"
                Start-Sleep -Seconds 2
            }
            break
        }
    }
} else {
    Write-Host "Could not find hieuck.it@gmail.com row in Google accounts"
}

Write-Host "Done"