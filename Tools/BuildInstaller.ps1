param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '0.1.0',
    [string]$BuildPath,
    [string]$OutputPath,
    [string]$CompilerPath,
    [switch]$BootstrapCompiler
)

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $BuildPath) { $BuildPath = Join-Path $projectRoot 'Builds/NeonFrontierDemo' }
if (-not $OutputPath) { $OutputPath = Join-Path $projectRoot 'Builds/Releases' }
$BuildPath = (Resolve-Path -LiteralPath $BuildPath).Path
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
foreach ($required in @('NeonFrontier.exe', 'UnityPlayer.dll', 'NeonFrontier_Data/globalgamemanagers', 'NeonFrontier_Data/Managed/Assembly-CSharp.dll', 'MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $BuildPath $required) -PathType Leaf)) { throw "Incomplete Windows player build: missing $required in $BuildPath" }
}
if ($OutputPath.StartsWith($BuildPath.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or $OutputPath -eq $BuildPath) {
    throw 'Installer output must be outside the source player directory.'
}

$compilerVersion = '6.7.3'
$toolCache = Join-Path $env:LOCALAPPDATA 'NeonFrontier/BuildTools'
$localCompilerDirectory = Join-Path $toolCache ('InnoSetup-' + $compilerVersion)
if (-not $CompilerPath) {
    $candidates = @((Join-Path $localCompilerDirectory 'ISCC.exe'))
    if (${env:ProgramFiles(x86)}) { $candidates += Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe' }
    if ($env:ProgramFiles) { $candidates += Join-Path $env:ProgramFiles 'Inno Setup 6/ISCC.exe' }
    $pathCompiler = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($pathCompiler) { $candidates += $pathCompiler.Source }
    $CompilerPath = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}
if (-not $CompilerPath -and $BootstrapCompiler) {
    New-Item -ItemType Directory -Path $toolCache -Force | Out-Null
    $download = Join-Path $toolCache ('innosetup-' + $compilerVersion + '.exe')
    $expectedHash = '9C73C3BAE7ED48D44112A0F48E66742C00090BDB5BEF71D9D3C056C66E97B732'
    if (-not (Test-Path -LiteralPath $download)) {
        Invoke-WebRequest -Uri 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe' -OutFile $download -UseBasicParsing
    }
    if ((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash -ne $expectedHash) { throw "Compiler download checksum mismatch: $download" }
    $signature = Get-AuthenticodeSignature -LiteralPath $download
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch '(^|, )CN=Pyrsys B\.V\.(,|$)') {
        throw 'Compiler download does not have a valid signature from Pyrsys B.V.'
    }
    $bootstrapLog = Join-Path $toolCache 'compiler-install.log'
    $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CURRENTUSER', '/SP-', '/MERGETASKS=!desktopicon,!associate', ('/DIR="' + $localCompilerDirectory + '"'), ('/LOG="' + $bootstrapLog + '"'))
    $install = Start-Process -FilePath $download -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
    if ($install.ExitCode -ne 0) { throw "Compiler installation failed ($($install.ExitCode)); see $bootstrapLog" }
    $CompilerPath = Join-Path $localCompilerDirectory 'ISCC.exe'
}
if (-not $CompilerPath -or -not (Test-Path -LiteralPath $CompilerPath -PathType Leaf)) {
    throw 'Inno Setup compiler was not found. Install Inno Setup 6.7.3, pass -CompilerPath, or rerun with -BootstrapCompiler.'
}
$CompilerPath = (Resolve-Path -LiteralPath $CompilerPath).Path
New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
& (Join-Path $PSScriptRoot 'Installer/BuildBranding.ps1')
$sourceFiles = Get-ChildItem -LiteralPath $BuildPath -Recurse -File | Where-Object {
    $relative = $_.FullName.Substring($BuildPath.Length + 1)
    $relative -notmatch '(?i)(DoNotShip|BackUpThisFolder|(^|[\\/])logs([\\/]|$)|\.(pdb|mdb|debug|log|tmp|bak|old|dmp)$)'
}
$installerPath = Join-Path $OutputPath "NeonFrontier-$Version-Windows-Setup.exe"
$compilerLog = Join-Path $OutputPath "NeonFrontier-$Version-installer-build.log"
$arguments = @('/Qp', "/DProductVersion=$Version", "/DBuildRoot=$BuildPath", "/DReleaseRoot=$OutputPath", (Join-Path $PSScriptRoot 'Installer/NeonFrontier.iss'))
& $CompilerPath @arguments 2>&1 | Tee-Object -FilePath $compilerLog
if ($LASTEXITCODE -ne 0) { throw "Installer compiler failed ($LASTEXITCODE). See $compilerLog" }
if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) { throw "Compiler did not produce $installerPath" }
$hash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumPath = $installerPath + '.sha256'
[System.IO.File]::WriteAllText($checksumPath, "$hash  $([System.IO.Path]::GetFileName($installerPath))`n", [System.Text.Encoding]::ASCII)
$compilerLibrary = Join-Path (Split-Path -Parent $CompilerPath) 'ISCmplr.dll'
$detectedCompilerVersion = if (Test-Path -LiteralPath $compilerLibrary) { (Get-Item -LiteralPath $compilerLibrary).VersionInfo.FileVersion } else { (Get-Item -LiteralPath $CompilerPath).VersionInfo.FileVersion }
if ($CompilerPath -eq (Join-Path $localCompilerDirectory 'ISCC.exe')) { $detectedCompilerVersion = $compilerVersion }
$manifest = [ordered]@{
    product = 'Neon Frontier'; version = $Version; platform = 'Windows x64'; installer = [System.IO.Path]::GetFileName($installerPath)
    sha256 = $hash; bytes = (Get-Item -LiteralPath $installerPath).Length
    compiler = $detectedCompilerVersion
    compilerSha256 = (Get-FileHash -LiteralPath $CompilerPath -Algorithm SHA256).Hash.ToLowerInvariant()
    payloadFileCount = @($sourceFiles).Count
    payloadBytes = ($sourceFiles | Measure-Object -Property Length -Sum).Sum
    files = @($sourceFiles | Sort-Object FullName | ForEach-Object { [ordered]@{ path = $_.FullName.Substring($BuildPath.Length + 1).Replace('\', '/'); bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() } })
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $OutputPath "NeonFrontier-$Version-installer-manifest.json") -Encoding UTF8
Add-Type -AssemblyName System.IO.Compression
$portablePath = Join-Path $OutputPath "NeonFrontier-$Version-Windows-x64.zip"
$archiveStream = [System.IO.File]::Open($portablePath, [System.IO.FileMode]::Create)
try {
    $archive = [System.IO.Compression.ZipArchive]::new($archiveStream, [System.IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        foreach ($source in ($sourceFiles | Sort-Object FullName)) {
            $relative = $source.FullName.Substring($BuildPath.Length + 1).Replace('\', '/')
            $entry = $archive.CreateEntry(('NeonFrontier/' + $relative), [System.IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = [DateTimeOffset]::new($source.LastWriteTimeUtc)
            $sourceStream = [System.IO.File]::OpenRead($source.FullName)
            try {
                $entryStream = $entry.Open()
                try { $sourceStream.CopyTo($entryStream) } finally { $entryStream.Dispose() }
            } finally { $sourceStream.Dispose() }
        }
    } finally { $archive.Dispose() }
} finally { $archiveStream.Dispose() }
$portableHash = (Get-FileHash -LiteralPath $portablePath -Algorithm SHA256).Hash.ToLowerInvariant()
[System.IO.File]::WriteAllText(($portablePath + '.sha256'), "$portableHash  $([System.IO.Path]::GetFileName($portablePath))`n", [System.Text.Encoding]::ASCII)
Write-Output "Installer: $installerPath"
Write-Output "SHA-256: $hash"
Write-Output "Portable ZIP: $portablePath"
Write-Output "Portable SHA-256: $portableHash"
Write-Output "Payload: $(@($sourceFiles).Count) files, $($manifest.payloadBytes) bytes"
