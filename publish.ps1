param(
    [ValidateSet('win-x64','win-arm64','win-x86')][string]$Runtime = 'win-x64',
    [switch]$FrameworkDependent
)
$ErrorActionPreference = 'Stop'
$sdk = Get-Command dotnet -ErrorAction SilentlyContinue
$localSdk = Join-Path $env:LOCALAPPDATA 'BatteryTrayBuild\dotnet\dotnet.exe'
if (Test-Path -LiteralPath $localSdk) { $dotnet = $localSdk }
elseif ($sdk) { $dotnet = $sdk.Source }
else { throw 'Instale o SDK .NET 10.' }
$project = Join-Path $PSScriptRoot 'src\BatteryTrayMonitor\BatteryTrayMonitor.csproj'
$suffix = if ($FrameworkDependent) { 'framework-dependent' } else { 'standalone' }
$output = Join-Path $PSScriptRoot "artifacts\$Runtime-$suffix"
$selfContained = if ($FrameworkDependent) { 'false' } else { 'true' }
& $dotnet publish $project -c Release -r $Runtime --self-contained $selfContained -p:PublishSingleFile=true "-p:EnableCompressionInSingleFile=$selfContained" -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $output
if ($LASTEXITCODE -ne 0) { throw 'Falha na publicação.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $output
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'THIRD-PARTY-NOTICES.md') -Destination $output
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'licenses') -Destination $output -Recurse -Force
Write-Host "Publicação concluída: $output"
