# Shared by the installer and uninstaller. Everything belongs to the current user.
function Get-BridgeLayout {
    $folder = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'SootKeys\bridge'
    $startup = [Environment]::GetFolderPath('Startup')
    if (-not $startup) { throw 'Windows did not return the current user Startup folder.' }
    @{
        Folder = $folder
        Shortcut = (Join-Path $startup 'Soot Keys - Codex.lnk')
    }
}

function Get-OwnedBridge([string] $Executable, [string] $BridgeFolder) {
    if (-not $Executable -or -not [IO.File]::Exists($Executable)) { return $false }
    $file = Get-Item -LiteralPath $Executable
    if ($file.Name -ine 'SootKeys.exe' -or $file.Directory.Parent.FullName -ine $BridgeFolder -or
        $file.Directory.Name -notmatch '^[a-f0-9]{64}$') { return $false }
    return (Get-FileHash -LiteralPath $file.FullName).Hash -ieq $file.Directory.Name
}

function Get-PortableExe {
    $packaged = Join-Path $PSScriptRoot 'SootKeys.exe'
    if ([IO.File]::Exists($packaged)) { return $packaged }
    $sourceBuild = Join-Path $PSScriptRoot 'desktop\dist\SootKeys.exe'
    if (-not [IO.File]::Exists($sourceBuild)) {
        & (Join-Path $PSScriptRoot 'desktop\build.ps1') | Out-Host
    }
    return $sourceBuild
}

function Remove-BridgeFile([string] $Executable) {
    # A mask-restoration worker can briefly outlive the stopped listener.
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        try { [IO.File]::Delete($Executable); break }
        catch [IO.IOException] { if ($attempt -eq 19) { throw } }
        catch [UnauthorizedAccessException] { if ($attempt -eq 19) { throw } }
        Start-Sleep -Milliseconds 150
    }
    $dir = [IO.Path]::GetDirectoryName($Executable)
    if ([IO.Directory]::Exists($dir) -and [IO.Directory]::GetFileSystemEntries($dir).Length -eq 0) {
        [IO.Directory]::Delete($dir)
    }
}

function Update-CodexLink([string] $PetHome, [switch] $Verify, [switch] $Remove) {
    $paths = Get-BridgeLayout
    $ws = New-Object -ComObject WScript.Shell
    $shortcut = $ws.CreateShortcut($paths.Shortcut)
    $hasShortcut = [IO.File]::Exists($paths.Shortcut)
    $previous = $shortcut.TargetPath
    if ($hasShortcut -and -not (Get-OwnedBridge $previous $paths.Folder)) {
        throw "Existing shortcut has an unexpected target: $($paths.Shortcut)"
    }
    if ($Remove) {
        if ($hasShortcut) {
            $stopped = Start-Process -FilePath $previous -ArgumentList '--stop-linked' -PassThru -Wait
            if ($stopped.ExitCode -ne 0) { throw 'The previous Codex link is still running.' }
            Remove-BridgeFile $previous
            [IO.File]::Delete($paths.Shortcut)
        }
        Write-Output 'Removed the automatic Codex link. The native pet remains available.'
        return
    }
    $exe = Get-PortableExe
    $hash = (Get-FileHash -LiteralPath $exe).Hash.ToLowerInvariant()
    $dir = Join-Path $paths.Folder $hash
    $installed = Join-Path $dir 'SootKeys.exe'
    # Win32 command-line parsing doubles trailing backslashes before a closing quote.
    $argHome = $PetHome -replace '(\\+)$', '$1$1'
    $arguments = '--codex-linked "' + $argHome + '"'
    if ($Verify) {
        if (-not $hasShortcut -or $previous -ine $installed -or $shortcut.Arguments -cne $arguments -or
            -not (Get-OwnedBridge $installed $paths.Folder)) { throw 'Codex link verification failed.' }
        return
    }
    [void][IO.Directory]::CreateDirectory($dir)
    if (-not [IO.File]::Exists($installed)) {
        $stage = Join-Path $dir ([IO.Path]::GetRandomFileName())
        try {
            [IO.File]::Copy($exe, $stage)
            try { [IO.File]::Move($stage,$installed) } catch [IO.IOException] {
                if (-not [IO.File]::Exists($installed)) { throw }
            }
        } finally { [IO.File]::Delete($stage) }
    }
    if (-not (Get-OwnedBridge $installed $paths.Folder)) { throw 'Installed bridge checksum mismatch.' }
    $changed = $hasShortcut -and ($previous -ine $installed -or $shortcut.Arguments -cne $arguments)
    if ($changed) {
        $stopped = Start-Process -FilePath $previous -ArgumentList '--stop-linked' -Wait -PassThru
        if ($stopped.ExitCode -ne 0) { throw 'Could not close the earlier link.' }
    }
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($paths.Shortcut))
    $shortcut.TargetPath = $installed
    $shortcut.Arguments = $arguments
    $shortcut.WorkingDirectory = $dir
    $shortcut.Description = 'Activate Soot Keys with the selected Codex desktop companion.'
    $shortcut.Save()
    $process = Start-Process -FilePath $installed -ArgumentList $arguments -PassThru
    # A second invocation can exit successfully if this version is already watching.
    if ($process.WaitForExit(800) -and $process.ExitCode -ne 0) { throw 'Codex link failed to start.' }
    if ($changed -and $previous -ine $installed -and (Get-OwnedBridge $previous $paths.Folder)) {
        Remove-BridgeFile $previous
    }
    Write-Output 'Codex link installed for this Windows user.'
}
