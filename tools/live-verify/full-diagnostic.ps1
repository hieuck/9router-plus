Write-Host "=== LIVE DIAGNOSTIC: Agnes 'Tu lay Key' ==="
Write-Host "UTC Start: $(Get-Date -Format u)"

$proc = Get-Process -Name 'RouterPlus' | Select-Object -First 1
$hwnd = $proc.MainWindowHandle
Write-Host "HWND: $hwnd"

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$automation = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

# Step 1: Search for profile
Write-Host ""
Write-Host "--- Step 1: Search for hieuck.it@gmail.com ---"
$searchCond = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "ProfileSearchTextBox")
$searchBox = $automation.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $searchCond)

if ($searchBox) {
    $valuePattern = $searchBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    if ($valuePattern) {
        $valuePattern.SetValue("hieuck.it@gmail.com")
        Write-Host "Set search text to hieuck.it@gmail.com"
        Start-Sleep -Seconds 2
    }
} else {
    Write-Host "ERROR: Search box NOT found"
    exit 1
}

# Step 2: Find and select the profile
Write-Host ""
Write-Host "--- Step 2: Find and select profile ---"
$listCond = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "ProfileList")
$profileList = $automation.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $listCond)

$selected = $false
if ($profileList) {
    $children = $profileList.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
    Write-Host "Profile list children: $($children.Count)"
    
    for ($i = 0; $i -lt $children.Count; $i++) {
        $item = $children[$i]
        $itemName = $item.Current.Name
        $bounds = $item.Current.BoundingRectangle
        Write-Host ("  [" + $i + "] Name='" + $itemName + "' Bounds=" + $bounds + " Enabled=" + $item.Current.IsEnabled + " Offscreen=" + $item.Current.IsOffscreen)
        
        if ($itemName -like "*hieuck.it*") {
            Write-Host ">>> Target profile found at index " + $i
            $selPattern = $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
            if ($selPattern) {
                $selPattern.Select()
                Write-Host "Selected via SelectionItemPattern"
                $selected = $true
            } else {
                Write-Host "No SelectionItemPattern, trying InvokePattern"
                $invokePattern = $item.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
                if ($invokePattern) {
                    $invokePattern.Invoke()
                    Write-Host "Invoked via InvokePattern"
                    $selected = $true
                } else {
                    Write-Host "No InvokePattern either"
                }
            }
        }
    }
} else {
    Write-Host "ERROR: Profile list NOT found"
    exit 1
}

Start-Sleep -Seconds 2

# Step 3: Verify selection
Write-Host ""
Write-Host "--- Step 3: Verify selection ---"
$selectedCond = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "SelectedProfileName")
$selectedProfile = $automation.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $selectedCond)

if ($selectedProfile) {
    Write-Host ("SelectedProfileName: '" + $selectedProfile.Current.Name + "'")
} else {
    Write-Host "SelectedProfileName NOT found (empty)"
}

$statusCond = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "StatusText")
$statusText = $automation.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $statusCond)

if ($statusText) {
    Write-Host ("StatusText: '" + $statusText.Current.Name + "'")
} else {
    Write-Host "StatusText NOT found"
}

# Step 4: Find Agnes card and "Tu lay Key" button
Write-Host ""
Write-Host "--- Step 4: Find Agnes card and 'Tu lay Key' button ---"
$allDesc = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
Write-Host ("Total elements in window: " + $allDesc.Count)

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
    
    if ($name -like "*Agnes*" -and $controlType -like "*DataItem*") {
        $agnesCard = $el
        Write-Host ("Found Agnes card at index " + $i + ": Name='" + $name + "' Type=" + $controlType)
    }
    
    if ($name -like "*Tu lay*" -and $controlType -eq "ControlType.Button") {
        $tuLayKeyButton = $el
        Write-Host ("Found 'Tu lay Key' button at index " + $i + ": Name='" + $name + "' Type=" + $controlType + " Enabled=" + $el.Current.IsEnabled + " Offscreen=" + $el.Current.IsOffscreen)
    }
}

