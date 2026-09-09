param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'

$hostName = 'com.lyricsdisplayer.nativehost'
$extensionId = 'lyrics-displayer@example.com'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$hostExecutable = Join-Path $repositoryRoot "windows\LyricsDisplayer.NativeHost\bin\$Configuration\net10.0\LyricsDisplayer.NativeHost.exe"

if (-not (Test-Path -LiteralPath $hostExecutable -PathType Leaf)) {
    throw "NativeHost executable was not found at '$hostExecutable'. Build windows\LyricsDisplayer.slnx using the $Configuration configuration first."
}

$registrationDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'LyricsDisplayer\NativeHost'
$manifestPath = Join-Path $registrationDirectory "$hostName.json"
New-Item -ItemType Directory -Path $registrationDirectory -Force | Out-Null

$manifest = [ordered]@{
    name = $hostName
    description = 'Lyrics Displayer Firefox Native Messaging bridge'
    path = $hostExecutable
    type = 'stdio'
    allowed_extensions = @($extensionId)
}
$manifestJson = $manifest | ConvertTo-Json -Depth 4
$utf8WithoutBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($manifestPath, $manifestJson, $utf8WithoutBom)

$registryPath = "HKCU:\Software\Mozilla\NativeMessagingHosts\$hostName"
New-Item -Path $registryPath -Force | Out-Null
Set-Item -Path $registryPath -Value $manifestPath

Write-Host "Registered $hostName for Firefox."
Write-Host "Manifest: $manifestPath"
Write-Host "Executable: $hostExecutable"
