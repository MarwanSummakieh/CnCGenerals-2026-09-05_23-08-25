param(
    [string]$BlenderPath = 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe',
    [string]$UnityPath = 'C:/Program Files/Unity/Hub/Editor/6000.3.10f1/Editor/Unity.exe',
    [switch]$SkipBlender,
    [switch]$SkipPlayer
)

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not (Test-Path -LiteralPath $UnityPath)) { throw "Unity executable not found: $UnityPath" }
if (-not $SkipBlender -and -not (Test-Path -LiteralPath $BlenderPath)) { throw "Blender executable not found: $BlenderPath" }
$logDirectory = Join-Path $projectRoot 'Logs'
New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null

Push-Location -LiteralPath $projectRoot
try {
    if (-not $SkipBlender) {
        foreach ($generator in @('generate_units.py', 'generate_structures.py', 'assemble_armies.py')) {
            & $BlenderPath --background --python (Join-Path $PSScriptRoot "Blender/$generator")
            if ($LASTEXITCODE -ne 0) { throw "Blender generator failed: $generator" }
        }
    }
    $method = if ($SkipPlayer) { 'NeonFrontier.Editor.ArmyShowcaseBuilder.Build' } else { 'NeonFrontier.Editor.ArmyShowcaseBuilder.BuildAll' }
    $arguments = @('-batchmode', '-projectPath', ('"' + $projectRoot + '"'), '-executeMethod', $method, '-quit', '-logFile', ('"' + (Join-Path $logDirectory 'ArmyRebuild.log') + '"'))
    $unityProcess = Start-Process -FilePath $UnityPath -ArgumentList $arguments -PassThru -Wait -WindowStyle Hidden
    if ($unityProcess.ExitCode -ne 0) { throw "Unity rebuild failed. See Logs/ArmyRebuild.log." }
    Write-Output 'Neon Frontier army assets rebuilt successfully.'
}
finally { Pop-Location }
