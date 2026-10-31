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

# Clear and type email then press Enter
$pattern = $searchTextBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
if ($pattern) {
    $pattern.SetValue("")
    Start-Sleep -Milliseconds 500
    $pattern.SetValue("hieuck.adobe2022@gmail.com")
    Write-Host "Typed email in search box"
} else {
    Write-Host "ValuePattern not available on search box"
}

# Press Enter to trigger search
Add-Type -MemberDefinition @"
    [DllImport("user32.dll")]
    public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, uint dwExtraInfo);
"@ -Name "Win32" -Namespace "Native"

Start-Sleep -Milliseconds 500
[Native.Win32]::keybd_event(0x0D, 0, 0, 0)
[Native.Win32]::keybd_event(0x0D, 0, 2, 0)
Write-Host "Pressed Enter"
Start-Sleep -Seconds 3

# Dump ALL elements to find the profile result
$allElements = $mainWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)

Write-Host "=== Elements containing 'adobe2022' ==="
foreach ($elem in $allElements) {
    $name = $elem.Current.Name
    if ($name -and $name -like "*adobe2022*") {
        Write-Host ("  Name='{0}', ControlType={1}, AutomationId='{2}', IsEnabled={3}'" -f $name, $elem.Current.ControlType, $elem.Current.AutomationId, $elem.Current.IsEnabled)
    }
}

Write-Host "=== Elements containing 'hieuck.adobe2022' ==="
foreach ($elem in $allElements) {
    $name = $elem.Current.Name
    if ($name -and $name -like "*hieuck.adobe2022*") {
        Write-Host ("  Name='{0}', ControlType={1}, AutomationId='{2}', IsEnabled={3}'" -f $name, $elem.Current.ControlType, $elem.Current.AutomationId, $elem.Current.IsEnabled)
    }
}

# Check the ProfileList container more deeply
$profileList = $mainWindow.FindFirst(
    [System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "ProfileList")
)

if ($profileList) {
    Write-Host "=== ProfileList descendants ==="
    $descendants = $profileList.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($elem in $descendants) {
        $name = $elem.Current.Name
        if ($name -and $name.Length -gt 0) {
            Write-Host ("  Name='{0}', ControlType={1}, AutomationId='{2}'" -f $name, $elem.Current.ControlType, $elem.Current.AutomationId)
        }
    }
}

Write-Host "Done"