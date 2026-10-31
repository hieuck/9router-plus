Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

$proc = Get-Process -Name 'RouterPlus' | Select-Object -First 1
$hwnd = $proc.MainWindowHandle

$automation = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)

# Find the profile list item for hieuck.it@gmail.com in main UI
$profileItem = $null
foreach ($elem in $all) {
    if ($elem.Current.Name -eq "hieuck.it@gmail.com" -and $elem.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text) {
        $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($elem)
        for ($level = 0; $level -lt 10 -and $parent; $level++) {
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
        Add-Type -MemberDefinition @"
            [DllImport("user32.dll")]
            public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint cButtons, uint dwExtraInfo);
"@ -Name "Win32" -Namespace "Native"
        [Native.Win32]::mouse_event(0x0002, 0, 0, 0, 0)
        [Native.Win32]::mouse_event(0x0004, 0, 0, 0, 0)
        Write-Host "Clicked profile item in main UI"
        Start-Sleep -Seconds 2
    }
}

# Now find Agnes provider button and click it
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
}

# Now check for "Tự động thêm" button
$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)

$targetBytes = [System.Text.Encoding]::UTF8.GetBytes("Tự động thêm")

Write-Host "=== Looking for 'Tự động thêm' button after Agnes click ==="
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
                Write-Host ("Found 'Tự động thêm' button: Enabled={0} AutoId='{1}'" -f $elem.Current.IsEnabled, $elem.Current.AutomationId)
                
                $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($elem)
                for ($i = 0; $i -lt 10 -and $parent; $i++) {
                    if ($parent.Current.Name -like "*Agnes*") {
                        Write-Host "  *** IN AGNES CARD! ***"
                        break
                    }
                    $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($parent)
                }
            }
        }
    }
}

Write-Host "Done"