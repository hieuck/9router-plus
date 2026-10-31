Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Get-Process -Name 'RouterPlus' | Select-Object -First 1
$hwnd = $proc.MainWindowHandle

$automation = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

$all = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)

# Find the Agnes "Tự động thêm" button
$targetBytes = [System.Text.Encoding]::UTF8.GetBytes("Tự động thêm")

Write-Host "=== Looking for 'Tự động thêm' button ==="
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
                Write-Host ("Found 'Tự động thêm' button: Enabled={0} AutoId='{1}'" -f $elem.Current.IsEnabled, $elem.Current.AutomationId)
                
                # Check if it's in Agnes card
                $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($elem)
                for ($i = 0; $i -lt 10 -and $parent; $i++) {
                    if ($parent.Current.Name -like "*Agnes*") {
                        Write-Host "  *** IN AGNES CARD! ***"
                        break
                    }
                    $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($parent)
                }
            }
        }
    }
}

# Also check status text
Write-Host ""
Write-Host "=== Status text ==="
foreach ($elem in $all) {
    if ($elem.Current.AutomationId -eq "StatusText") {
        Write-Host "StatusText: $($elem.Current.Name)"
    }
}

Write-Host "Done"