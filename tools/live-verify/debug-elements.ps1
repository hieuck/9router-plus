Write-Host "=== DEBUG: All Elements ==="

$proc = Get-Process -Name 'RouterPlus' | Select-Object -First 1
$hwnd = $proc.MainWindowHandle

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$automation = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
$allDesc = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
Write-Host ("Total elements: " + $allDesc.Count)

# Search for Agnes-related elements
Write-Host ""
Write-Host "--- Elements with 'Agnes' in name ---"
for ($i = 0; $i -lt $allDesc.Count; $i++) {
    $el = $allDesc[$i]
    if ($el.Current.Name -like "*Agnes*") {
        Write-Host ("  [$i] Name='" + $el.Current.Name + "' Type='" + $el.Current.ControlType.ProgrammaticName + "' AutoId='" + $el.Current.AutomationId + "' Enabled=" + $el.Current.IsEnabled + " Offscreen=" + $el.Current.IsOffscreen)
    }
}

# Search for button elements
Write-Host ""
Write-Host "--- All Buttons ---"
for ($i = 0; $i -lt $allDesc.Count; $i++) {
    $el = $allDesc[$i]
    if ($el.Current.ControlType.ProgrammaticName -eq "ControlType.Button" -and $el.Current.Name) {
        Write-Host ("  [$i] Name='" + $el.Current.Name + "' AutoId='" + $el.Current.AutomationId + "' Enabled=" + $el.Current.IsEnabled + " Offscreen=" + $el.Current.IsOffscreen)
    }
}

# Search for "Tu" or "Key" in buttons
Write-Host ""
Write-Host "--- Buttons with 'Tu' or 'Key' or 'lay' ---"
for ($i = 0; $i -lt $allDesc.Count; $i++) {
    $el = $allDesc[$i]
    if ($el.Current.ControlType.ProgrammaticName -eq "ControlType.Button" -and ($el.Current.Name -like "*Tu*" -or $el.Current.Name -like "*Key*" -or $el.Current.Name -like "*lay*")) {
        Write-Host ("  [$i] Name='" + $el.Current.Name + "' AutoId='" + $el.Current.AutomationId + "' Enabled=" + $el.Current.IsEnabled + " Offscreen=" + $el.Current.IsOffscreen)
    }
}

# Search for DataItem elements (cards)
Write-Host ""
Write-Host "--- DataItem elements ---"
for ($i = 0; $i -lt $allDesc.Count; $i++) {
    $el = $allDesc[$i]
    if ($el.Current.ControlType.ProgrammaticName -like "*DataItem*") {
        Write-Host ("  [$i] Name='" + $el.Current.Name + "' AutoId='" + $el.Current.AutomationId + "' Enabled=" + $el.Current.IsEnabled + " Offscreen=" + $el.Current.IsOffscreen)
    }
}

# Search for ProviderCardViewModel
Write-Host ""
Write-Host "--- Elements with 'ProviderCardViewModel' ---"
for ($i = 0; $i -lt $allDesc.Count; $i++) {
    $el = $allDesc[$i]
    if ($el.Current.Name -like "*ProviderCardViewModel*") {
        Write-Host ("  [$i] Name='" + $el.Current.Name + "' Type='" + $el.Current.ControlType.ProgrammaticName + "' AutoId='" + $el.Current.AutomationId + "'")
    }
}