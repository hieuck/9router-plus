Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Mouse {
    [DllImport("user32.dll")]
    public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint cButtons, uint dwExtraInfo);
    public const uint MOUSEEVENTF_LEFTDOWN = 0x02;
    public const uint MOUSEEVENTF_LEFTUP = 0x04;
    public static void Click() {
        mouse_event(MOUSEEVENTF_LEFTDOWN | MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
    }
}
"@

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
    
    # Wait longer for filtering to complete
    Start-Sleep -Seconds 5
    
    # Step 2: Find the profile list
    $profileList = $element.FindFirst([System.Windows.Automation.TreeScope]::Descendants, 
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "ProfileList"))
    if ($profileList -eq $null) {
        Write-Output "ProfileList not found!"
        return
    }
    
    # Get the ListItem (there's only one visible after filtering)
    $children = $profileList.FindAll([System.Windows.Automation.TreeScope]::Children, 
        [System.Windows.Automation.Condition]::TrueCondition)
    
    Write-Output "Initial children count: $($children.Count)"
    
    if ($children.Count -eq 0) {
        Write-Output "No ListItems found initially, waiting more..."
        Start-Sleep -Seconds 5
        
        $children = $profileList.FindAll([System.Windows.Automation.TreeScope]::Children, 
            [System.Windows.Automation.Condition]::TrueCondition)
        Write-Output "Children after 5s more: $($children.Count)"
    }
    
    if ($children.Count -eq 0) {
        Write-Output "Still no ListItems! Checking descendants..."
        $descendants = $profileList.FindAll([System.Windows.Automation.TreeScope]::Descendants, 
            [System.Windows.Automation.Condition]::TrueCondition)
        Write-Output "Descendants count: $($descendants.Count)"
        for ($i = 0; $i -lt $descendants.Count; $i++) {
            $d = $descendants[$i]
            if (-not [string]::IsNullOrEmpty($d.Current.Name)) {
                Write-Output "  [$i] Name: '$($d.Current.Name)', ControlType: $($d.Current.ControlType.ProgrammaticName)"
            }
        }
        return
    }
    
    $listItem = $children[0]
    Write-Output "Found ListItem: $($listItem.Current.Name)"
    
    # Verify this is the correct profile by checking text content
    $textElements = $listItem.FindAll([System.Windows.Automation.TreeScope]::Descendants, 
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text))
    $foundProfile = $false
    foreach ($textEl in $textElements) {
        if ($textEl.Current.Name -eq "hieuck.adobe2022@gmail.com") {
            $foundProfile = $true
            break
        }
    }
    
    if (-not $foundProfile) {
        Write-Output "ListItem does not contain the target profile!"
        return
    }
    
    Write-Output "Confirmed: ListItem contains 'hieuck.adobe2022@gmail.com'"
    
    # Select the profile using SelectionItemPattern
    $selectionItemPattern = $listItem.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
    if ($selectionItemPattern) {
        $selectionItemPattern.Select()
        Write-Output "Profile selected via SelectionItemPattern"
    } else {
        Write-Output "ListItem does not support SelectionItemPattern!"
        return
    }
    
    Start-Sleep -Seconds 3
    
    # Verify selection
    $selectionPattern = $profileList.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern)
    if ($selectionPattern) {
        $selectedItems = $selectionPattern.Current.GetSelection()
        Write-Output "Selection verified: $($selectedItems.Count) item(s) selected"
    }
    
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
    
    $agnesInvoke = $agnesButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    if ($agnesInvoke) {
        $agnesInvoke.Invoke()
        Write-Output "Agnes button invoked via InvokePattern"
    } else {
        Write-Output "Agnes button does not support InvokePattern, trying mouse click..."
        $clickablePoint = $agnesButton.GetClickablePoint()
        if ($clickablePoint) {
            [System.Windows.Forms.Cursor]::Position = [System.Drawing.Point]::new([int]$clickablePoint.X, [int]$clickablePoint.Y)
            [Mouse]::Click()
            Write-Output "Agnes button clicked via mouse event"
        }
    }
    Start-Sleep -Seconds 3
    
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
        Write-Output "Button is DISABLED - waiting longer for UI to update..."
        Start-Sleep -Seconds 5
        
        # Re-find and check again
        $autoGetKeyButton = $element.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($autoGetKeyButton -ne $null) {
            Write-Output "Re-check: Enabled=$($autoGetKeyButton.Current.IsEnabled), Visible=$(-not $autoGetKeyButton.Current.IsOffscreen)"
        }
        
        if (-not $autoGetKeyButton.Current.IsEnabled) {
            Write-Output "Button still DISABLED after waiting - checking StatusText..."
            
            $statusTextElement = $element.FindFirst([System.Windows.Automation.TreeScope]::Descendants, 
                [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "StatusText"))
            if ($statusTextElement -ne $null) {
                Write-Output "StatusText: $($statusTextElement.Current.Name)"
            }
            
            return
        }
    }
    
    # Step 5: Invoke the button once
    $startTimeUtc = [DateTime]::UtcNow
    Write-Output "=== INVOKING 'Tự động thêm' AT $($startTimeUtc.ToString('o')) ==="
    
    $invokePattern = $autoGetKeyButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    if ($invokePattern) {
        $invokePattern.Invoke()
        Write-Output "Button invoked via InvokePattern"
    } else {
        Write-Output "Button does not support InvokePattern, trying mouse click..."
        $clickablePoint = $autoGetKeyButton.GetClickablePoint()
        if ($clickablePoint) {
            [System.Windows.Forms.Cursor]::Position = [System.Drawing.Point]::new([int]$clickablePoint.X, [int]$clickablePoint.Y)
            [Mouse]::Click()
            Write-Output "Button clicked via mouse event"
        } else {
            Write-Output "Could not get clickable point for 'Tự động thêm' button"
            return
        }
    }
    
    # Step 6: Monitor for up to 300 seconds
    $timeout = [TimeSpan]::FromSeconds(300)
    $deadline = [DateTime]::UtcNow + $timeout
    $checkInterval = 5
    
    Write-Output "Monitoring for Agnes key flow completion..."
    Write-Output "Will check: StatusText, session logs, new Agnes connection in 9Router"
    
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
        
        # Also check Chrome CDP ports
        try {
            $cdpResponse = Invoke-WebRequest -Uri "http://127.0.0.1:9222/json" -Method Get -UseBasicParsing -TimeoutSec 3
            if ($cdpResponse.StatusCode -eq 200) {
                $tabs = $cdpResponse.Content | ConvertFrom-Json
                $agnesTabs = $tabs | Where-Object { $_.url -like "*agnes-ai.com*" -or $_.url -like "*accounts.google.com*" }
                if ($agnesTabs) {
                    Write-Output "*** CHROME TABS FOUND ***"
                    foreach ($tab in $agnesTabs) {
                        Write-Output "  Tab: url=$($tab.url), title=$($tab.title)"
                    }
                }
            }
        } catch {
            # Ignore CDP errors
        }
    }
    
    Write-Output "=== MONITORING TIMEOUT (300s) ==="
    Write-Output "Test completed. Check session logs and 9Router for new Agnes connection."
} else {
    Write-Output "RouterPlus process not found!"
}