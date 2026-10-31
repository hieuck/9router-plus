Add-Type -AssemblyName 'UIAutomationClient'
Add-Type -AssemblyName 'UIAutomationTypes'

$wins = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
$window = $null
foreach ($w in $wins) {
    if ($w.Current.Name -like '*RouterPlus*' -or $w.Current.Name -like '*9Router*' -or $w.Current.ProcessId -eq (Get-Process -Name 'RouterPlus' -ErrorAction SilentlyContinue).Id) {
        $window = $w
        break
    }
}
if (-not $window) { Write-Host 'ERROR: Window not found'; exit 1 }
Write-Host "Found: $($window.Current.Name)"

$statusText = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, 
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "StatusText"))

Write-Host "=== Monitoring started ==="
for ($i = 0; $i -lt 48; $i++) { # 48 * 5s = 240s
    Start-Sleep -Seconds 5
    
    if ($statusText) {
        Write-Host "[$(Get-Date -Format HH:mm:ss)] StatusText: $($statusText.Current.Name)"
    }
    
    $all = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($c in $all) {
        $name = $c.Current.Name
        if (-not $name) { $name = "" }
        if ($name -match 'AutoGetKey|Agnes|ChromeLauncher|ConnectAgnes|page_url|Google|Popup|Create|ApiKey|POST|Connection|error') {
            Write-Host "[$(Get-Date -Format HH:mm:ss)] Log: $name"
        }
    }
}
Write-Host "=== Monitoring ended ==="