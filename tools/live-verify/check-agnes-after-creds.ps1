Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Get-Process -Name 'RouterPlus' | Select-Object -First 1
$hwnd = $proc.MainWindowHandle

$automation = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

# Close the credentials dialog first - look for a close button
$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
for ($i = 0; $i -lt $all.Count; $i++) {
    $el = $all[$i]
    $name = $el.Current.Name
    $autoId = $el.Current.AutomationId
    $controlType = $el.Current.ControlType.ProgrammaticName
    
    if (($name -eq "Close" -or $name -eq "×" -or $autoId -like "*Close*") -and $controlType -eq "ControlType.Button") {
        Write-Host ("Found close button at index " + $i + ": Name='" + $name + "' AutoId='" + $autoId + "'")
        $invokePattern = $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        if ($invokePattern) {
            $invokePattern.Invoke()
            Write-Host "Clicked close button"
        }
        break
    }
}

Start-Sleep -Seconds 1

# Now check the Agnes "Tự động thêm" button state
$all2 = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
for ($i = 0; $i -lt $all2.Count; $i++) {
    $el = $all2[$i]
    $name = $el.Current.Name
    $autoId = $el.Current.AutomationId
    $controlType = $el.Current.ControlType.ProgrammaticName
    $isEnabled = $el.Current.IsEnabled
    
    if ($name -eq "Tự động thêm" -and $controlType -eq "ControlType.Button") {
        Write-Host ("Tự động thêm button at index " + $i + ": Enabled=" + $isEnabled)
    }
}