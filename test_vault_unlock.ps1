Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Get-Process -Name 'RouterPlus' | Select-Object -First 1
$hwnd = $proc.MainWindowHandle

$automation = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
Write-Host "Total elements: " $all.Count
for ($i = 0; $i -lt $all.Count; $i++) {
    $el = $all[$i]
    $name = $el.Current.Name
    $autoId = $el.Current.AutomationId
    $controlType = $el.Current.ControlType.ProgrammaticName
    $isEnabled = $el.Current.IsEnabled
    $className = $el.Current.ClassName
    
    if ($name -like "*vault*" -or $name -like "*Vault*" -or $name -like "*mật khẩu*" -or $name -like "*Mật*" -or $name -like "*password*" -or $name -like "*Password*" -or $name -like "*unlock*" -or $name -like "*Unlock*" -or $name -like "*mở khóa*" -or $name -like "*Mở*" -or $name -like "*lock*" -or $name -like "*Lock*") {
        Write-Host "[$i] Name='$name' AutoId='$autoId' Type='$controlType' Enabled=$isEnabled Class='$className'"
    }
}