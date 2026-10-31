Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

# Get the RouterPlus process
$process = Get-Process -Name "RouterPlus" | Select-Object -First 1
if (-not $process) {
    Write-Host "RouterPlus not found"
    exit 1
}

Write-Host "Found RouterPlus process: PID $($process.Id)"

$mainWindow = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
if (-not $mainWindow) {
    Write-Host "Main window not found"
    exit 1
}

Write-Host "Found main window: $($mainWindow.Current.Name)"

# Find ProfileSearchTextBox
$searchTextBox = $mainWindow.FindFirst(
    [System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "ProfileSearchTextBox")
)

if (-not $searchTextBox) {
    Write-Host "ProfileSearchTextBox not found"
    exit 1
}

Write-Host "Found ProfileSearchTextBox"

# Set focus and type email
$pattern = $searchTextBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
if ($pattern) {
    $pattern.SetValue("")
    Start-Sleep -Milliseconds 300
    $pattern.SetValue("hieuck.adobe2022@gmail.com")
    Write-Host "Typed email in search box"
} else {
    Write-Host "ValuePattern not available on search box"
    exit 1
}

Start-Sleep -Seconds 2

# Find ProfileList and select the profile
$profileList = $mainWindow.FindFirst(
    [System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "ProfileList")
)

if (-not $profileList) {
    Write-Host "ProfileList not found"
    exit 1
}

Write-Host "Found ProfileList"

# Find the target profile item
$allElements = $mainWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)

$profileItem = $null
foreach ($elem in $allElements) {
    $name = $elem.Current.Name
    if ($name -and $name -like "*hieuck.adobe2022*") {
        Write-Host "Found target profile element: $name, ControlType=$($elem.Current.ControlType)"
        # Try to get parent ListItem
        $parent = $elem
        for ($i=0; $i -lt 5; $i++) {
            $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($parent)
            if (-not $parent) { break }
            if ($parent.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem) {
                $profileItem = $parent
                Write-Host "Found parent ListItem"
                break
            }
        }
        break
    }
}

if (-not $profileItem) {
    Write-Host "ERROR: Could not find profile ListItem for hieuck.adobe2022@gmail.com"
    exit 1
}

Write-Host "Found profile ListItem, attempting to select..."

# Try SelectionItemPattern
$selectionPattern = $profileItem.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
if ($selectionPattern) {
    $selectionPattern.Select()
    Write-Host "Selected profile via SelectionItemPattern"
} else {
    Write-Host "SelectionItemPattern not available, trying click..."
    $clickablePattern = $profileItem.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    if ($clickablePattern) {
        $clickablePattern.Invoke()
        Write-Host "Invoked profile item"
    } else {
        Write-Host "No SelectionItemPattern or InvokePattern on profile item"
        exit 1
    }
}

Start-Sleep -Seconds 3

# Verify selection via StatusText
$statusText = $mainWindow.FindFirst(
    [System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "StatusText")
)
if ($statusText) {
    $statusPattern = $statusText.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    if ($statusPattern) {
        Write-Host "StatusText after selection: $($statusPattern.Current.Value)"
    } else {
        Write-Host "StatusText Name: $($statusText.Current.Name)"
    }
}

Write-Host ""
Write-Host "=== Looking for Agnes 'Tự động thêm' button ==="

# Click the "Agnes (8)" provider button to select Agnes
Write-Host "Clicking 'Agnes (8)' provider button to select Agnes..."
$agnesProviderBtn = $null
foreach ($elem in $allElements) {
    $name = $elem.Current.Name
    if ($name -and $name -eq "Agnes (8)" -and $elem.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button) {
        $agnesProviderBtn = $elem
        Write-Host "Found 'Agnes (8)' provider button"
        break
    }
}

if ($agnesProviderBtn) {
    # Try SelectionItemPattern for toggle-like behavior
    $selectionPattern = $agnesProviderBtn.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
    if ($selectionPattern) {
        $selectionPattern.Select()
        Write-Host "Selected 'Agnes (8)' via SelectionItemPattern"
    } else {
        $invokePattern = $agnesProviderBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        if ($invokePattern) {
            $invokePattern.Invoke()
            Write-Host "Clicked 'Agnes (8)' provider button via InvokePattern"
        } else {
            $togglePattern = $agnesProviderBtn.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
            if ($togglePattern) {
                $togglePattern.Toggle()
                Write-Host "Toggled 'Agnes (8)' provider button"
            } else {
                Write-Host "No pattern available on Agnes provider button"
            }
        }
    }
    Start-Sleep -Seconds 2
    
    # Re-find all elements after clicking
    $allElements = $mainWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
} else {
    Write-Host "Agnes provider button not found"
}

