param(
    [string]$UnityPath = 'C:/Program Files/Unity/Hub/Editor/6000.3.10f1/Editor/Unity.exe',
    [switch]$SceneOnly,
    [switch]$NoWait,
    [ValidateRange(30, 7200)][int]$TimeoutSeconds = 1800
)

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not (Test-Path -LiteralPath $UnityPath)) { throw "Unity executable not found: $UnityPath" }
$command = if ($SceneOnly) { 'scene' } else { 'build' }
$tempDirectory = Join-Path $projectRoot 'Temp'
$requestPath = Join-Path $tempDirectory 'SkirmishBuild.request'
$statusPath = Join-Path $tempDirectory 'SkirmishBuild.status.json'
$logDirectory = Join-Path $projectRoot 'Logs'
New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null

# Reuse this project's editor. Never kill it, open a second instance on its project,
# change its active scene, or stop an existing Play Mode session.
$normalizedProject = $projectRoot.Replace('\', '/').TrimEnd('/')
$openEditor = Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" |
    Where-Object { $_.CommandLine -and $_.CommandLine.Replace('\', '/').IndexOf($normalizedProject, [StringComparison]::OrdinalIgnoreCase) -ge 0 } |
    Select-Object -First 1

if ($openEditor) {
    New-Item -ItemType Directory -Force -Path $tempDirectory | Out-Null
    if (Test-Path -LiteralPath $requestPath) {
        throw "A gameplay build request is already pending. Inspect $statusPath before submitting another."
    }
    if (Test-Path -LiteralPath $statusPath) {
        try { $previousStatus = Get-Content -LiteralPath $statusPath -Raw | ConvertFrom-Json } catch { $previousStatus = $null }
        if ($previousStatus -and $previousStatus.state -eq 'running') {
            throw "A gameplay build is already running in the open Unity editor. Inspect $statusPath."
        }
    }
    $requestId = [Guid]::NewGuid().ToString('N')
    $pendingPath = Join-Path $tempDirectory ("SkirmishBuild.$requestId.pending")
    [System.IO.File]::WriteAllLines($pendingPath, @($command, $requestId))
    Move-Item -LiteralPath $pendingPath -Destination $requestPath
    Write-Output "Requested '$command' in the open Unity editor (PID $($openEditor.ProcessId))."
    Write-Output 'The editor must import SkirmishBuilder.cs once before its request bridge is available. If needed, focus Unity to let it compile.'
    if ($NoWait) {
        Write-Output "Status: $statusPath"
        return
    }
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $previousMessage = ''
    while ([DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Seconds 1
        if (-not (Test-Path -LiteralPath $statusPath)) { continue }
        # The editor writes a small status file; an in-progress write is retried.
        try { $status = Get-Content -LiteralPath $statusPath -Raw | ConvertFrom-Json } catch { continue }
        if ($status.requestId -ne $requestId) { continue }
        $message = "$($status.state): $($status.message)"
        if ($message -ne $previousMessage) {
            Write-Output $message
            $previousMessage = $message
        }
        if ($status.state -eq 'failed') { throw "Unity gameplay build failed. $($status.message)" }
        if ($status.state -eq 'completed') {
            if (-not $SceneOnly) { Write-Output "Player: $($status.player)" }
            return
        }
    }
    throw "Timed out waiting for Unity. The request/build has not been cancelled. Inspect $statusPath and the Unity Console."
}

if (Test-Path -LiteralPath (Join-Path $tempDirectory 'UnityLockfile')) {
    throw 'Unity has a project lock but its process could not be identified. Close the project normally or inspect its editor before running a batch build.'
}
if ($NoWait) { throw '-NoWait is supported only when this project is already open in Unity.' }
$method = if ($SceneOnly) { 'NeonFrontier.Editor.SkirmishBuilder.Build' } else { 'NeonFrontier.Editor.SkirmishBuilder.BuildAll' }
$logPath = Join-Path $logDirectory 'SkirmishBuild.log'
$arguments = @('-batchmode', '-projectPath', ('"' + $projectRoot + '"'), '-executeMethod', $method, '-quit', '-logFile', ('"' + $logPath + '"'))
$unityProcess = Start-Process -FilePath $UnityPath -ArgumentList $arguments -PassThru -WindowStyle Hidden
# Wait for the editor itself; its shared licensing helper can outlive the build.
$unityProcess.WaitForExit()
if ($unityProcess.ExitCode -ne 0) { throw "Unity gameplay build failed. See $logPath." }
Write-Output 'Neon Frontier gameplay scene generated successfully.'
if (-not $SceneOnly) { Write-Output ("Player: " + (Join-Path $projectRoot 'Builds/NeonFrontierDemo/NeonFrontier.exe')) }
