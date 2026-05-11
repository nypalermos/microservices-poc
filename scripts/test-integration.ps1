param(
  [ValidateSet("python-consumer", "dotnet-consumer")]
  [string]$Profile = "python-consumer",
  [int]$StartupTimeoutSeconds = 120
)

$ErrorActionPreference = "Stop"

$composeFile = "infra/docker-compose.yml"
$consumerService = if ($Profile -eq "python-consumer") { "consumer-py" } else { "consumer-dotnet" }
$testMessage = "integration-test-$Profile-$(Get-Date -Format 'yyyyMMddHHmmss')"

function Wait-ForProducerHealth {
  param([int]$TimeoutSeconds)
  $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
  while ((Get-Date) -lt $deadline) {
    try {
      $resp = Invoke-RestMethod -Uri "http://localhost:8080/healthz" -Method Get -TimeoutSec 3
      if ($resp -eq "ok") {
        return
      }
    } catch {
      Start-Sleep -Seconds 2
    }
  }
  throw "Producer did not become healthy within $TimeoutSeconds seconds."
}

function Assert-ConsumerReceivedMessage {
  param(
    [string]$ServiceName,
    [string]$ExpectedText,
    [int]$TimeoutSeconds = 60
  )
  $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
  while ((Get-Date) -lt $deadline) {
    $logs = docker compose -f $composeFile logs $ServiceName --tail 200 2>$null
    if ($logs -match [Regex]::Escape($ExpectedText)) {
      return
    }
    Start-Sleep -Seconds 2
  }
  throw "Consumer logs did not contain expected message: $ExpectedText"
}

function Assert-KafkaTopicContainsText {
  param(
    [string]$ExpectedText,
    [int]$TimeoutSeconds = 120
  )
  $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
  while ((Get-Date) -lt $deadline) {
    $out = docker compose -f $composeFile exec -T kafka kafka-console-consumer `
      --bootstrap-server localhost:9092 `
      --topic poc.consumed `
      --from-beginning `
      --max-messages 200 `
      --timeout-ms 20000 2>$null
    if ($out -match [Regex]::Escape($ExpectedText)) {
      return
    }
    Start-Sleep -Seconds 3
  }
  throw "Kafka topic poc.consumed did not contain expected text: $ExpectedText"
}

try {
  Write-Host "Cleaning any existing stack containers..."
  docker compose -f $composeFile --profile python-consumer --profile dotnet-consumer down --remove-orphans

  Write-Host "Starting stack for profile: $Profile"
  docker compose -f $composeFile --profile $Profile up --build -d

  Write-Host "Waiting for producer health..."
  Wait-ForProducerHealth -TimeoutSeconds $StartupTimeoutSeconds

  Write-Host "Publishing test message..."
  $body = @{
    message = $testMessage
    eventType = "integration.test"
  } | ConvertTo-Json

  $response = Invoke-RestMethod `
    -Uri "http://localhost:8080/publish" `
    -Method Post `
    -Body $body `
    -ContentType "application/json"

  if ($response.status -ne "published") {
    throw "Publish endpoint returned unexpected status: $($response.status)"
  }

  Write-Host "Asserting consumer processed the message..."
  Assert-ConsumerReceivedMessage -ServiceName $consumerService -ExpectedText $testMessage

  Write-Host "Asserting Kafka consumed event..."
  Assert-KafkaTopicContainsText -ExpectedText $testMessage

  Write-Host "Integration test passed for profile: $Profile"
} finally {
  Write-Host "Stopping stack..."
  docker compose -f $composeFile --profile python-consumer --profile dotnet-consumer down --remove-orphans
}
