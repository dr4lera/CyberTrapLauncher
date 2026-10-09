param([Parameter(Mandatory=$true)][string]$ScheduleIPath)
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
Push-Location $project
try {
    dotnet build ./guest/CyberTrap.Launcher.Guest.csproj -c Release "-p:ScheduleIPath=$ScheduleIPath" --nologo
    if ($LASTEXITCODE) { throw 'Guest helper build failed.' }
    New-Item -ItemType Directory -Force ./payload | Out-Null
    Copy-Item ./guest/bin/Release/net6.0/CyberTrap.Launcher.Guest.dll ./payload/CyberTrap.Launcher.Guest.dll -Force
    (Get-FileHash ./payload/CyberTrap.Launcher.Guest.dll -Algorithm SHA256).Hash | Set-Content ./payload/CyberTrap.Launcher.Guest.dll.sha256 -Encoding ascii
    $outDir = Join-Path $project ('publish/' + [guid]::NewGuid().ToString('N'))
    dotnet publish ./CyberTrapLauncher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -o $outDir --nologo
    if ($LASTEXITCODE) { throw 'Launcher publish failed.' }
    Copy-Item ./README.md,./LICENSE,./CHANGELOG.md $outDir -Force
    New-Item -ItemType Directory -Force ./dist | Out-Null
    $archive = Join-Path $project 'dist/CyberTrapLauncher-v0.1.0-win-x64.zip'
    Compress-Archive -Path "$outDir/*" -DestinationPath $archive -Force
    $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
    "$hash  $(Split-Path -Leaf $archive)" | Set-Content "$archive.sha256" -Encoding ascii
    Write-Output "Packaged $archive"
    Write-Output "Executable: $outDir/CyberTrapLauncher.exe"
} finally { Pop-Location }
