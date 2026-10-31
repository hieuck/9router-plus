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

# Dump all ProviderCardViewModel items
$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)

Write-Host "=== All ProviderCardViewModel / ItemsControlItem ==="
foreach ($elem in $all) {
    if ($elem.Current.ClassName -eq "RouterPlus.App.ViewModels.ProviderCardViewModel" -or $elem.Current.ClassName -eq "ItemsControlItem") {
        $children = $elem.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
        $hasAgnes = $false
        foreach ($child in $children) {
            if ($child.Current.Name -eq "Agnes AI") {
                $hasAgnes = $true
                break
            }
        }
        if ($hasAgnes) {
            Write-Host ("Found Agnes card: Name='{0}' Class='{1}' Type='{2}' Children={3}" -f $elem.Current.Name, $elem.Current.ClassName, $elem.Current.ControlType, $children.Count)
            Write-Host "=== Children of Agnes card ==="
            for ($i = 0; $i -lt $children.Count; $i++) {
                $child = $children[$i]
                $name = $child.Current.Name
                $autoId = $child.Current.AutomationId
                $controlType = $child.Current.ControlType
                $isEnabled = $child.Current.IsEnabled
                $className = $child.Current.ClassName
                
                if ($name -or $autoId -or $className) {
                    Write-Host ("  [{0}] Name='{1}' AutoId='{2}' Type='{3}' Enabled={4} Class='{5}'" -f $i, $name, $autoId, $controlType, $isEnabled, $className)
                }
            }
            break
        }
    }
}

# Also check what the selected profile shows
Write-Host ""
Write-Host "=== Selected profile info ==="
foreach ($elem in $all) {
    if ($elem.Current.AutomationId -eq "SelectedProfileName") {
        Write-Host "SelectedProfileName: '$($elem.Current.Name)'"
    }
}

Write-Host "Done"