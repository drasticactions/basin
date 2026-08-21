#!/usr/bin/env pwsh
#
# build-waylonia.ps1 [OPTIONS]
#
# The Windows half of scripts/build-waylonia.sh: publishes apps/Waylonia with
# NativeAOT and zips it into artifacts/, the same folder shape build-release.sh
# stages every other program in. A NativeAOT publish is a native link, so a
# release of Waylonia is one run on each of Linux, macOS and Windows, and this
# is the run a Windows box makes. The shell script needs a POSIX shell, zip and
# lipo that a Windows box has none of, which is why this is a second script
# rather than a flag on that one.
#
# Windows adds nothing to the publish the way macOS adds the universal binary
# and Linux the waylonia.desktop check, so this is the publish, the version
# default and the zip and no more.
#
#   -Version V   version to stamp, default 0.1.0-local.g<commit>
#   -Rid RID     runtime identifier, default the host's
#   -Out DIR     where the zip is written, default artifacts/
#

[CmdletBinding()]
param(
    [string]$Version,
    [string]$Rid,
    [string]$Out
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = 'apps/Waylonia'

function Get-ProgramName([string]$project) {
    $file = Get-ChildItem -Path (Join-Path $root $project) -Filter *.csproj -File | Select-Object -First 1
    if ($file) {
        $match = Select-String -Path $file.FullName -Pattern '<AssemblyName>(.*)</AssemblyName>' | Select-Object -First 1
        if ($match) { return $match.Matches[0].Groups[1].Value }
    }

    return (Split-Path $project -Leaf).ToLowerInvariant()
}

function Get-HostRid {
    $line = dotnet --info | Select-String -Pattern '^\s*RID:\s*(\S+)' | Select-Object -First 1
    if (-not $line) { throw "dotnet --info reported no RID." }
    return $line.Matches[0].Groups[1].Value
}

function Get-LocalVersion {
    $commit = git -C $root rev-parse --short HEAD 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $commit) { $commit = 'unknown' }

    git -C $root diff --quiet HEAD 2>$null | Out-Null
    $dirty = if ($LASTEXITCODE -ne 0) { '.dirty' } else { '' }

    return "0.1.0-local.g$commit$dirty"
}

function Get-ProgramBinary([string]$directory, [string]$name) {
    $exe = Join-Path $directory "$name.exe"
    if (Test-Path $exe) { return $exe }
    return (Join-Path $directory $name)
}

# The ILCompiler targets invoke vswhere.exe bare to find the MSVC linker, so a
# machine with Visual Studio installed but the installer directory off PATH
# fails at the native link with a command-not-found inside the link command.
function Add-VsWhereToPath {
    if (Get-Command vswhere.exe -ErrorAction Ignore) { return }

    $installer = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
    if (Test-Path (Join-Path $installer 'vswhere.exe')) {
        $env:PATH = "$installer;$env:PATH"
        return
    }

    Write-Warning "vswhere.exe is not on PATH and is not at its installed location."
    Write-Warning "         A NativeAOT publish links with MSVC and will fail without it."
    Write-Warning "         Install the Visual Studio C++ build tools, or run this from a developer prompt."
}

Add-VsWhereToPath

$hostRid = Get-HostRid
if (-not $Rid) { $Rid = $hostRid }
if ($Rid -ne $hostRid) {
    Write-Warning "publishing $Rid from $hostRid needs a cross toolchain this does not install."
    Write-Warning "         Build on the target machine when the native link fails."
}

if (-not $Version) { $Version = Get-LocalVersion }
if (-not $Out) { $Out = Join-Path $root 'artifacts' }

$name = Get-ProgramName $project
$folder = "$name-$Version-$Rid"

New-Item -ItemType Directory -Force -Path $Out | Out-Null
$Out = (Resolve-Path $Out).Path
$stage = Join-Path $Out "stage-$name"
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }

try {
    Write-Host "version $Version, rid $Rid"
    Write-Host ''
    Write-Host "publishing $project"

    $destination = Join-Path $stage $folder
    dotnet publish (Join-Path $root $project) -c Release -r $Rid `
        -p:Version=$Version -o $destination --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

    Get-ChildItem -Path $destination -Include *.pdb, *.xml, *.dbg -File -Recurse |
        Remove-Item -Force

    Copy-Item (Join-Path $root 'LICENSE') (Join-Path $destination 'LICENSE')
    $readme = Join-Path $root "$project/README.md"
    if (Test-Path $readme) { Copy-Item $readme (Join-Path $destination 'README.md') }

    # A binary built for another architecture cannot start here, which is not a
    # build failure, so a readback that could not run is a warning rather than
    # the end of a publish that otherwise succeeded.
    $binary = Get-ProgramBinary $destination $name
    $stamped = $null
    try { $stamped = & $binary --version } catch { }

    if (-not $stamped) {
        Write-Warning "$(Split-Path $binary -Leaf) would not start here, so its version was not read back."
    }
    elseif ((($stamped -split '\+')[0]) -ne $Version) {
        Write-Warning "$(Split-Path $binary -Leaf) reports version '$stamped', not '$Version'"
    }

    $zip = Join-Path $Out "$folder.zip"
    if (Test-Path $zip) { Remove-Item -Force $zip }
    Compress-Archive -Path $destination -DestinationPath $zip

    Write-Host ''
    $size = '{0:N1} MB' -f ((Get-Item $zip).Length / 1MB)
    Write-Host "$size  $zip"
    Write-Host ''
}
finally {
    if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
}