# Search for "Tự động thêm" buttons using byte comparison (from debug output)
# Name bytes for "Tự động thêm": 84,225,187,177,32,196,145,225,187,153,110,103,32,116,104,195,170,109 (18 bytes)
$targetBytes = @(84,225,187,177,32,196,145,225,187,153,110,103,32,116,104,195,170,109)

$autoAddButtons = @()
foreach ($elem in $allElements) {
    if ($elem.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button) {
        $name = $elem.Current.Name
        $enabled = $elem.Current.IsEnabled
        
        # Byte comparison with the observed bytes
        if ($name) {
            $nameBytes = [System.Text.Encoding]::UTF8.GetBytes($name)
            $match = $false
            for ($i = 0; $i -le $nameBytes.Length - $targetBytes.Length; $i++) {
                $found = $true
                for ($j = 0; $j -lt $targetBytes.Length; $j++) {
                    if ($nameBytes[$i + $j] -ne $targetBytes[$j]) {
                        $found = $false
                        break
                    }
                }
                if ($found) { $match = $true; break }
            }
            if ($match) {
                $autoAddButtons += $elem
                Write-Host "Found 'Tự động thêm' button (BYTE MATCH): Name='$name', Enabled=$enabled, AutomationId='$($elem.Current.AutomationId)'"
                
                # Check parent chain for Agnes - dump full chain
                $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($elem)
                Write-Host "  Parent chain:"
                for ($k = 0; $k -lt 15 -and $parent; $k++) {
                    $pname = $parent.Current.Name
                    $pct = $parent.Current.ControlType
                    Write-Host ("    [{0}] Name='{1}', ControlType={2}" -f $k, $pname, $pct)
                    if ($pname -and $pname -like "*Agnes*") {
                        Write-Host "    -> Found Agnes in parent chain!"
                        break
                    }
                    $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($parent)
                }
            }
        }
    }
}
Write-Host "Total 'Tự động thêm' buttons found: $($autoAddButtons.Count)"

# Use the first enabled button
$autoAddButton = $null
if ($autoAddButtons.Count -gt 0) {
    foreach ($btn in $autoAddButtons) {
        if ($btn.Current.IsEnabled) {
            $autoAddButton = $btn
            Write-Host "Using first enabled 'Tự động thêm' button"
            break
        }
    }
}

if (-not $autoAddButton) {
    Write-Host "ERROR: No enabled 'Tự động thêm' button found"
    exit 1
}

# Check IsEnabled
$isEnabled = $autoAddButton.Current.IsEnabled
Write-Host "Button IsEnabled: $isEnabled"

$invokeTime = (Get-Date).ToUniversalTime()
Write-Host "Invoke time (UTC): $invokeTime"

if ($isEnabled -eq $false) {
    Write-Host "ERROR: 'Tự động thêm' button is DISABLED"
    exit 1
}

Write-Host "Button is ENABLED, invoking..."

$invokePattern = $autoAddButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
if ($invokePattern) {
    $invokePattern.Invoke()
    Write-Host "Invoked 'Tự động thêm' button at $invokeTime"
} else {
    Write-Host "ERROR: InvokePattern not available on button"
    exit 1
}

Write-Host ""
Write-Host "=== Monitoring for 240 seconds (checking every 10s) ==="

