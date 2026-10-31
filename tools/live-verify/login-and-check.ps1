# Login to get cookies
$loginUri = "http://localhost:20128/api/auth/login"
$payload = '{"password":"123456"}'

$response = Invoke-RestMethod -Uri $loginUri -Method Post -Body $payload -ContentType "application/json" -SessionVariable session -ErrorAction Stop
Write-Host "Login response:"
Write-Host ($response | ConvertTo-Json -Depth 5)

# Now call /api/providers with the session
$providersResponse = Invoke-RestMethod -Uri "http://localhost:20128/api/providers" -WebSession $session -Method Get -ErrorAction Stop
Write-Host ""
Write-Host "Providers response:"
Write-Host ($providersResponse | ConvertTo-Json -Depth 5)