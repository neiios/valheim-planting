<#
.SYNOPSIS
    Installs the BulkPlanting Valheim mod on Windows, plus the BepInEx mod loader if needed.

.DESCRIPTION
    Finds Valheim through Steam, installs BepInExPack_Valheim (pinned version, checksum
    verified) unless BepInEx is already there, and copies BulkPlanting.dll into
    BepInEx\plugins\BulkPlanting. Uses BulkPlanting.dll from the script's folder if present,
    otherwise downloads the latest release from GitHub.

.EXAMPLE
    .\install.ps1
.EXAMPLE
    .\install.ps1 -ValheimDir "D:\SteamLibrary\steamapps\common\Valheim"
.EXAMPLE
    .\install.ps1 -Uninstall
#>
[CmdletBinding()]
param(
    [string]$ValheimDir = $env:VALHEIM_DIR,
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
# Invoke-WebRequest is extremely slow with the progress bar in Windows PowerShell 5.1.
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$BepInExVersion = '5.4.2351'
$BepInExSha256 = 'BCE631497976A93977CEB08E166712E6C31D15244956F89F17DF092A9B62E29F'
$BepInExUrl = "https://thunderstore.io/package/download/denikson/BepInExPack_Valheim/$BepInExVersion/"
$ModUrl = 'https://github.com/neiios/valheim-planting/releases/latest/download/BulkPlanting.dll'

function Test-ValheimDir([string]$Dir) {
    return [bool]$Dir -and (Test-Path -LiteralPath (Join-Path $Dir 'valheim.exe'))
}

function Get-SteamLibraries {
    $roots = @()
    foreach ($key in 'HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam', 'HKLM:\SOFTWARE\Valve\Steam') {
        try {
            $item = Get-ItemProperty -Path $key -ErrorAction Stop
            foreach ($name in 'SteamPath', 'InstallPath') {
                if ($item.$name) { $roots += ($item.$name -replace '/', '\') }
            }
        } catch { }
    }
    if (${env:ProgramFiles(x86)}) { $roots += (Join-Path ${env:ProgramFiles(x86)} 'Steam') }

    $libraries = @()
    foreach ($root in ($roots | Select-Object -Unique)) {
        $libraries += $root
        $vdf = [IO.Path]::Combine($root, 'steamapps', 'libraryfolders.vdf')
        if (Test-Path -LiteralPath $vdf) {
            foreach ($match in [regex]::Matches((Get-Content -LiteralPath $vdf -Raw), '"path"\s+"([^"]+)"')) {
                $libraries += ($match.Groups[1].Value -replace '\\\\', '\')
            }
        }
    }
    return $libraries | Select-Object -Unique
}

function Find-Valheim {
    if ($ValheimDir) {
        if (Test-ValheimDir $ValheimDir) { return (Resolve-Path -LiteralPath $ValheimDir).Path }
        throw "valheim.exe was not found in '$ValheimDir'."
    }
    foreach ($library in Get-SteamLibraries) {
        $dir = [IO.Path]::Combine($library, 'steamapps', 'common', 'Valheim')
        if (Test-ValheimDir $dir) { return $dir }
    }
    Write-Host "Couldn't find Valheim automatically." -ForegroundColor Yellow
    Write-Host 'In Steam: right-click Valheim > Manage > Browse local files, then copy that folder path.'
    while ($true) {
        $dir = (Read-Host 'Paste the Valheim folder path').Trim().Trim('"')
        if (Test-ValheimDir $dir) { return $dir }
        Write-Host "valheim.exe isn't in '$dir'. Try again." -ForegroundColor Red
    }
}

function Install-BepInEx([string]$Game) {
    if (Test-Path -LiteralPath ([IO.Path]::Combine($Game, 'BepInEx', 'core', 'BepInEx.dll'))) {
        Write-Host 'BepInEx is already installed, keeping it.'
        return
    }
    Write-Host "Installing BepInExPack_Valheim $BepInExVersion..."
    $temp = Join-Path ([IO.Path]::GetTempPath()) ('BulkPlanting-' + [guid]::NewGuid())
    New-Item -ItemType Directory -Path $temp | Out-Null
    try {
        $zip = Join-Path $temp 'BepInExPack_Valheim.zip'
        Invoke-WebRequest -Uri $BepInExUrl -OutFile $zip -UseBasicParsing
        $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
        if ($hash -ne $BepInExSha256) {
            throw "The BepInEx download doesn't match the expected checksum (got $hash). Nothing was installed."
        }
        $extracted = Join-Path $temp 'pack'
        Expand-Archive -LiteralPath $zip -DestinationPath $extracted
        Copy-Item -Path ([IO.Path]::Combine($extracted, 'BepInExPack_Valheim', '*')) -Destination $Game -Recurse -Force
    } finally {
        Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Install-Mod([string]$Game) {
    $plugins = [IO.Path]::Combine($Game, 'BepInEx', 'plugins', 'BulkPlanting')
    New-Item -ItemType Directory -Force -Path $plugins | Out-Null
    $target = Join-Path $plugins 'BulkPlanting.dll'
    $local = $null
    if ($PSScriptRoot) { $local = Join-Path $PSScriptRoot 'BulkPlanting.dll' }
    if ($local -and (Test-Path -LiteralPath $local)) {
        Write-Host 'Installing BulkPlanting...'
        Copy-Item -LiteralPath $local -Destination $target -Force
    } else {
        Write-Host 'Downloading the latest BulkPlanting release...'
        Invoke-WebRequest -Uri $ModUrl -OutFile $target -UseBasicParsing
    }
}

try {
    if (Get-Process -Name 'valheim' -ErrorAction SilentlyContinue) {
        throw 'Valheim is running. Close it, then run the installer again.'
    }
    $game = Find-Valheim
    Write-Host "Valheim found: $game"

    if ($Uninstall) {
        Remove-Item -LiteralPath ([IO.Path]::Combine($game, 'BepInEx', 'plugins', 'BulkPlanting')) -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host ''
        Write-Host 'BulkPlanting removed.' -ForegroundColor Green
        Write-Host 'BepInEx was left in place since other mods may use it. To remove it as well, delete'
        Write-Host 'winhttp.dll, doorstop_config.ini, .doorstop_version, changelog.txt, doorstop_libs,'
        Write-Host 'start_game_bepinex.sh, start_server_bepinex.sh and the BepInEx folder from the Valheim folder.'
        return
    }

    Install-BepInEx $game
    Install-Mod $game

    Write-Host ''
    Write-Host 'Done! Start Valheim from Steam as usual.' -ForegroundColor Green
    Write-Host 'Equip the Cultivator, pick a seed, and press N to toggle bulk planting.'
} catch {
    Write-Host ''
    Write-Host "Installation failed: $($_.Exception.Message)" -ForegroundColor Red
}