for ($i = 1; $i -le 24; $i++) {
    Start-Sleep -Seconds 10
    
    $currentTime = (Get-Date).ToUniversalTime()
    Write-Host "--- Check #$i at $currentTime UTC ---"
    
    # Check StatusText
    $statusText = $mainWindow.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "StatusText")
    )
    if ($statusText) {
        $statusPattern = $statusText.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
        if ($statusPattern) {
            $status = $statusPattern.Current.Value
            Write-Host "StatusText: $status"
        } else {
            Write-Host "StatusText Name: $($statusText.Current.Name)"
        }
    } else {
        Write-Host "StatusText element not found"
    }
    
    # Check Chrome processes with remote-debugging-port
    Write-Host "Checking Chrome processes with remote-debugging-port..."
    $chromeProcs = Get-Process -Name "chrome" -ErrorAction SilentlyContinue
    foreach ($proc in $chromeProcs) {
        try {
            $cmdLine = $proc.StartInfo.Arguments
            if (-not $cmdLine) { $cmdLine = "" }
            if ($cmdLine -like "*remote-debugging-port*") {
                Write-Host "  Chrome PID $($proc.Id): $cmdLine"
            }
        } catch {
            # Ignore
        }
    }
    
    # Try to connect to Chrome debug port and list tabs
    try {
        $chromeDebugProcs = Get-Process -Name "chrome" -ErrorAction SilentlyContinue | Where-Object { $_.StartInfo.Arguments -like "*remote-debugging-port*" }
        foreach ($proc in $chromeDebugProcs) {
            if ($proc.StartInfo.Arguments -match 'remote-debugging-port=(\d+)') {
                $port = $matches[1]
                try {
                    $response = Invoke-RestMethod -Uri "http://localhost:$port/json/list" -TimeoutSec 5 -ErrorAction Stop
                    Write-Host "  Chrome debug port $port - tabs:"
                    foreach ($tab in $response) {
                        Write-Host "    - $($tab.url) (title: $($tab.title))"
                    }
                } catch {
                    $errMsg = $_.Exception.Message
                    Write-Host ("  Could not connect to Chrome debug port {0}: {1}" -f $port, $errMsg)
                }
            }
        }
    } catch {
        $errMsg = $_.Exception.Message
        Write-Host ("  Error checking Chrome debug ports: {0}" -f $errMsg)
    }
    
    # Check newest session log for keywords
    $logPath = "E:\GitHub\9router-plus\src\RouterPlus.App\bin\Release\net8.0-windows\logs"
    if (Test-Path $logPath) {
        $logFiles = Get-ChildItem -Path $logPath -Filter "*.log" | Sort-Object LastWriteTime -Descending
        if ($logFiles.Count -gt 0) {
            $newestLog = $logFiles[0]
            Write-Host "Newest log: $($newestLog.Name) (modified: $($newestLog.LastWriteTime))"
            $logContent = Get-Content -Path $newestLog.FullName -Tail 50 -ErrorAction SilentlyContinue
            $keywords = @("AutoGetKey", "Agnes", "ChromeLauncher", "ConnectAgnes", "page_url", "Google", "Popup", "Create", "ApiKey", "POST", "Connection", "errors")
            foreach ($keyword in $keywords) {
                $matches = $logContent | Select-String -Pattern $keyword -CaseSensitive -SimpleMatch
                if ($matches) {
                    foreach ($match in $matches) {
                        Write-Host "  LOG MATCH [$keyword]: $($match.Line)"
                    }
                }
            }
        } else {
            Write-Host "No log files found in $logPath"
        }
    } else {
        Write-Host "Log directory not found: $logPath"
    }
    
    Write-Host ""
}

Write-Host "=== 240 seconds monitoring complete ==="
Write-Host ""

# Check 9Router for new Agnes connection
Write-Host "Checking 9Router dashboard for new Agnes connection..."
try {
    $dashboardUrl = "http://localhost:20128/api/providers"
    $response = Invoke-RestMethod -Uri $dashboardUrl -TimeoutSec 10 -ErrorAction Stop
    Write-Host "Providers API response:"
    $response | ConvertTo-Json -Depth 5 | Write-Host
    
    # Look for Agnes connections
    foreach ($provider in $response) {
        if ($provider.kind -eq "Agnes" -or $provider.name -like "*Agnes*") {
            Write-Host "Found Agnes provider: $($provider | ConvertTo-Json -Depth 5)"
            if ($provider.connections) {
                foreach ($conn in $provider.connections) {
                    Write-Host "  Connection: name=$($conn.name), id=$($conn.id), createdAt=$($conn.createdAt)"
                }
            }
        }
    }
} catch {
    $errMsg = $_.Exception.Message
    Write-Host ("Could not query 9Router dashboard: {0}" -f $errMsg)
}

Write-Host ""
Write-Host "=== Test complete ==="