Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Get-Process -Name 'RouterPlus' | Select-Object -First 1
$hwnd = $proc.MainWindowHandle

$automation = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

# Click the "🔐  Credentials" button to open vault
$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
for ($i = 0; $i -lt $all.Count; $i++) {
    $el = $all[$i]
    $name = $el.Current.Name
    $autoId = $el.Current.AutomationId
    $controlType = $el.Current.ControlType.ProgrammaticName
    
    if ($autoId -eq "CredentialsManagerButton") {
        Write-Host ("Found Credentials button at index " + $i + ": Name='" + $name + "' AutoId='" + $autoId + "'")
        $invokePattern = $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        if ($invokePattern) {
            $invokePattern.Invoke()
            Write-Host "Clicked Credentials button"
        } else {
            Write-Host "No InvokePattern"
        }
        break
    }
}

Start-Sleep -Seconds 2

# Check if vault is locked (look for password box or unlock button)
$all2 = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
for ($i = 0; $i -lt $all2.Count; $i++) {
    $el = $all2[$i]
    $name = $el.Current.Name
    $autoId = $el.Current.AutomationId
    $className = $el.Current.ClassName
    $controlType = $el.Current.ControlType.ProgrammaticName
    $isEnabled = $el.Current.IsEnabled
    
    if ($name -like "*vault*" -or $name -like "*Vault*" -or $name -like "*password*" -or $name -like "*Password*" -or $name -like "*unlock*" -or $name -like "*Unlock*" -or $autoId -like "*vault*" -or $autoId -like "*unlock*" -or $className -like "*PasswordBox*") {
        Write-Host ("[$i] Name='" + $name + "' AutoId='" + $autoId + "' Class='" + $className + "' Type='" + $controlType + "' Enabled=" + $isEnabled)
    }
}