if (-not $agnesCard) {
    Write-Host "ERROR: Agnes card NOT found"
    for ($i = 0; $i -lt $allDesc.Count; $i++) {
        $el = $allDesc[$i]
        if ($el.Current.Name -like "*Agnes*") {
            Write-Host ("  Agnes-related: Name='" + $el.Current.Name + "' Type='" + $el.Current.ControlType.ProgrammaticName + "' AutoId='" + $el.Current.AutomationId + "'")
        }
    }
}

if (-not $tuLayKeyButton) {
    Write-Host "ERROR: 'Tu lay Key' button NOT found"
    for ($i = 0; $i -lt $allDesc.Count; $i++) {
        $el = $allDesc[$i]
        if ($el.Current.ControlType.ProgrammaticName -eq "ControlType.Button" -and $el.Current.Name) {
            Write-Host ("  Button: Name='" + $el.Current.Name + "' AutoId='" + $el.Current.AutomationId + "' Enabled=" + $el.Current.IsEnabled + " Offscreen=" + $el.Current.IsOffscreen)
        }
    }
}

# Step 5: If button found, check properties and invoke
if ($tuLayKeyButton) {
    Write-Host ""
    Write-Host "--- Step 5: Button properties ---"
    Write-Host ("IsEnabled: " + $tuLayKeyButton.Current.IsEnabled)
    Write-Host ("IsOffscreen: " + $tuLayKeyButton.Current.IsOffscreen)
    Write-Host ("BoundingRectangle: " + $tuLayKeyButton.Current.BoundingRectangle)
    
    $invokePattern = $tuLayKeyButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    if ($invokePattern) {
        Write-Host "InvokePattern: Available"
    } else {
        Write-Host "InvokePattern: NOT Available"
    }
    
    if ($tuLayKeyButton.Current.IsEnabled -and -not $tuLayKeyButton.Current.IsOffscreen) {
        Write-Host ""
        Write-Host "--- Step 6: Invoking 'Tu lay Key' button ---"
        $startTime = Get-Date -Format u
        Write-Host ("UTC Invoke time: " + $startTime)
        
        $invokePattern.Invoke()
        Write-Host "Button invoked"
        
        # Poll status for up to 120s
        Write-Host ""
        Write-Host "--- Step 7: Polling status (max 120s) ---"
        $maxWait = 120
        $interval = 2
        $elapsed = 0
        
        while ($elapsed -lt $maxWait) {
            Start-Sleep -Seconds $interval
            $elapsed += $interval
            
            if ($statusTextEl) {
                $currentStatus = $statusTextEl.Current.Name
                Write-Host ("[" + $elapsed + " s] StatusText: '" + $currentStatus + "'")
                
                if ($currentStatus -like "*loi*" -or $currentStatus -like "*error*" -or $currentStatus -like "*thanh cong*" -or $currentStatus -like "*success*" -or $currentStatus -like "*xong*" -or $currentStatus -like "*done*" -or $currentStatus -like "*Chrome*" -or $currentStatus -like "*mo*") {
                    Write-Host ">>> Status change detected, continuing to monitor..."
                }
            }
            
            if ($tuLayKeyButton) {
                $enabled = $tuLayKeyButton.Current.IsEnabled
                Write-Host ("[" + $elapsed + " s] Button IsEnabled: " + $enabled)
            }
        }
        
        Write-Host ""
        Write-Host ("--- Polling complete (" + $maxWait + " s) ---")
    } else {
        Write-Host ""
        Write-Host "--- Button is DISABLED or OFFSCREEN ---"
        Write-Host ("Reason: IsEnabled=" + $tuLayKeyButton.Current.IsEnabled + ", IsOffscreen=" + $tuLayKeyButton.Current.IsOffscreen)
    }
} else {
    Write-Host ""
    Write-Host "--- Cannot proceed: Button not found ---"
}

# Step 8: Check for Agnes connection via API
Write-Host ""
Write-Host "--- Step 8: Check Agnes connection via API ---"
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