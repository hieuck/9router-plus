Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

$proc = Get-Process -Name 'RouterPlus' | Select-Object -First 1
$hwnd = $proc.MainWindowHandle

$automation = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

# Re-scan and find Tự động thêm button - dump all buttons
$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)

Write-Host "=== All buttons with 'Tự động' in name ==="
foreach ($elem in $all) {
    $name = $elem.Current.Name
    if ($name -and ($name -like "*Tự*" -or $name -like "*tự*" -or $name -like "*thêm*" -or $name -like "*Thêm*")) {
        Write-Host ("Name='{0}' Type='{1}' Enabled={2} Class='{3}'" -f $name, $elem.Current.ControlType, $elem.Current.IsEnabled, $elem.Current.ClassName)
        
        # Show parent chain
        $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($elem)
        for ($i = 0; $i -lt 10 -and $parent; $i++) {
            Write-Host ("  Parent {0}: Name='{1}' Class='{2}' Type='{3}'" -f $i, $parent.Current.Name, $parent.Current.ClassName, $parent.Current.ControlType)
            $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($parent)
        }
    }
}

Write-Host ""
Write-Host "=== All ProviderCardViewModel items ==="
foreach ($elem in $all) {
    if ($elem.Current.ClassName -eq "RouterPlus.App.ViewModels.ProviderCardViewModel" -and $elem.Current.ControlType -eq [System.Windows.Automation.ControlType]::DataItem) {
        $children = $elem.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
        $hasAgnes = $false
        foreach ($child in $children) {
            if ($child.Current.Name -eq "Agnes AI") {
                $hasAgnes = $true
                break
            }
        }
        if ($hasAgnes) {
            Write-Host "Found Agnes container: Name='{0}' Class='{1}' Type='{2}'" -f $elem.Current.Name, $elem.Current.ClassName, $elem.Current.ControlType
            Write-Host "=== Children of this container ==="
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

Write-Host "Done"