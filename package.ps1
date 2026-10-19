param([switch]$SkipRestore)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.build-env'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.build-tools/nuget-packages'
$project = Join-Path $PSScriptRoot 'src/DragonPet.csproj'
[xml]$projectXml = Get-Content -LiteralPath $project
$version = [string]$projectXml.Project.PropertyGroup.Version
$rid = 'win-x64'
$name = "GPT-DragonGirl-Pet-v$version-$rid"
$run = Join-Path $PSScriptRoot ('artifacts/package-' + [Guid]::NewGuid().ToString('N'))
$candidate = Join-Path $run 'validation'
$stage = Join-Path $run $name
New-Item -ItemType Directory -Path $run, $stage -Force | Out-Null
if (-not $SkipRestore) {
    & dotnet restore $project -r $rid --configfile (Join-Path $PSScriptRoot 'NuGet.Config') --source 'https://api.nuget.org/v3/index.json' -p:SelfContained=true
    if ($LASTEXITCODE -ne 0) { throw 'Runtime restore failed' }
}
& dotnet publish $project -c Release -r $rid --self-contained true --no-restore -o $candidate -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw 'Self-contained build failed' }
# Test a disposable candidate; never package the data created by the self-tests.
$test = Start-Process -FilePath (Join-Path $candidate 'GPTPet.exe') -ArgumentList '--self-test' -Wait -PassThru -WindowStyle Hidden
$report = Join-Path $candidate 'test-results.txt'
Get-Content -LiteralPath $report | Select-Object -Last 1
if ($test.ExitCode -ne 0) { throw "Self-tests failed; see $report" }
foreach ($item in (Get-ChildItem -LiteralPath $candidate -File)) {
    if ($item.Name -ne 'test-results.txt' -and $item.Extension -notin @('.pdb', '.lnk')) {
        Copy-Item -LiteralPath $item.FullName -Destination $stage
    }
}
foreach ($folder in @('assets', '图片', 'config')) { Copy-Item -LiteralPath (Join-Path $candidate $folder) -Destination $stage -Recurse }
foreach ($document in @('README.md', 'THIRD-PARTY-NOTICES.txt')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $document) -Destination $stage }
# NuGet's runtime license files are package metadata, so dotnet publish does not copy them automatically.
$licenseFolder = Join-Path $stage 'licenses'
foreach ($runtimePackage in @('microsoft.netcore.app.runtime.win-x64', 'microsoft.windowsdesktop.app.runtime.win-x64')) {
    $packageRoot = Join-Path $env:NUGET_PACKAGES $runtimePackage
    foreach ($notice in (Get-ChildItem -LiteralPath $packageRoot -Recurse -File | Where-Object { $_.Name -match '^(LICENSE|THIRD-PARTY-NOTICES)(\.txt)?$' })) {
        $destination = Join-Path $licenseFolder $runtimePackage
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        Copy-Item -LiteralPath $notice.FullName -Destination $destination
    }
}
$quickStart = @'
GPT 小龙桌宠 · Windows x64 分享版

1. 将整个压缩包解压到可写文件夹，双击 GPTPet.exe。
2. 无需安装 .NET、Python 或 Node.js；不要只复制 exe。
3. 点小龙脚下的「设置」，可创建当前电脑的桌面快捷方式。
4. 默认专注 25 分钟 / 休息 5 分钟，额度提醒线为剩余 20%，均可修改和关闭。
5. 桌宠、表情、音效、番茄钟与时间彩蛋可离线使用。
6. Codex 额度功能需你自己安装并登录 Codex；没有 Codex 也能正常养小龙。
7. 设置和记录会在本文件夹的 data 中生成，发行包中不包含任何人的登录凭据。

更新时先退出旧版。将旧版 data 文件夹复制到新版目录可保留设置；移动位置后请重新创建快捷方式和自启动项。
项目：https://github.com/Yang-yy-05/GPT-DragonGirl-Pet
'@
[IO.File]::WriteAllText((Join-Path $stage '使用说明.txt'), $quickStart, [Text.UTF8Encoding]::new($true))
foreach ($required in @('GPTPet.exe', 'coreclr.dll', 'hostfxr.dll', 'PresentationFramework.dll', 'assets/rubber-duck.wav', 'assets/fonts/OFL-ZCOOLKuaiLe.txt')) {
    if (-not (Test-Path -LiteralPath (Join-Path $stage $required))) { throw "Missing packaged dependency: $required" }
}
$config = Get-Content -LiteralPath (Join-Path $stage 'GPTPet.runtimeconfig.json') -Raw | ConvertFrom-Json
if ($config.runtimeOptions.framework -or $config.runtimeOptions.frameworks -or -not $config.runtimeOptions.includedFrameworks) { throw 'Package still requires an installed .NET framework' }
$forbidden = Get-ChildItem -LiteralPath $stage -Recurse -Force | Where-Object { $_.Name -in @('data', 'settings.json', 'companion.json', 'errors.log', 'quota-check.json', 'test-results.txt') -or $_.Extension -in @('.lnk', '.pdb') }
if ($forbidden) { throw 'Unexpected personal or development files in package' }
$zip = Join-Path $run ($name + '.zip')
Compress-Archive -LiteralPath $stage -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $run ($name + '.sha256')), "$hash  $name.zip`n", [Text.Encoding]::ASCII)
[pscustomobject]@{ Version = $version; Archive = $zip; Sha256 = $hash; Folder = $stage; ValidationReport = $report } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'artifacts/latest-package.json') -Encoding utf8
Write-Output "Share package: $zip"
Write-Output "SHA-256: $hash"
