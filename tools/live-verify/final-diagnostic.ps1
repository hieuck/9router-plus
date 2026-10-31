Write-Host "=== LIVE DIAGNOSTIC: Agnes 'Tu lay Key' ==="
Write-Host "UTC Start: $(Get-Date -Format u)"

$proc = Get-Process -Name 'RouterPlus' | Select-Object -First 1
$hwnd = $proc.MainWindowHandle
Write-Host "HWND: $hwnd"

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$automation = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

# Verify profile is selected
$selectedCond = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "SelectedProfileName")
$selectedProfile = $automation.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $selectedCond)
if ($selectedProfile) {
    Write-Host ("SelectedProfileName: '" + $selectedProfile.Current.Name + "'")
} else {
    Write-Host "SelectedProfileName NOT found"
    exit 1
}

# Find Agnes card and "Tu lay Key" button
$allDesc = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)

$agnesCard = $null
$tuLayKeyButton = $null
$statusTextEl = $null

for ($i = 0; $i -lt $allDesc.Count; $i++) {
    $el = $allDesc[$i]
    $name = $el.Current.Name
    $autoId = $el.Current.AutomationId
    $controlType = $el.Current.ControlType.ProgrammaticName
    
    if ($autoId -eq "StatusText") {
        $statusTextEl = $el
    }
    
    if ($name -eq "Tự lấy key" -and $controlType -eq "ControlType.Button") {
        $tuLayKeyButton = $el
        Write-Host ("Found 'Tu lay Key' button at index " + $i + ": Name='" + $name + "' Type=" + $controlType + " Enabled=" + $el.Current.IsEnabled + " Offscreen=" + $el.Current.IsOffscreen)
    }
}

if (-not $tuLayKeyButton) {
    Write-Host "ERROR: 'Tu lay Key' button NOT found"
    exit 1
}

Write-Host ""
Write-Host "--- Button properties ---"
Write-Host ("IsEnabled: " + $tuLayKeyButton.Current.IsEnabled)
Write-Host ("IsOffscreen: " + $tuLayKeyButton.Current.IsOffscreen)
Write-Host ("BoundingRectangle: " + $tuLayKeyButton.Current.BoundingRectangle)

$invokePattern = $tuLayKeyButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
if ($invokePattern) {
    Write-Host "InvokePattern: Available"
} else {
    Write-Host "InvokePattern: NOT Available"
    exit 1
}

if ($tuLayKeyButton.Current.IsEnabled -and -not $tuLayKeyButton.Current.IsOffscreen) {
    Write-Host ""
    Write-Host "--- Invoking 'Tu lay Key' button ---"
    $startTime = Get-Date -Format u
    Write-Host ("UTC Invoke time: " + $startTime)
    
    $invokePattern.Invoke()
    Write-Host "Button invoked"
    
    # Poll status for up to 120s
    Write-Host ""
    Write-Host "--- Polling status (max 120s) ---"
    $maxWait = 120
    $interval = 2
    $elapsed = 0
    
    while ($elapsed -lt $maxWait) {
        Start-Sleep -Seconds $interval
        $elapsed += $interval
        
        if ($statusTextEl) {
            $currentStatus = $statusTextEl.Current.Name
            Write-Host ("[" + $elapsed + " s] StatusText: '" + $currentStatus + "'")
        }
        
        if ($tuLayKeyButton) {
            $enabled = $tuLayKeyButton.Current.IsEnabled
            Write-Host ("[" + $elapsed + " s] Button IsEnabled: " + $enabled)
        }
        
        # Check for Chrome process
        $chromeProcs = Get-Process -Name "chrome" -ErrorAction SilentlyContinue
        if ($chromeProcs) {
            Write-Host ("[" + $elapsed + " s] Chrome processes detected: " + $chromeProcs.Count)
        }
    }
    
    Write-Host ""
    Write-Host ("--- Polling complete (" + $maxWait + " s) ---")
} else {
    Write-Host ""
    Write-Host "--- Button is DISABLED or OFFSCREEN ---"
    Write-Host ("Reason: IsEnabled=" + $tuLayKeyButton.Current.IsEnabled + ", IsOffscreen=" + $tuLayKeyButton.Current.IsOffscreen)
}

# Check for Agnes connection via API
Write-Host ""
Write-Host "--- Check Agnes connection via API ---"
try {
    $response = Invoke-RestMethod -Uri "http://localhost:20128/api/providers" -Headers @{ "Authorization" = "Bearer 123456" } -Method Get
    $agnesConns = $response | Where-Object { $_.provider -eq "Agnes" -and $_.name -eq "hieuck.it@gmail.com" }
    if ($agnesConns) {
        Write-Host "Agnes connection found for hieuck.it@gmail.com:"
        $agnesConns | ForEach-Object {
            Write-Host ("  Id: " + $_.id)
            Write-Host ("  Name: " + $_.name)
            Write-Host ("  CreatedAt: " + $_.createdAt)
            Write-Host ("  IsActive: " + $_.isActive)
            Write-Host ("  TestStatus: " + $_.testStatus)
        }
    } else {
        Write-Host "No Agnes connection found for hieuck.it@gmail.com"
    }
} catch {
    Write-Host ("API check failed: " + $_.Exception.Message)
}

Write-Host ""
Write-Host "=== DIAGNOSTIC COMPLETE ==="
Write-Host ("UTC End: " + (Get-Date -Format u))