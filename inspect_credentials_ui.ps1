Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

function Get-AutomationElement {
    param(
        [System.Windows.Automation.AutomationElement]$Parent,
        [string]$AutomationId,
        [string]$Name
    )

    if ($AutomationId) {
        $condition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
            $AutomationId
        )
    }
    elseif ($Name) {
        $condition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty,
            $Name
        )
    }
    else {
        return $null
    }

    return $Parent.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

Write-Host "=== Finding Main Window ==="
$process = Get-Process -Name "RouterPlus" -ErrorAction Stop
$mainWindow = $null

foreach ($proc in $process) {
    $hwnd = $proc.MainWindowHandle
    if ($hwnd -ne 0) {
        $mainWindow = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
        Write-Host "Found main window: $($mainWindow.Current.Name)"
        break
    }
}

if (-not $mainWindow) {
    Write-Host "ERROR: Could not find main window"
    exit 1
}

Write-Host ""
Write-Host "=== Opening Credentials Manager ==="
$credButton = Get-AutomationElement -Parent $mainWindow -AutomationId "CredentialsManagerButton"

if ($credButton) {
    Write-Host "Found button, invoking..."
    $invokePattern = $credButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $invokePattern.Invoke()
    Start-Sleep -Seconds 1
}

Write-Host ""
Write-Host "=== Finding Credentials Manager Dialog ==="
$credDialog = Get-AutomationElement -Parent $mainWindow -Name "🔐 Credentials Manager"

if (-not $credDialog) {
    Write-Host "ERROR: Could not find Credentials Manager dialog"
    exit 1
}

Write-Host "Found Credentials Manager dialog"

Write-Host ""
Write-Host "=== Searching for ALL buttons in dialog ==="
$buttonCondition = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Button
)
$allButtons = $credDialog.FindAll([System.Windows.Automation.TreeScope]::Descendants, $buttonCondition)

Write-Host "Found $($allButtons.Count) buttons total"
Write-Host ""

$loginRelatedButtons = @()
foreach ($button in $allButtons) {
    $name = $button.Current.Name
    $automationId = $button.Current.AutomationId

    if ($name -match "login|Login|🚀" -or $automationId -match "Login") {
        $loginRelatedButtons += $button
        Write-Host "LOGIN BUTTON: Name='$name' AutomationId='$automationId'"
    }
}

Write-Host ""
Write-Host "=== Summary ==="
Write-Host "Total buttons: $($allButtons.Count)"
Write-Host "Login-related buttons: $($loginRelatedButtons.Count)"

if ($loginRelatedButtons.Count -eq 0) {
    Write-Host ""
    Write-Host "NO LOGIN BUTTONS FOUND IN ANY PROVIDER TAB"
}

Write-Host ""
Write-Host "=== Checking specific tabs ==="

$googleTab = Get-AutomationElement -Parent $credDialog -AutomationId "GoogleAccountsTab"
if ($googleTab) {
    Write-Host "Google tab exists"
}

$codexTab = Get-AutomationElement -Parent $credDialog -AutomationId "CodexTab"
if ($codexTab) {
    Write-Host "Codex tab exists"
}

$kiroTab = Get-AutomationElement -Parent $credDialog -AutomationId "KiroTab"
if ($kiroTab) {
    Write-Host "Kiro tab exists"
}

$githubTab = Get-AutomationElement -Parent $credDialog -AutomationId "GitHubTab"
if ($githubTab) {
    Write-Host "GitHub tab exists"
}

$openrouterTab = Get-AutomationElement -Parent $credDialog -AutomationId "OpenRouterTab"
if ($openrouterTab) {
    Write-Host "OpenRouter tab exists"
}

Write-Host ""
Write-Host "Done"
