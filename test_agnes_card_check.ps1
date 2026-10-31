Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

$proc = Get-Process -Name 'RouterPlus' | Select-Object -First 1
$hwnd = $proc.MainWindowHandle

$automation = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

# Find and click profile in main UI using SelectionItemPattern
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
    $pattern = $profileItem.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
    if ($pattern) {
        Write-Host "Selecting profile via SelectionItemPattern..."
        $pattern.Select()
        Start-Sleep -Seconds 2
    }
}

# Click Agnes provider button using TogglePattern
$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
$agnesProviderBtn = $null
foreach ($elem in $all) {
    if ($elem.Current.Name -eq "Agnes (8)" -and $elem.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button) {
        $agnesProviderBtn = $elem
        break
    }
}

if ($agnesProviderBtn) {
    $pattern = $agnesProviderBtn.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    if ($pattern) {
        Write-Host "Toggling Agnes provider button..."
        $pattern.Toggle()
        Start-Sleep -Seconds 3
    }
}

# Dump all ItemsControlItem with ProviderCardViewModel
$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)

Write-Host "=== All ItemsControlItem with ProviderCardViewModel ==="
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

# Check all buttons
Write-Host ""
Write-Host "=== All buttons with 'Tự động' in name ==="
$targetBytes = [System.Text.Encoding]::UTF8.GetBytes("Tự động thêm")
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
                Write-Host ("Found 'Tự động thêm': Enabled={0} AutoId='{1}'" -f $elem.Current.IsEnabled, $elem.Current.AutomationId)
            }
        }
    }
}

Write-Host "Done"