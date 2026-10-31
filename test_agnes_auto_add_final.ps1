Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

$proc = Get-Process -Name 'RouterPlus' | Select-Object -First 1
$hwnd = $proc.MainWindowHandle

$automation = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

# Find and click profile in main UI
$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
$profileItem = $null
foreach ($elem in $all) {
    if ($elem.Current.Name -eq "hieuck.it@gmail.com" -and $elem.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text) {
        $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($elem)
        for ($level = 0; $level -lt 10 -and $parent; $level++) {
            if ($parent.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem) {
                $profileItem = $parent
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
        Add-Type -MemberDefinition @"
            [DllImport("user32.dll")]
            public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint cButtons, uint dwExtraInfo);
"@ -Name "Win32" -Namespace "Native"
        [Native.Win32]::mouse_event(0x0002, 0, 0, 0, 0)
        [Native.Win32]::mouse_event(0x0004, 0, 0, 0, 0)
        Start-Sleep -Seconds 2
    }
}

# Click Agnes provider button
$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
$agnesProviderBtn = $null
foreach ($elem in $all) {
    if ($elem.Current.Name -eq "Agnes (8)" -and $elem.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button) {
        $agnesProviderBtn = $elem
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
        Start-Sleep -Seconds 3
    }
}

# Find Agnes card and click "Tự động thêm"
$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)

$targetBytes = [System.Text.Encoding]::UTF8.GetBytes("Tự động thêm")
$agnesAutoAddBtn = $null

foreach ($elem in $all) {
    if ($elem.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button) {
        $name = $elem.Current.Name
        if ($name) {
            $nameBytes = [System.Text.Encoding]::UTF8.GetBytes($name)
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
            
            if ($match) {
                # Check if it's in Agnes card
                $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($elem)
                for ($i = 0; $i -lt 10 -and $parent; $i++) {
                    if ($parent.Current.Name -like "*Agnes*") {
                        $agnesAutoAddBtn = $elem
                        Write-Host ("Found Agnes 'Tự động thêm' button: Enabled={0}" -f $elem.Current.IsEnabled)
                        break
                    }
                    $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($parent)
                }
                if ($agnesAutoAddBtn) { break }
            }
        }
    }
}

if ($agnesAutoAddBtn) {
    $invokePattern = $agnesAutoAddBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    if ($invokePattern -and $agnesAutoAddBtn.Current.IsEnabled) {
        Write-Host "Invoking Agnes 'Tự động thêm' button..."
        $invokePattern.Invoke()
        Write-Host "Button invoked successfully!"
        
        # Monitor for 30 seconds
        for ($sec = 0; $sec -lt 30; $sec++) {
            Start-Sleep -Seconds 1
            $all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
            foreach ($elem in $all) {
                if ($elem.Current.AutomationId -eq "StatusText") {
                    Write-Host "Status [$sec]: $($elem.Current.Name)"
                    break
                }
            }
        }
    } else {
        Write-Host "Button not invokable or not enabled. IsEnabled=$($agnesAutoAddBtn.Current.IsEnabled)"
    }
} else {
    Write-Host "Could not find Agnes 'Tự động thêm' button"
}

Write-Host "Done"