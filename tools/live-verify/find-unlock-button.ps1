Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Get-Process -Name 'RouterPlus' | Select-Object -First 1
$hwnd = $proc.MainWindowHandle

$automation = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

# Check all buttons in credentials dialog - look for unlock
$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
for ($i = 0; $i -lt $all.Count; $i++) {
    $el = $all[$i]
    $name = $el.Current.Name
    $autoId = $el.Current.AutomationId
    $className = $el.Current.ClassName
    $controlType = $el.Current.ControlType.ProgrammaticName
    $isEnabled = $el.Current.IsEnabled
    
    if ($controlType -eq "ControlType.Button" -and ($name -like "*unlock*" -or $name -like "*Unlock*" -or $name -like "*mở*" -or $name -like "*Mở*" -or $name -like "*lock*" -or $name -like "*Lock*" -or $autoId -like "*unlock*" -or $autoId -like "*lock*" -or $autoId -like "*vault*")) {
        Write-Host ("[$i] Name='" + $name + "' AutoId='" + $autoId + "' Class='" + $className + "' Type='" + $controlType + "' Enabled=" + $isEnabled)
    }
}