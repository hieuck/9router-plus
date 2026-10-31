Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

$process = Get-Process -Name "RouterPlus" | Select-Object -First 1
$mainWindow = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)

# Clear search and retype with focus
$searchTextBox = $mainWindow.FindFirst(
    [System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "ProfileSearchTextBox")
)

if ($searchTextBox) {
    # Focus first
    $focusPattern = $searchTextBox.GetCurrentPattern([System.Windows.Automation.FocusPattern]::Pattern)
    if ($focusPattern) {
        $focusPattern.SetFocus()
        Write-Host "Focused search box"
    }
    
    $valuePattern = $searchTextBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    if ($valuePattern) {
        $valuePattern.SetValue("")
        Start-Sleep -Milliseconds 500
        $valuePattern.SetValue("hieuck.adobe2022@gmail.com")
        Write-Host "Retyped email in search box"
    }
}

Start-Sleep -Seconds 2

# Check elements again
$allElements = $mainWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)

Write-Host "=== Elements with 'adobe2022' in Name AFTER retype ===" 
foreach ($elem in $allElements) {
    if ($elem.Current.Name -and $elem.Current.Name -like "*adobe2022*") {
        Write-Host "  Name='$($elem.Current.Name)', ControlType=$($elem.Current.ControlType), AutomationId='$($elem.Current.AutomationId)', IsEnabled=$($elem.Current.IsEnabled)"
    }
}

# Check ProfileList children
$profileList = $mainWindow.FindFirst(
    [System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "ProfileList")
)

if ($profileList) {
    Write-Host "=== ProfileList children after retype ==="
    $children = $profileList.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($elem in $children) {
        $name = $elem.Current.Name
        if ($name -and $name.Length -gt 0) {
            Write-Host "  Name='$($elem.Current.Name)', ControlType=$($elem.Current.ControlType), AutomationId='$($elem.Current.AutomationId)', IsEnabled=$($elem.Current.IsEnabled)"
        }
    }
    
    Write-Host "=== ProfileList descendants after retype ==="
    $descendants = $profileList.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($elem in $descendants) {
        $name = $elem.Current.Name
        if ($name -and $name.Length -gt 0) {
            Write-Host "  Name='$($elem.Current.Name)', ControlType=$($elem.Current.ControlType), AutomationId='$($elem.Current.AutomationId)', IsEnabled=$($elem.Current.IsEnabled)"
        }
    }
}

# Try pressing Enter
Add-Type -MemberDefinition @"
    [DllImport("user32.dll")]
    public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, uint dwExtraInfo);
"@ -Name "Win32" -Namespace "Native"

Start-Sleep -Milliseconds 500
[Native.Win32]::keybd_event(0x0D, 0, 0, 0)
[Native.Win32]::keybd_event(0x0D, 0, 2, 0)
Write-Host "Pressed Enter"

Start-Sleep -Seconds 3

# Check again after Enter
$allElements2 = $mainWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)

Write-Host "=== Elements with 'adobe2022' in Name AFTER Enter ===" 
foreach ($elem in $allElements2) {
    if ($elem.Current.Name -and $elem.Current.Name -like "*adobe2022*") {
        Write-Host "  Name='$($elem.Current.Name)', ControlType=$($elem.Current.ControlType), AutomationId='$($elem.Current.AutomationId)', IsEnabled=$($elem.Current.IsEnabled)"
    }
}

if ($profileList) {
    Write-Host "=== ProfileList children after Enter ==="
    $children = $profileList.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($elem in $children) {
        $name = $elem.Current.Name
        if ($name -and $name.Length -gt 0) {
            Write-Host "  Name='$($elem.Current.Name)', ControlType=$($elem.Current.ControlType), AutomationId='$($elem.Current.AutomationId)', IsEnabled=$($elem.Current.IsEnabled)"
        }
    }
}