Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName System.Windows.Forms

$proc = Get-Process -Name "RouterPlus" | Select-Object -First 1
if ($proc) {
    Write-Output "Found RouterPlus process: PID=$($proc.Id)"
    
    $element = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
    if ($element -eq $null) {
        Write-Output "Main window element not found!"
        return
    }
    
    Write-Output "Main window title: $($element.Current.Name)"
    
    # Step 1: Find ProfileSearchTextBox and set text
    $searchBox = $element.FindFirst([System.Windows.Automation.TreeScope]::Descendants, 
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "ProfileSearchTextBox"))
    if ($searchBox -eq $null) {
        Write-Output "ProfileSearchTextBox not found!"
        return
    }
    
    $textPattern = $searchBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    if ($textPattern -eq $null) {
        Write-Output "ProfileSearchTextBox does not support ValuePattern!"
        return
    }
    
    Write-Output "Setting profile search text to 'hieuck.adobe2022@gmail.com'"
    $textPattern.SetValue("hieuck.adobe2022@gmail.com")
    
    Start-Sleep -Seconds 2
    
    # Step 2: Find the profile in the list
    $profileList = $element.FindFirst([System.Windows.Automation.TreeScope]::Descendants, 
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "ProfileList"))
    if ($profileList -eq $null) {
        Write-Output "ProfileList not found!"
        return
    }
    
    $profileItem = $profileList.FindFirst([System.Windows.Automation.TreeScope]::Descendants, 
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::NameProperty, "hieuck.adobe2022@gmail.com"))
    if ($profileItem -eq $null) {
        Write-Output "Profile 'hieuck.adobe2022@gmail.com' not found in list!"
        
        # List visible profiles
        $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
        $visibleProfiles = $profileList.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
        Write-Output "Visible profiles (count: $($visibleProfiles.Count)):"
        for ($i = 0; $i -lt $visibleProfiles.Count; $i++) {
            Write-Output "  - $($visibleProfiles[$i].Current.Name)"
        }
        return
    }
    
    Write-Output "Found profile: $($profileItem.Current.Name)"
    # Try to click via mouse
    $clickablePoint = $profileItem.GetClickablePoint()
    if ($clickablePoint) {
        [System.Windows.Forms.Cursor]::Position = [System.Drawing.Point]::new([int]$clickablePoint.X, [int]$clickablePoint.Y)
        [System.Windows.Forms.SendKeys]::SendWait("{CLICK}")
    } else {
        Write-Output "Could not get clickable point for profile item"
    }
    Start-Sleep -Seconds 2
    
    # Step 3: Find and click Agnes provider button "Agnes (5)"
    $condition = [System.Windows.Automation.AndCondition]::new(
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button),
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::NameProperty, "Agnes (5)")
    )
    $agnesButton = $element.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    
    if ($agnesButton -eq $null) {
        Write-Output "Agnes (5) button not found!"
        return
    }
    
    Write-Output "Found Agnes provider button: '$($agnesButton.Current.Name)'"
    Write-Output "Clicking Agnes provider button to select it..."
    
    $clickablePoint = $agnesButton.GetClickablePoint()
    if ($clickablePoint) {
        [System.Windows.Forms.Cursor]::Position = [System.Drawing.Point]::new([int]$clickablePoint.X, [int]$clickablePoint.Y)
        [System.Windows.Forms.SendKeys]::SendWait("{CLICK}")
    } else {
        Write-Output "Could not get clickable point for Agnes button"
    }
    Start-Sleep -Seconds 2
    
    # Step 4: Find the "Tự động thêm" button at the main window level
    $condition = [System.Windows.Automation.AndCondition]::new(
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button),
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::NameProperty, "Tự động thêm")
    )
    $autoGetKeyButton = $element.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    
    if ($autoGetKeyButton -eq $null) {
        $condition = [System.Windows.Automation.AndCondition]::new(
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button),
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::NameProperty, "Auto add")
        )
        $autoGetKeyButton = $element.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    }
    
    if ($autoGetKeyButton -eq $null) {
        Write-Output "'Tự động thêm' button not found at main window level!"
        
        # List all buttons at main window level
        $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
        $buttons = $element.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
        Write-Output "All Buttons at main window level (count: $($buttons.Count)):"
        for ($i = 0; $i -lt $buttons.Count; $i++) {
            $b = $buttons[$i]
            if (-not [string]::IsNullOrEmpty($b.Current.Name)) {
                Write-Output "  - Name: '$($b.Current.Name)', Enabled: $($b.Current.IsEnabled), Visible: $(-not $b.Current.IsOffscreen)"
            }
        }
        return
    }
    
    Write-Output "Found 'Tự động thêm' button: Enabled=$($autoGetKeyButton.Current.IsEnabled), Visible=$(-not $autoGetKeyButton.Current.IsOffscreen)"
    
    if (-not $autoGetKeyButton.Current.IsEnabled) {
        Write-Output "Button is DISABLED - cannot invoke"
        return
    }
    
    # Step 5: Invoke the button once
    $startTimeUtc = [DateTime]::UtcNow
    Write-Output "=== INVOKING 'Tự động thêm' AT $($startTimeUtc.ToString('o')) ==="
    
    $clickablePoint = $autoGetKeyButton.GetClickablePoint()
    if ($clickablePoint) {
        [System.Windows.Forms.Cursor]::Position = [System.Drawing.Point]::new([int]$clickablePoint.X, [int]$clickablePoint.Y)
        [System.Windows.Forms.SendKeys]::SendWait("{CLICK}")
    } else {
        Write-Output "Could not get clickable point for 'Tự động thêm' button"
        return
    }
    
    # Step 6: Monitor for up to 300 seconds
    $timeout = [TimeSpan]::FromSeconds(300)
    $deadline = [DateTime]::UtcNow + $timeout
    $checkInterval = 5
    
    Write-Output "Monitoring for Agnes key flow completion..."
    Write-Output "Will check: StatusText, session logs, new Agnes connection in 9Router"
    
    $statusTextElement = $element.FindFirst([System.Windows.Automation.TreeScope]::Descendants, 
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "StatusText"))
    
    $iteration = 0
    while ([DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Seconds $checkInterval
        $iteration++
        
        # Re-find the button and status text each iteration (they may be recreated)
        $statusTextElement = $element.FindFirst([System.Windows.Automation.TreeScope]::Descendants, 
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "StatusText"))
        
        $condition = [System.Windows.Automation.AndCondition]::new(
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button),
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::NameProperty, "Tự động thêm")
        )
        $autoGetKeyButton = $element.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        
        # Check StatusText
        if ($statusTextElement -ne $null) {
            $statusText = $statusTextElement.Current.Name
            if (-not [string]::IsNullOrEmpty($statusText)) {
                Write-Output "[$([DateTime]::UtcNow.ToString('HH:mm:ss'))] StatusText: $statusText"
                
                if ($statusText -match "Agnes|key|thành công|success|thất bại|failed|lỗi|error") {
                    Write-Output "*** KEY STATUS CHANGE: $statusText ***"
                }
            }
        }
        
        # Check if button is still enabled
        if ($autoGetKeyButton -ne $null) {
            $isEnabled = $autoGetKeyButton.Current.IsEnabled
            $isVisible = -not $autoGetKeyButton.Current.IsOffscreen
            Write-Output "[$([DateTime]::UtcNow.ToString('HH:mm:ss'))] Button: Enabled=$isEnabled, Visible=$isVisible"
        } else {
            Write-Output "[$([DateTime]::UtcNow.ToString('HH:mm:ss'))] Button: Not found (may be hidden during operation)"
        }
        
        # Check for new Agnes connection via 9Router API
        try {
            $headers = @{Authorization="Bearer 123456"}
            $response = Invoke-WebRequest -Uri "http://localhost:5000/api/providers" -Headers $headers -Method Get -UseBasicParsing -TimeoutSec 5
            if ($response.StatusCode -eq 200) {
                $providers = $response.Content | ConvertFrom-Json
                $agnesProvider = $providers | Where-Object { $_.kind -eq "Agnes" }
                if ($agnesProvider) {
                    $connections = $agnesProvider.connections
                    if ($connections) {
                        foreach ($conn in $connections) {
                            if ($conn.email -eq "hieuck.adobe2022@gmail.com") {
                                Write-Output "*** NEW AGNES CONNECTION FOUND FOR hieuck.adobe2022@gmail.com ***"
                                Write-Output "Connection details: $($conn | ConvertTo-Json -Depth 5)"
                            }
                        }
                    }
                }
            }
        } catch {
            Write-Output "API check failed: $($_.Exception.Message)"
        }
    }
    
    Write-Output "=== MONITORING TIMEOUT (300s) ==="
    Write-Output "Test completed. Check session logs and 9Router for new Agnes connection."
} else {
    Write-Output "RouterPlus process not found!"
}