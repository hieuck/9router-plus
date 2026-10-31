Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Get-Process -Name 'RouterPlus' | Select-Object -First 1
$hwnd = $proc.MainWindowHandle

$automation = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
Write-Host "Total elements: " $all.Count

# Dump everything with Agnes or Tự động
for ($i = 0; $i -lt $all.Count; $i++) {
    $el = $all[$i]
    $name = $el.Current.Name
    $autoId = $el.Current.AutomationId
    $controlType = $el.Current.ControlType.ProgrammaticName
    $isEnabled = $el.Current.IsEnabled
    $className = $el.Current.ClassName
    
    if ($name -like "*Agnes*" -or $name -like "*Tự*" -or $name -like "*tự*" -or $name -like "*thêm*" -or $name -like "*Thêm*" -or $name -like "*Auto*" -or $name -like "*Add*") {
        Write-Host "[$i] Name='$name' AutoId='$autoId' Type='$controlType' Enabled=$isEnabled Class='$className'"
    }
}

Write-Host ""
Write-Host "=== All ProviderCardViewModel items ==="
foreach ($elem in $all) {
    if ($elem.Current.ClassName -eq "RouterPlus.App.ViewModels.ProviderCardViewModel" -and $elem.Current.ControlType -eq [System.Windows.Automation.ControlType]::DataItem) {
        $children = $elem.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
        Write-Host "Card: Name='{0}' Class='{1}' Type='{2}' Children={3}" -f $elem.Current.Name, $elem.Current.ClassName, $elem.Current.ControlType, $children.Count
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
    }
}

Write-Host "Done"