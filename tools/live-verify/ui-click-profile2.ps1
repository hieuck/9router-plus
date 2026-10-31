Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Get-Process -Name 'RouterPlus' | Select-Object -First 1
$hwnd = $proc.MainWindowHandle

$automation = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)

# Find the profile list
$listCond = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "ProfileList")
$profileList = $automation.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $listCond)

if ($profileList) {
    Write-Host "Found list, setting focus..."
    $profileList.SetFocus()
    Start-Sleep -Seconds 1
    
    $items = $profileList.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
    for ($i = 0; $i -lt $items.Count; $i++) {
        $item = $items[$i]
        $itemName = $item.Current.Name
        if ($itemName -like "*hieuck.it*") {
            Write-Host ">>> Found target at index " + $i
            Write-Host "Item bounds: " + $item.Current.BoundingRectangle
            
            # Try invoke
            $invokePattern = $item.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
            if ($invokePattern) {
                $invokePattern.Invoke()
                Write-Host "Invoked"
            } else {
                Write-Host "No invoke pattern, trying SelectionItemPattern"
                $selPattern = $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
                if ($selPattern) {
                    $selPattern.Select()
                    Write-Host "Selected"
                }
            }
            
            # Also try clicking on the ListBoxItem via mouse
            $rect = $item.Current.BoundingRectangle
            $x = [int]($rect.Left + $rect.Width / 2)
            $y = [int]($rect.Top + $rect.Height / 2)
            Write-Host "Clicking at ($x, $y)"
            Add-Type -AssemblyName System.Windows.Forms
            [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point($x, $y)
            Start-Sleep -Milliseconds 200
            # Left click
            $sig = @'
using System;
using System.Runtime.InteropServices;
public class Mouse {
    [DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint cButtons, UIntPtr dwExtraInfo);
    public const uint MOUSEEVENTF_LEFTDOWN = 0x02;
    public const uint MOUSEEVENTF_LEFTUP = 0x04;
    public static void LeftClick() { mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero); mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero); }
}
'@
            Add-Type -TypeDefinition $sig -Language CSharp
            [Mouse]::LeftClick()
            Write-Host "Mouse click sent"
            Start-Sleep -Seconds 2
        }
    }
}

# Check SelectedProfileName again
$selectedCond = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "SelectedProfileName")
$selectedProfile = $automation.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $selectedCond)

if ($selectedProfile) {
    Write-Host "SelectedProfileName: '" + $selectedProfile.Current.Name + "'"
} else {
    Write-Host "SelectedProfileName NOT found"
}

# Also check StatusText
$statusCond = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElementIdentifiers]::AutomationIdProperty, "StatusText")
$statusText = $automation.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $statusCond)

if ($statusText) {
    Write-Host "StatusText: '" + $statusText.Current.Name + "'"
} else {
    Write-Host "StatusText NOT found"
}