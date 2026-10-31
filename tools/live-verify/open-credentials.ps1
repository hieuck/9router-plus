Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Get-Process -Name 'RouterPlus' | Select-Object -First 1
$hwnd = $proc.MainWindowHandle

$automation = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

# Click the Credentials button by AutomationId
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

# Now dump all elements in the credentials dialog
$all2 = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
Write-Host ("Total elements after opening credentials: " + $all2.Count)

for ($i = 0; $i -lt $all2.Count; $i++) {
    $el = $all2[$i]
    $name = $el.Current.Name
    $autoId = $el.Current.AutomationId
    $className = $el.Current.ClassName
    $controlType = $el.Current.ControlType.ProgrammaticName
    $isEnabled = $el.Current.IsEnabled
    
    if ($name) {
        Write-Host ("[$i] Name='" + $name + "' AutoId='" + $autoId + "' Class='" + $className + "' Type='" + $controlType + "' Enabled=" + $isEnabled)
    }
}