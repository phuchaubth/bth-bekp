<#
.SYNOPSIS
  Builds the IIS deployment package: ASP.NET Core API with the React app in wwwroot (one IIS site).
.PARAMETER Output
  Folder to publish to (copy its content to the IIS site physical path).
.PARAMETER BasePath
  URL path of the site: "/" for a dedicated IIS site, "/bekp/" for a sub-application.
.EXAMPLE
  ./scripts/publish-iis.ps1 -Output C:\deploy\bekp -BasePath /bekp/
#>
param(
  [string]$Output = "artifacts/iis",
  [string]$BasePath = "/",
  [string]$Configuration = "Release"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not $BasePath.StartsWith("/")) { $BasePath = "/$BasePath" }
if (-not $BasePath.EndsWith("/")) { $BasePath = "$BasePath/" }
if (-not [System.IO.Path]::IsPathRooted($Output)) { $Output = Join-Path $root $Output }

Write-Host "== Frontend (base path $BasePath)"
Push-Location (Join-Path $root "frontend")
try {
  $env:VITE_BASE_PATH = $BasePath
  npm ci
  if ($LASTEXITCODE -ne 0) { throw "npm ci failed" }
  npm run build
  if ($LASTEXITCODE -ne 0) { throw "frontend build failed" }
} finally {
  Remove-Item Env:VITE_BASE_PATH -ErrorAction SilentlyContinue
  Pop-Location
}

Write-Host "== Backend"
dotnet publish (Join-Path $root "backend/BEKP.Api/BEKP.Api.csproj") -c $Configuration -o $Output
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$wwwroot = Join-Path $Output "wwwroot"
New-Item -ItemType Directory -Force -Path $wwwroot | Out-Null
Copy-Item -Path (Join-Path $root "frontend/dist/*") -Destination $wwwroot -Recurse -Force
New-Item -ItemType Directory -Force -Path (Join-Path $Output "logs") | Out-Null

Write-Host "IIS package ready: $Output"
