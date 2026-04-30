$ErrorActionPreference = "Stop"

Write-Host "Running Go producer tests/build checks..."
Push-Location "services/producer-go"
go test ./...
go build ./...
Pop-Location

Write-Host "Running Python consumer unit tests..."
Push-Location "services/consumer-py"
python -m py_compile consumer.py
python -m unittest -v test_consumer.py
Pop-Location

Write-Host "Running .NET consumer tests/build checks..."
Push-Location "services/consumer-dotnet"
dotnet test ".\\Tests\\consumer-dotnet.Tests.csproj" --verbosity minimal
Pop-Location

Write-Host "Generating coverage reports..."
./scripts/test-coverage.ps1

Write-Host "Running integration tests against Python consumer..."
./scripts/test-integration.ps1 -Profile python-consumer

Write-Host "Running integration tests against .NET consumer..."
./scripts/test-integration.ps1 -Profile dotnet-consumer

Write-Host "All test stages completed."
