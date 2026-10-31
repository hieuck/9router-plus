Add-Type -AssemblyName UIAutomationClient
Add-Type -Path "E:\GitHub\9router-plus\tests\RouterPlus.App.E2E\bin\Release\net8.0-windows\FlaUI.Core.dll"
Add-Type -Path "E:\GitHub\9router-plus\tests\RouterPlus.App.E2E\bin\Release\net8.0-windows\FlaUI.UIA3.dll"

$proc = Get-Process -Name "RouterPlus" | Select-Object -First 1
if ($proc) {
    Write-Output "Found RouterPlus process: PID=$($proc.Id)"
    
    $app = [FlaUI.Core.Application]::Attach($proc.Id)
    $automation = New-Object FlaUI.UIA3.UIA3Automation
    $mainWindow = $app.GetMainWindow($automation)
    
    if ($mainWindow -eq $null) {
        Write-Output "Main window not found!"
        return
    }
    
    Write-Output "Main window title: $($mainWindow.Title)"
    
    # Step 1: Find ProfileSearchTextBox and set text
    $searchBox = $mainWindow.FindFirstDescendant($automation.CreateAndCondition($automation.CreatePropertyCondition([FlaUI.Core.Definitions.Property]::AutomationIdProperty, "ProfileSearchTextBox")))
    if ($searchBox -eq $null) {
        Write-Output "ProfileSearchTextBox not found!"
        return
    }
    
    $searchTextBox = $searchBox.AsTextBox()
    if ($searchTextBox -eq $null) {
        Write-Output "ProfileSearchTextBox is not a TextBox!"
        return
    }
    
    Write-Output "Setting profile search text to 'hieuck.adobe2022@gmail.com'"
    $searchTextBox.Text = "hieuck.adobe2022@gmail.com"
    
    Start-Sleep -Seconds 2
    
    # Step 2: Find the profile in the list
    $profileList = $mainWindow.FindFirstDescendant($automation.CreateAndCondition($automation.CreatePropertyCondition([FlaUI.Core.Definitions.Property]::AutomationIdProperty, "ProfileList")))
    if ($profileList -eq $null) {
        Write-Output "ProfileList not found!"
        return
    }
    
    $profileItem = $profileList.FindFirstDescendant($automation.CreateAndCondition($automation.CreatePropertyCondition([FlaUI.Core.Definitions.Property]::NameProperty, "hieuck.adobe2022@gmail.com")))
    if ($profileItem -eq $null) {
        Write-Output "Profile 'hieuck.adobe2022@gmail.com' not found in list!"
        
        # List visible profiles
        $visibleProfiles = $profileList.FindAllDescendants($automation.CreatePropertyCondition([FlaUI.Core.Definitions.Property]::ControlTypeProperty, [FlaUI.Core.Definitions.ControlType]::ListItem))
        Write-Output "Visible profiles:"
        foreach ($p in $visibleProfiles) {
            Write-Output "  - $($p.Name)"
        }
        return
    }
    
    Write-Output "Found profile: $($profileItem.Name)"
    $profileItem.Click()
    Start-Sleep -Seconds 2
    
    # Step 3: Find Agnes provider card
    $agnesCard = $mainWindow.FindFirstDescendant($automation.CreateAndCondition($automation.CreatePropertyCondition([FlaUI.Core.Definitions.Property]::NameProperty, "Agnes AI")))
    
    if ($agnesCard -eq $null) {
        $agnesCard = $mainWindow.FindFirstDescendant($automation.CreateAndCondition($automation.CreatePropertyCondition([FlaUI.Core.Definitions.Property]::NameProperty, "Agnes")))
    }
    
    if ($agnesCard -eq $null) {
        $agnesButton = $mainWindow.FindFirstDescendant($automation.CreateAndCondition(
            $automation.CreatePropertyCondition([FlaUI.Core.Definitions.Property]::ControlTypeProperty, [FlaUI.Core.Definitions.ControlType]::Button),
            $automation.CreatePropertyCondition([FlaUI.Core.Definitions.Property]::NameProperty, "Agnes (5)")
        ))
        
        if ($agnesButton -ne $null) {
            Write-Output "Found Agnes provider button: '$($agnesButton.Name)'"
            $agnesCard = $agnesButton
        } else {
            Write-Output "Agnes provider button not found!"
            return
        }
    }
    
    # Click the Agnes provider button to select Agnes as the active provider
    Write-Output "Clicking Agnes provider button to select it..."
    $agnesCard.Click()
    Start-Sleep -Seconds 2
    
    Write-Output "Found Agnes card: $($agnesCard.Name)"
    
    # Step 4: Find the "Tự động thêm" button at the main window level
    $autoGetKeyButton = $mainWindow.FindFirstDescendant($automation.CreateAndCondition(
        $automation.CreatePropertyCondition([FlaUI.Core.Definitions.Property]::ControlTypeProperty, [FlaUI.Core.Definitions.ControlType]::Button),
        $automation.CreatePropertyCondition([FlaUI.Core.Definitions.Property]::NameProperty, "Tự động thêm")
    ))
    
    if ($autoGetKeyButton -eq $null) {
        $autoGetKeyButton = $mainWindow.FindFirstDescendant($automation.CreateAndCondition(
            $automation.CreatePropertyCondition([FlaUI.Core.Definitions.Property]::ControlTypeProperty, [FlaUI.Core.Definitions.ControlType]::Button),
            $automation.CreatePropertyCondition([FlaUI.Core.Definitions.Property]::NameProperty, "Auto add")
        ))
    }
    
    if ($autoGetKeyButton -eq $null) {
        Write-Output "'Tự động thêm' button not found at main window level!"
        
        # List all buttons at main window level
        $buttons = $mainWindow.FindAllDescendants($automation.CreatePropertyCondition([FlaUI.Core.Definitions.Property]::ControlTypeProperty, [FlaUI.Core.Definitions.ControlType]::Button))
        Write-Output "All Buttons at main window level:"
        foreach ($b in $buttons) {
            if (-not [string]::IsNullOrEmpty($b.Name)) {
                Write-Output "  - Name: '$($b.Name)', Enabled: $($b.IsEnabled), Visible: $(-not $b.IsOffscreen)"
            }
        }
        return
    }
    
    Write-Output "Found 'Tự động thêm' button: Enabled=$($autoGetKeyButton.IsEnabled), Visible=$(-not $autoGetKeyButton.IsOffscreen)"
    
    # Step 5: Invoke the button once
    $startTimeUtc = [DateTime]::UtcNow
    Write-Output "=== INVOKING 'Tự động thêm' AT $($startTimeUtc.ToString('o')) ==="
    
    $autoGetKeyButton.Click()
    
    # Step 6: Monitor for up to 300 seconds
    $timeout = [TimeSpan]::FromSeconds(300)
    $deadline = [DateTime]::UtcNow + $timeout
    $checkInterval = [TimeSpan]::FromSeconds(5)
    
    Write-Output "Monitoring for Agnes key flow completion..."
    Write-Output "Will check: StatusText, session logs, new Agnes connection in 9Router"
    
    $statusTextElement = $mainWindow.FindFirstDescendant($automation.CreateAndCondition($automation.CreatePropertyCondition([FlaUI.Core.Definitions.Property]::AutomationIdProperty, "StatusText")))
    
    $iteration = 0
    while ([DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Seconds 5
        $iteration++
        
        # Check StatusText
        if ($statusTextElement -ne $null) {
            $statusText = $statusTextElement.Name
            if (-not [string]::IsNullOrEmpty($statusText)) {
                Write-Output "[$([DateTime]::UtcNow.ToString('HH:mm:ss'))] StatusText: $statusText"
                
                if ($statusText -match "Agnes|key|thành công|success|thất bại|failed|lỗi|error") {
                    Write-Output "*** KEY STATUS CHANGE: $statusText ***"
                }
            }
        }
        
        # Check if button is still enabled
        $isEnabled = $autoGetKeyButton.IsEnabled
        $isVisible = -not $autoGetKeyButton.IsOffscreen
        Write-Output "[$([DateTime]::UtcNow.ToString('HH:mm:ss'))] Button: Enabled=$isEnabled, Visible=$isVisible"
        
        # Check for new Agnes connection via 9Router API
        try {
            $headers = @{Authorization="Bearer 123456"}
            $response = Invoke-WebRequest -Uri "http://localhost:5000/api/providers" -Headers $headers -Method Get -UseBasicParsing
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