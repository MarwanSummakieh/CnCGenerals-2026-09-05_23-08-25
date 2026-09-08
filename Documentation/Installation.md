# Installing Neon Frontier

Download `NeonFrontier-0.1.0-Windows-Setup.exe` from the project's GitHub release and open it. Windows 10 version 21H1 (build 19043) or later and an x64 processor are required, following the [Unity 6000.3 player requirements](https://docs.unity3d.com/6000.3/Documentation/Manual/windows-requirements-and-compatibility.html). The game needs a graphics device compatible with DirectX 11 or DirectX 12.

The installer installs for the current Windows account without requesting administrator access. Its default location is `%LOCALAPPDATA%\Programs\Neon Frontier`. A Start Menu shortcut is included; the desktop shortcut is optional. The final page offers an unchecked option to launch the game.

All required Unity player files are included. Keep `NeonFrontier.exe`, `UnityPlayer.dll`, `MonoBleedingEdge`, `D3D12`, and `NeonFrontier_Data` together if you move a portable copy. No Unity Editor installation is needed to play.

For installation without shortcuts or an uninstall entry, download `NeonFrontier-0.1.0-Windows-x64.zip`, extract it, and open `NeonFrontier\NeonFrontier.exe`.

## Updates and removal

Close every running copy of Neon Frontier before installing an update or uninstalling. Setup checks for the game process and offers Retry if it is still running. Updates reuse the existing installation directory and shortcut choices. Installing another version uses the same Windows uninstall entry.

Remove the game through **Settings → Apps → Installed apps → Neon Frontier → Uninstall**. The uninstaller removes the files and shortcuts it installed. User preferences and player logs stored outside the installation directory are retained.

## Verify a download

Compare the installer's SHA-256 hash with its accompanying `.sha256` file:

```powershell
Get-FileHash .\NeonFrontier-0.1.0-Windows-Setup.exe -Algorithm SHA256
```

## Build the installer

First create a Windows player build with `Tools/BuildSkirmish.ps1`, or place an existing complete build in `Builds/NeonFrontierDemo`. Then run from the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File .\Tools\BuildInstaller.ps1 -BootstrapCompiler
```

The optional bootstrap flag downloads the pinned Inno Setup 6.7.3 compiler from its official release, checks its SHA-256 and Windows signature, and installs it for the current account under `%LOCALAPPDATA%\NeonFrontier\BuildTools`. The compiler is a build dependency and is not included in the game installer. If Inno Setup is already available, omit the flag or pass `-CompilerPath 'C:\path\to\ISCC.exe'`.

Optional parameters are `-Version`, `-BuildPath`, and `-OutputPath`. The default output directory is `Builds/Releases`, containing the installer, portable ZIP, SHA-256 files, compilation log, and a payload manifest with individual file hashes. The script checks the required runtime files before compiling. Debug symbols, backup folders, `DoNotShip` directories, temporary files, crash dumps, and logs are excluded.

The installer definition is `Tools/Installer/NeonFrontier.iss`. Its AppId must remain unchanged across updates. Branding assets are reproduced by `Tools/Installer/BuildBranding.ps1`. No signing certificate is configured; Windows may show an unknown publisher for the game installer.

## Unattended installation

Close the game first. Silent setup does not launch or restart the game and does not create a desktop shortcut unless requested:

```powershell
.\NeonFrontier-0.1.0-Windows-Setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /LOG="setup.log"
```

Use `/DIR="C:\path\to\Neon Frontier"` to choose a directory writable by the current account. Add `/TASKS="desktopicon"` to request the desktop shortcut or `/TASKS=""` to explicitly disable it during an update. Silent installation or uninstallation exits without changing files if a game process is detected. Silent uninstall uses `unins000.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG="uninstall.log"` from the installation directory.
