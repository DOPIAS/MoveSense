[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '0.2.1',
    [switch]$Installer,
    [switch]$Offline,
    [switch]$InProcessTests
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    function Invoke-Dotnet([string[]]$Arguments) {
        & dotnet @Arguments
        if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE" }
    }
    $restore = @()
    if ($Offline) { $restore += @('--ignore-failed-sources', '-p:NuGetAudit=false') }
    Invoke-Dotnet (@('restore', '-m:1') + $restore)
    Invoke-Dotnet @('build', '-c', 'Release', '--no-restore', '-m:1', ('-p:Version=' + $Version))
    if ($InProcessTests) {
        Invoke-Dotnet @('run', '--project', 'tests/MoveSense.Core.Tests', '-c', 'Release', '--no-build')
    } else {
        Invoke-Dotnet @('test', '-c', 'Release', '--no-build', '--no-restore', '-m:1', '--logger', 'trx')
    }
    $portable = Join-Path $root "artifacts/portable-$Version"
    if (Test-Path $portable) { throw 'This portable version already exists. Choose a new version or archive that build first.' }
    Invoke-Dotnet (@('publish', 'src/MoveSense.Desktop', '-c', 'Release', '-r', 'win-x64',
        '--self-contained', 'true', '-o', $portable, '-p:DebugType=None', '-p:DebugSymbols=false',
        ('-p:Version=' + $Version)) + $restore)
    Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $portable 'LICENSE-MoveSense.txt')
    $cache = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget/packages' }
    $coreRuntime = Get-ChildItem (Join-Path $cache 'microsoft.netcore.app.runtime.win-x64') -Directory |
        Where-Object { $_.Name -match '^8\.0\.\d+$' } | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    $desktopRuntime = Get-ChildItem (Join-Path $cache 'microsoft.windowsdesktop.app.runtime.win-x64') -Directory |
        Where-Object { $_.Name -match '^8\.0\.\d+$' } | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    if (!$coreRuntime -or !$desktopRuntime) { throw 'Runtime license files are missing.' }
    Copy-Item -LiteralPath (Join-Path $coreRuntime.FullName 'LICENSE.TXT') -Destination (Join-Path $portable 'LICENSE-dotnet.txt')
    Copy-Item -LiteralPath (Join-Path $coreRuntime.FullName 'THIRD-PARTY-NOTICES.TXT') -Destination (Join-Path $portable 'THIRD-PARTY-NOTICES-dotnet.txt')
    Copy-Item -LiteralPath (Join-Path $desktopRuntime.FullName 'LICENSE') -Destination (Join-Path $portable 'LICENSE-WindowsDesktop.txt')
    $smoke = Join-Path $root 'artifacts/smoke-portable'
    New-Item -ItemType Directory -Force -Path $smoke | Out-Null
    $process = Start-Process -FilePath (Join-Path $portable 'MoveSense.exe') -ArgumentList @('--smoke-test', ('"' + $smoke + '"')) -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(45000)) { throw 'Portable launch check timed out.' }
    if ($process.ExitCode -ne 0) { throw "Portable check failed: $($process.ExitCode)" }
    $result = Get-Content -Raw -LiteralPath (Join-Path $smoke 'result.json') | ConvertFrom-Json
    if (!$result.success -or !$result.sqlite -or $result.undo -ne 'Undone') { throw 'Portable move/recovery verification failed.' }
    $zip = Join-Path $root "artifacts/MoveSense-Portable-$Version-win-x64.zip"
    if (Test-Path $zip) { throw 'The versioned archive already exists.' }
    Compress-Archive -Path (Join-Path $portable '*') -DestinationPath $zip -CompressionLevel Optimal
    $assets = @($zip)
    if ($Installer) {
        $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
        $iscc = if ($command) { $command.Source } else { Join-Path ([Environment]::GetEnvironmentVariable('ProgramFiles(x86)')) 'Inno Setup 6/ISCC.exe' }
        if (!(Test-Path $iscc)) { throw 'Inno Setup 6 is required for the installer. The portable ZIP is available.' }
        & $iscc "/DMyAppVersion=$Version" "/DMyBuildSource=$portable" (Join-Path $root 'installer/MoveSense.iss')
        if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
        $assets += Join-Path $root "artifacts/MoveSense-Setup-$Version-win-x64.exe"
    }
    $assets | ForEach-Object {
        $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $_).Hash.ToLowerInvariant()
        "$hash  $([IO.Path]::GetFileName($_))"
    } | Set-Content -Encoding ascii -LiteralPath (Join-Path $root 'artifacts/SHA256SUMS.txt')
    Write-Output 'Validated release files:'
    $assets | ForEach-Object { Write-Output $_ }
} finally { Pop-Location }


