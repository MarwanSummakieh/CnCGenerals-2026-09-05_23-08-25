param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '0.1.0'
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$releaseRoot = Join-Path $projectRoot 'Builds/Releases'
$installer = Join-Path $releaseRoot "NeonFrontier-$Version-Windows-Setup.exe"
$manifestPath = Join-Path $releaseRoot "NeonFrontier-$Version-installer-manifest.json"
$portable = Join-Path $releaseRoot "NeonFrontier-$Version-Windows-x64.zip"
$runId = [Guid]::NewGuid().ToString('N')
$installRoot = Join-Path $projectRoot ('Builds/InstallerVerification/' + $runId)
$resultRoot = Join-Path $projectRoot ('Logs/InstallerVerification/' + $runId)
$registryPath = 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Uninstall/{02CA9730-42CE-4B72-B278-F1158B6B3301}_is1'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Neon Frontier/Neon Frontier.lnk'
if (Test-Path -LiteralPath $registryPath) { throw 'Neon Frontier is already installed for this user. This isolated test will not replace an existing installation.' }
if (Test-Path -LiteralPath $shortcut) { throw 'An existing Neon Frontier Start Menu shortcut would be replaced. Leave it intact and use a separate Windows test account.' }
if (Test-Path -LiteralPath $installRoot) { throw 'The fresh test installation directory unexpectedly exists.' }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ((Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash -ne $manifest.sha256) { throw 'Installer hash does not match its payload manifest.' }
New-Item -ItemType Directory -Force -Path $resultRoot | Out-Null
$checks = [Collections.Generic.List[object]]::new()
$game = $null
$script:activeSetup = $null
$failure = $null
$started = [DateTime]::UtcNow.ToString('O')

function Assert-Result([string]$Name, [bool]$Passed, [string]$Detail) {
    $checks.Add([ordered]@{ name = $Name; passed = $Passed; detail = $Detail })
    if (-not $Passed) { throw "$Name failed: $Detail" }
    Write-Output "PASS $Name"
}

function Run-Setup([string]$LogName) {
    $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/TASKS=', ('/DIR="' + $installRoot + '"'), ('/LOG="' + (Join-Path $resultRoot $LogName) + '"'))
    $process = Start-Process -FilePath $installer -ArgumentList $arguments -PassThru -WindowStyle Hidden
    $script:activeSetup = $process
    if (-not $process.WaitForExit(120000)) { throw "Setup exceeded its two-minute test budget; process $($process.Id) is still active. Cleanup will not uninstall while it runs." }
    $script:activeSetup = $null
    return $process.ExitCode
}

try {
    Assert-Result 'Silent current-user installation' ((Run-Setup 'install.log') -eq 0) 'Setup must finish successfully without interaction.'
    Assert-Result 'Windows uninstall registration' (Test-Path -LiteralPath $registryPath) 'Current-user uninstall entry exists.'
    $registration = Get-ItemProperty -LiteralPath $registryPath
    Assert-Result 'Registered version' ($registration.DisplayVersion -eq $Version) 'Uninstall entry matches this release.'
    Assert-Result 'Registered installation directory' ($registration.InstallLocation.TrimEnd('\') -eq $installRoot.TrimEnd('\')) 'Uninstaller owns only the isolated test directory.'
    Assert-Result 'Start Menu shortcut created' (Test-Path -LiteralPath $shortcut) 'Default installation includes a launch shortcut.'
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($shortcut)
    Assert-Result 'Shortcut launches the installed game' ($link.TargetPath -eq (Join-Path $installRoot 'NeonFrontier.exe')) 'Shortcut points to this installation.'
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($link)
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)

    foreach ($file in $manifest.files) {
        $installed = [IO.Path]::GetFullPath((Join-Path $installRoot $file.path))
        if (-not $installed.StartsWith($installRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Payload manifest contains a path outside the installation.' }
        if (-not (Test-Path -LiteralPath $installed -PathType Leaf) -or (Get-FileHash -LiteralPath $installed -Algorithm SHA256).Hash -ne $file.sha256) { throw "Installed payload mismatch: $($file.path)" }
    }
    Assert-Result 'Installed payload matches source manifest' $true ("Verified all " + $manifest.files.Count + ' game files by SHA-256.')
    $expected = @($manifest.files | ForEach-Object path) + @('NeonFrontier.ico', 'unins000.exe', 'unins000.dat', 'unins000.msg')
    $unexpected = @(Get-ChildItem -LiteralPath $installRoot -Recurse -File | Where-Object { $_.FullName.Substring($installRoot.Length + 1).Replace('\', '/') -notin $expected })
    Assert-Result 'No debug or unrelated files installed' ($unexpected.Count -eq 0) 'Only the release payload, icon and uninstaller are installed.'

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($portable)
    try {
        Assert-Result 'Portable ZIP file count' ($archive.Entries.Count -eq $manifest.files.Count) 'Portable archive has the identical game payload.'
        foreach ($file in $manifest.files) {
            $entry = $archive.GetEntry('NeonFrontier/' + $file.path)
            if (-not $entry) { throw "Portable archive is missing $($file.path)" }
            $stream = $entry.Open(); $sha = [Security.Cryptography.SHA256]::Create()
            try { $hash = ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '').ToLowerInvariant() }
            finally { $stream.Dispose(); $sha.Dispose() }
            if ($hash -ne $file.sha256) { throw "Portable payload mismatch: $($file.path)" }
        }
        Assert-Result 'Portable ZIP content integrity' $true 'Every decompressed file matches the source payload SHA-256.'
    }
    finally { $archive.Dispose() }

    $runtimeRoot = Join-Path $resultRoot 'Runtime'
    $arguments = @('-screen-fullscreen', '0', '-screen-width', '1600', '-screen-height', '900', '-skirmishVerify', ('"' + $runtimeRoot + '"'), '-logFile', ('"' + (Join-Path $resultRoot 'player.log') + '"'))
    $game = Start-Process -FilePath (Join-Path $installRoot 'NeonFrontier.exe') -WorkingDirectory $installRoot -ArgumentList $arguments -PassThru -WindowStyle Hidden
    Assert-Result 'Installed game starts' (-not $game.WaitForExit(3000)) 'The installed executable remains running after launch.'
    Assert-Result 'Setup rejects a running game' ((Run-Setup 'running-game-guard.log') -ne 0) 'A second silent setup refuses to change a live installation.'
    Assert-Result 'Setup leaves the running match intact' (-not $game.HasExited) 'Process guard does not terminate the game.'
    if (-not $game.WaitForExit(300000)) { throw 'Installed game verification exceeded five minutes.' }
    $runtime = Get-Content -LiteralPath (Join-Path $runtimeRoot 'verification-report.json') -Raw | ConvertFrom-Json
    Assert-Result 'Installed game runtime verification' ($game.ExitCode -eq 0 -and $runtime.status -eq 'passed' -and $runtime.runtimeErrors.Count -eq 0) ($runtime.checks.Count.ToString() + ' runtime checks; zero runtime errors required.')
    Assert-Result 'Installed game interface renders' (@($runtime.screenshots | Where-Object { $_.includesInterface -and $_.nonblank }).Count -eq 14) 'Fourteen valid world-and-interface captures from the installed player.'
}
catch { $failure = $_.Exception.Message }
finally {
    if ($game -and -not $game.HasExited) {
        try { $game.Kill(); $game.WaitForExit() }
        catch { if (-not $game.HasExited) { $failure = 'Could not stop the test game for cleanup: ' + $_.Exception.Message } }
    }
    $uninstaller = Join-Path $installRoot 'unins000.exe'
    if ($script:activeSetup -and -not $script:activeSetup.HasExited) {
        Write-Warning "Setup process $($script:activeSetup.Id) remains active. The test installation is retained for inspection."
    }
    elseif (Test-Path -LiteralPath $uninstaller) {
        try {
            $safeRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'Builds/InstallerVerification')).TrimEnd('\') + '\'
            $registered = Get-ItemProperty -LiteralPath $registryPath
            if (-not $installRoot.StartsWith($safeRoot, [StringComparison]::OrdinalIgnoreCase) -or $registered.InstallLocation.TrimEnd('\') -ne $installRoot.TrimEnd('\')) { throw 'Refusing uninstall: the registered directory is not this isolated test installation.' }
            $process = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/LOG="' + (Join-Path $resultRoot 'uninstall.log') + '"')) -PassThru -WindowStyle Hidden
            if (-not $process.WaitForExit(60000)) { throw 'Uninstall timed out.' }
            $deadline = [DateTime]::UtcNow.AddSeconds(15)
            while ((Test-Path -LiteralPath $registryPath) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 200 }
            Assert-Result 'Silent uninstall completes' ($process.ExitCode -eq 0) 'Uninstaller reports success.'
            Assert-Result 'Uninstall removes game and registration' (-not (Test-Path -LiteralPath (Join-Path $installRoot 'NeonFrontier.exe')) -and -not (Test-Path -LiteralPath $registryPath)) 'Installed game and its current-user uninstall entry are removed.'
            Assert-Result 'Uninstall removes launch shortcut' (-not (Test-Path -LiteralPath $shortcut)) 'Start Menu is restored after the test.'
        }
        catch { if ($failure) { $failure += '; ' + $_.Exception.Message } else { $failure = $_.Exception.Message } }
    }
    $report = [ordered]@{ product = 'Neon Frontier'; version = $Version; startedUtc = $started; finishedUtc = [DateTime]::UtcNow.ToString('O'); status = $(if ($failure) { 'failed' } else { 'passed' }); failure = $failure; installerSha256 = $manifest.sha256; checks = @($checks.ToArray()) }
    $report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $resultRoot 'installer-verification.json') -Encoding UTF8
}
if ($failure) { throw $failure }
Write-Output "Installer verification passed: $($checks.Count) checks. Report: $resultRoot/installer-verification.json"
