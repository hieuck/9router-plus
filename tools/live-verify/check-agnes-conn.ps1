$response = Invoke-RestMethod -Uri "http://localhost:20128/api/auth/login" -Method Post -Body '{"password":"123456"}' -ContentType "application/json" -SessionVariable session
$providers = Invoke-RestMethod -Uri "http://localhost:20128/api/providers" -WebSession $session -Method Get
$agnesConns = $providers.connections | Where-Object { $_.provider -eq "Agnes" -and $_.name -eq "hieuck.it@gmail.com" }
if ($agnesConns) {
    Write-Host "Agnes connection found for hieuck.it@gmail.com:"
    $agnesConns | ForEach-Object {
        Write-Host ("  Id: " + $_.id)
        Write-Host ("  Name: " + $_.name)
        Write-Host ("  CreatedAt: " + $_.createdAt)
        Write-Host ("  IsActive: " + $_.isActive)
        Write-Host ("  TestStatus: " + $_.testStatus)
        Write-Host ("  ExpiresAt: " + $_.expiresAt)
        Write-Host ("  ErrorCode: " + $_.errorCode)
        Write-Host ("  LastError: " + $_.lastError)
    }
} else {
    Write-Host "No Agnes connection found for hieuck.it@gmail.com"
}