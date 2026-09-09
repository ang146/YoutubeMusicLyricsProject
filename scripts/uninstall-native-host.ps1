$ErrorActionPreference = 'Stop'

$hostName = 'com.lyricsdisplayer.nativehost'
$registryPath = "HKCU:\Software\Mozilla\NativeMessagingHosts\$hostName"
$manifestPath = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) "LyricsDisplayer\NativeHost\$hostName.json"

if (Test-Path -LiteralPath $registryPath) {
    Remove-Item -LiteralPath $registryPath -Recurse -Force
}

if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
    Remove-Item -LiteralPath $manifestPath -Force
}

Write-Host "Unregistered $hostName from Firefox."
