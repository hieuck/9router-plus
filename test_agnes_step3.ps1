Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

$process = Get-Process -Name "RouterPlus" | Select-Object -First 1
if (-not $process) {
    Write-Host "RouterPlus not found"
    exit 1
}

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
    $pattern.SetValue("hieuck.adobe2022@gmail.com")
    Write-Host "Typed email in search box"
} else {
    Write-Host "ValuePattern not available on search box"
}

Start-Sleep -Seconds 2

# Find ALL elements and dump them to understand the tree
$allElements = $mainWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)

# First, find the Agnes provider button (the tab/button that says "Agnes (8)")
$agnesProviderBtn = $null
foreach ($elem in $allElements) {
    if ($elem.Current.Name -eq "Agnes (8)" -and $elem.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button) {
        $agnesProviderBtn = $elem
        Write-Host "Found Agnes provider button: $($elem.Current.Name)"
        break
    }
}

if ($agnesProviderBtn) {
    # Click the Agnes provider button to show its card
    $invokePattern = $agnesProviderBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    if ($invokePattern) {
        $invokePattern.Invoke()
        Write-Host "Clicked Agnes provider button"
        Start-Sleep -Seconds 2
    }
}

# Re-find all elements after clicking
$allElements = $mainWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)

# Now look for "Tự động thêm" buttons (with Vietnamese encoding)
$autoAddButtons = @()
foreach ($elem in $allElements) {
    $name = $elem.Current.Name
    if (($name -eq "Tự động thêm" -or $name -like "*Tự động thêm*" -or $name -like "*Tự động*thêm*") -and $elem.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button) {
        $autoAddButtons += $elem
    }
}

Write-Host "Found $($autoAddButtons.Count) 'Tự động thêm' buttons"

# Also search for "Auto add" (English)
foreach ($elem in $allElements) {
    if ($elem.Current.Name -eq "Auto add" -and $elem.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button) {
        $autoAddButtons += $elem
    }
}

Write-Host "Total 'Tự động thêm' / 'Auto add' buttons: $($autoAddButtons.Count)"

foreach ($btn in $autoAddButtons) {
    Write-Host "  Button: Name='$($btn.Current.Name)', AutomationId='$($btn.Current.AutomationId)', Enabled=$($btn.Current.IsEnabled)"
    
    # Check parent for Agnes
    $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($btn)
    $foundAgnes = $false
    for ($i = 0; $i -lt 15 -and $parent; $i++) {
        if ($parent.Current.Name -like "*Agnes*") {
            $foundAgnes = $true
            Write-Host "    Found in Agnes container: $($parent.Current.Name)"
            break
        }
        $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($parent)
    }
    
    if ($foundAgnes -and $btn.Current.IsEnabled) {
        $invokePattern = $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        if ($invokePattern) {
            Write-Host "Invoking 'Tự động thêm' button..."
            $invokePattern.Invoke()
            Write-Host "Button invoked successfully"
        }
        break
    }
}

# Also dump all buttons for debugging
Write-Host "All buttons found:"
foreach ($elem in $allElements) {
    if ($elem.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button) {
        Write-Host "  Button: Name='$($elem.Current.Name)', AutomationId='$($elem.Current.AutomationId)', Enabled=$($elem.Current.IsEnabled)"
    }
}

Write-Host "Done"