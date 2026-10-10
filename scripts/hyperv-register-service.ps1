#Requires -Version 5.1
param(
    [Parameter(Mandatory = $true)]
    [ValidateRange(1, 4294967294)]
    [uint32] $Port,

    [switch] $Remove,

    [string] $Name
)

$ErrorActionPreference = 'Stop'

$identity = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $identity.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "writing HKLM needs an elevated PowerShell; run this again as Administrator"
    exit 1
}

$service = '{0:x8}-facb-11e6-bd58-64006a7986d3' -f $Port
$root = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Virtualization\GuestCommunicationServices'
$key = Join-Path $root $service
$braced = Join-Path $root "{$service}"
if (Test-Path $braced) {
    Remove-Item -Path $braced -Recurse
    Write-Output "removed ${braced}: Hyper-V never matches a braced key"
}

if ($Remove) {
    if (Test-Path $key) {
        Remove-Item -Path $key -Recurse
        Write-Output "removed $key"
    } else {
        Write-Output "no key for vsock:$Port at $key"
    }
    exit 0
}

if (-not $Name) {
    $Name = "basin waypipe $Port"
}

if (-not (Test-Path $root)) {
    New-Item -Path $root -Force | Out-Null
}

if (-not (Test-Path $key)) {
    New-Item -Path $key | Out-Null
}

New-ItemProperty -Path $key -Name ElementName -Value $Name -PropertyType String -Force | Out-Null
Write-Output "registered vsock:$Port as ServiceId $service ($Name)"
Write-Output "bind it with --hyperv-vm children, or --hyperv-vm GUID from (Get-VM NAME).Id for one guest; neither needs elevation, and the VM needs no restart"
Write-Output "in the guest: sudo modprobe hv_sock; waypipe --vsock -s $Port server -- foot"
