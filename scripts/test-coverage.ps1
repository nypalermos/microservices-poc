$ErrorActionPreference = "Stop"

$root = Get-Location
$coverageRoot = Join-Path $root "coverage"
$goCoverageDir = Join-Path $coverageRoot "go"
$pythonCoverageDir = Join-Path $coverageRoot "python"
$dotnetCoverageDir = Join-Path $coverageRoot "dotnet"

New-Item -ItemType Directory -Force -Path $goCoverageDir, $pythonCoverageDir, $dotnetCoverageDir | Out-Null

Write-Host "Generating Go coverage..."
Push-Location "services/producer-go"
go test ./... -coverprofile "$goCoverageDir/coverage.out"
go tool cover -func="$goCoverageDir/coverage.out" > "$goCoverageDir/summary.txt"
Pop-Location

Write-Host "Generating Python coverage..."
Push-Location "services/consumer-py"
python -m pip install coverage
python -m coverage run -m unittest test_consumer.py
python -m coverage xml -o "$pythonCoverageDir/coverage.xml"
python -m coverage report > "$pythonCoverageDir/summary.txt"
Pop-Location

Write-Host "Generating .NET coverage..."
Push-Location "services/consumer-dotnet"
dotnet test ".\\Tests\\consumer-dotnet.Tests.csproj" `
  --collect:"XPlat Code Coverage" `
  --results-directory "$dotnetCoverageDir" `
  --verbosity minimal
Pop-Location

Write-Host "Coverage artifacts written to:"
Write-Host "- $goCoverageDir"
Write-Host "- $pythonCoverageDir"
Write-Host "- $dotnetCoverageDir"
