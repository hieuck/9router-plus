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

# Re-scan and find Agnes card directly
$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)

$agnesCard = $null
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
            $agnesCard = $elem
            Write-Host "Found Agnes card"
            break
        }
    }
}

if ($agnesCard) {
    $children = $agnesCard.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    
    # Find "Tự động thêm" button in Agnes card children
    $agnesAutoAddBtn = $null
    foreach ($child in $children) {
        if ($child.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button) {
            $name = $child.Current.Name
            if ($name -and $name -like "*Tự*" -and $name -like "*thêm*") {
                $agnesAutoAddBtn = $child
                Write-Host ("Found Agnes 'Tự động thêm' button in card: Enabled={0}" -f $child.Current.IsEnabled)
                break
            }
        }
    }
    
    if ($agnesAutoAddBtn -and $agnesAutoAddBtn.Current.IsEnabled) {
        $invokePattern = $agnesAutoAddBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        if ($invokePattern) {
            Write-Host "Invoking Agnes 'Tự động thêm' button..."
            $invokePattern.Invoke()
            Write-Host "Button invoked successfully!"
            
            # Monitor for 60 seconds
            for ($sec = 0; $sec -lt 60; $sec++) {
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
            Write-Host "Button has no InvokePattern"
        }
    } else {
        Write-Host "Could not find enabled Agnes 'Tự động thêm' button in card"
    }
} else {
    Write-Host "Agnes card not found"
}

Write-Host "Done"