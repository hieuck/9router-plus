Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

$proc = Get-Process -Name 'RouterPlus' | Select-Object -First 1
$hwnd = $proc.MainWindowHandle

$automation = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

# Find and click the Credentials button if not already open
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

# Find ProfileSearchTextBox in Credentials Manager
$searchBox = $null
foreach ($elem in $all) {
    if ($elem.Current.AutomationId -eq "ProfileSearchTextBox" -and $elem.Current.ControlType -eq [System.Windows.Automation.ControlType]::Edit) {
        $searchBox = $elem
        Write-Host "Found ProfileSearchTextBox in Credentials Manager"
        break
    }
}

if ($searchBox) {
    $pattern = $searchBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    if ($pattern) {
        $pattern.SetValue("hieuck.it@gmail.com")
        Write-Host "Typed email in search box"
        Start-Sleep -Seconds 2
    }
}

# Re-scan to find the profile in the list
$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
$profileItem = $null
foreach ($elem in $all) {
    if ($elem.Current.Name -like "*hieuck.it@gmail.com*" -and $elem.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text) {
        # Find the parent ListBoxItem
        $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($elem)
        for ($i = 0; $i -lt 5 -and $parent; $i++) {
            if ($parent.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem) {
                $profileItem = $parent
                Write-Host "Found profile ListItem: $($parent.Current.Name)"
                break
            }
            $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($parent)
        }
        if ($profileItem) { break }
    }
}

if ($profileItem) {
    $clickablePoint = $profileItem.GetClickablePoint()
    if ($clickablePoint) {
        [System.Windows.Forms.Cursor]::Position = [System.Drawing.Point]::new([int]$clickablePoint.X, [int]$clickablePoint.Y)
        Start-Sleep -Milliseconds 500
        [Native.Win32]::mouse_event(0x0002, 0, 0, 0, 0)
        [Native.Win32]::mouse_event(0x0004, 0, 0, 0, 0)
        Write-Host "Clicked profile item"
        Start-Sleep -Seconds 2
    }
} else {
    Write-Host "Could not find profile item"
}

# Re-scan and find Agnes provider button
$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
$agnesProviderBtn = $null
foreach ($elem in $all) {
    if ($elem.Current.Name -eq "Agnes (8)" -and $elem.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button) {
        $agnesProviderBtn = $elem
        Write-Host "Found Agnes provider button"
        break
    }
}

if ($agnesProviderBtn) {
    $clickablePoint = $agnesProviderBtn.GetClickablePoint()
    if ($clickablePoint) {
        [System.Windows.Forms.Cursor]::Position = [System.Drawing.Point]::new([int]$clickablePoint.X, [int]$clickablePoint.Y)
        Start-Sleep -Milliseconds 500
        [Native.Win32]::mouse_event(0x0002, 0, 0, 0, 0)
        [Native.Win32]::mouse_event(0x0004, 0, 0, 0, 0)
        Write-Host "Clicked Agnes provider button"
        Start-Sleep -Seconds 3
    }
} else {
    Write-Host "Agnes provider button not found"
}

# Re-scan and find Tự động thêm button
$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
$autoAddBtn = $null
foreach ($elem in $all) {
    $name = $elem.Current.Name
    if ($name) {
        $nameBytes = [System.Text.Encoding]::UTF8.GetBytes($name)
        $targetBytes = [System.Text.Encoding]::UTF8.GetBytes("Tự động thêm")
        $match = $true
        if ($nameBytes.Length -eq $targetBytes.Length) {
            for ($k = 0; $k -lt $nameBytes.Length; $k++) {
                if ($nameBytes[$k] -ne $targetBytes[$k]) {
                    $match = $false
                    break
                }
            }
        } else {
            $match = $false
        }
        
        if ($match -and $elem.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button) {
            Write-Host ("Found 'Tự động thêm' button: Enabled={0}" -f $elem.Current.IsEnabled)
            $autoAddBtn = $elem
            break
        }
    }
}

if ($autoAddBtn) {
    $invokePattern = $autoAddBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    if ($invokePattern -and $autoAddBtn.Current.IsEnabled) {
        Write-Host "Invoking 'Tự động thêm' button..."
        $invokePattern.Invoke()
        Write-Host "Button invoked successfully!"
    } else {
        Write-Host "Button not invokable or not enabled. IsEnabled=$($autoAddBtn.Current.IsEnabled)"
    }
} else {
    Write-Host "Could not find 'Tự động thêm' button"
}

Write-Host "Done"