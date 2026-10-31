Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName System.Windows.Forms

# Find RouterPlus process
$proc = Get-Process -Name "RouterPlus.App" -ErrorAction SilentlyContinue
if (-not $proc) {
    Write-Host "RouterPlus.App not running"
    exit 1
}

$mainWindow = $proc.MainWindowHandle
if (-not $mainWindow -or $mainWindow -eq 0) {
    Write-Host "No main window handle"
    exit 1
}

$auto = [System.Windows.Automation.AutomationElement]::FromHandle($mainWindow)
if (-not $auto) {
    Write-Host "Could not get AutomationElement from handle"
    exit 1
}

# Find the Agnes card (ItemsControlItem with ProviderCardViewModel)
$cardCondition = [System.Windows.Automation.PropertyCondition]::new(
    [System.Windows.Automation.AutomationElement]::ClassNameProperty,
    "ItemsControlItem"
)
$cards = $auto.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cardCondition)

$agnesCard = $null
foreach ($card in $cards) {
    $name = $card.Current.Name
    $autoId = $card.Current.AutomationId
    if ($name -and $name -like "*Agnes*") {
        $agnesCard = $card
        Write-Host "Found Agnes card: Name='$name', AutoId='$autoId'"
        break
    }
}

if (-not $agnesCard) {
    Write-Host "Agnes card not found"
    exit 1
}

# Now find the "Tự động thêm" button within the Agnes card
$btnCondition = [System.Windows.Automation.PropertyCondition]::new(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Button
)
$buttons = $agnesCard.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCondition)

$targetBtn = $null
foreach ($btn in $buttons) {
    $name = $btn.Current.Name
    $enabled = $btn.Current.IsEnabled
    Write-Host "Button in Agnes card: Name='$name', Enabled=$enabled"
    if ($name -and ($name -like "*Tự*" -or $name -like "*thêm*" -or $name -like "*Auto*")) {
        $targetBtn = $btn
        Write-Host "TARGET BUTTON FOUND: Name='$name', Enabled=$enabled"
        break
    }
}

if (-not $targetBtn) {
    Write-Host "Tự động thêm button not found in Agnes card"
    exit 1
}

# Try to invoke via InvokePattern
$invokePattern = $targetBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
if ($invokePattern) {
    Write-Host "Invoking Tự động thêm button..."
    $invokePattern.Invoke()
    Write-Host "Invoke completed"
} else {
    Write-Host "No InvokePattern available"
    exit 1
}