param(
  [string]$Message = "hello-from-poc"
)

Write-Host "Publishing message: $Message"
$body = @{
  message = $Message
  eventType = "demo.message"
} | ConvertTo-Json

Invoke-RestMethod `
  -Uri "http://localhost:8080/publish" `
  -Method Post `
  -Body $body `
  -ContentType "application/json"

Write-Host "If successful, check consumer logs:"
Write-Host "docker compose -f infra/docker-compose.yml logs consumer-py --tail 50"
