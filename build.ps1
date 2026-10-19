param([switch]$SkipRestore)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.build-env'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$target = Join-Path $PSScriptRoot '发布/GPT小龙'
if (-not $SkipRestore) {
    & dotnet restore (Join-Path $PSScriptRoot 'src/DragonPet.csproj') --configfile (Join-Path $PSScriptRoot 'NuGet.Config')
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed' }
}
if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'assets/gpt-dragon.ico'))) {
    & dotnet build (Join-Path $PSScriptRoot 'src/DragonPet.csproj') -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Asset bootstrap build failed' }
    $assetInfo = [System.Diagnostics.ProcessStartInfo]::new((Join-Path $PSScriptRoot 'src/bin/Release/net10.0-windows/GPTPet.exe'))
    $assetInfo.UseShellExecute = $false
    $assetInfo.CreateNoWindow = $true
    $assetInfo.ArgumentList.Add('--prepare-assets')
    $assetInfo.ArgumentList.Add((Join-Path $PSScriptRoot 'assets'))
    $assetJob = [System.Diagnostics.Process]::Start($assetInfo)
    $assetJob.WaitForExit()
    if ($assetJob.ExitCode -ne 0) { throw 'Asset generation failed' }
}
& dotnet publish (Join-Path $PSScriptRoot 'src/DragonPet.csproj') -c Release --self-contained false -o $target --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
$test = Start-Process -FilePath (Join-Path $target 'GPTPet.exe') -ArgumentList '--self-test' -Wait -PassThru -WindowStyle Hidden
Get-Content -LiteralPath (Join-Path $target 'test-results.txt')
if ($test.ExitCode -ne 0) { throw 'Self-tests failed' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $target
Write-Output "Built: $target/GPTPet.exe"
