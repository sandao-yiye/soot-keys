# Exercise real Windows shortcuts, copies and child processes in an isolated folder.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\WindowsLink.ps1')
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('SootKeys-Test-' + [Guid]::NewGuid().ToString('N'))
$null = [IO.Directory]::CreateDirectory($fixture)
$portable = Join-Path $fixture 'portable.exe'
$linkPath = Join-Path $fixture 'startup\Soot Keys.lnk'
$fixtureBridge = Join-Path $fixture 'bridge'
function Get-BridgeLayout { @{Folder=$fixtureBridge; Shortcut=$linkPath} }
function Get-PortableExe { return $portable }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$sourcePath = Join-Path $fixture 'Stub.cs'
function Build-Fixture([int]$Version) {
    [IO.File]::WriteAllText($sourcePath, "class Stub { static int Main() { return $Version - $Version; } }")
    & $compiler /nologo /target:winexe "/out:$portable" $sourcePath
    if ($LASTEXITCODE -ne 0) { throw 'Could not build fixture' }
}
try {
    $petHome = Join-Path $fixture 'custom home'
    Build-Fixture 1
    Update-CodexLink $petHome
    Update-CodexLink $petHome -Verify
    Update-CodexLink $petHome
    $first = (New-Object -ComObject WScript.Shell).CreateShortcut($linkPath).TargetPath
    Build-Fixture 2
    Update-CodexLink $petHome
    Update-CodexLink $petHome -Verify
    if ([IO.File]::Exists($first)) { throw 'Upgrade left its old executable behind.' }
    Update-CodexLink -Remove
    if ([IO.File]::Exists($linkPath)) { throw 'Startup shortcut survived removal.' }
    $foreign = (New-Object -ComObject WScript.Shell).CreateShortcut($linkPath)
    $foreign.TargetPath = $portable
    $foreign.Save()
    $refused = $false
    try { Update-CodexLink -Remove } catch { $refused = $true }
    if (-not $refused -or -not [IO.File]::Exists($linkPath) -or -not [IO.File]::Exists($portable)) {
        throw 'Foreign shortcut protection failed.'
    }
    'PASS - install, verify, reinstall, upgrade, remove and unrelated shortcut protection'
} finally {
    $base = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
    $folder = Get-Item -LiteralPath $fixture
    if ($folder.Parent.FullName -eq $base -and $folder.Name -match '^SootKeys-Test-[a-f0-9]{32}$') {
        Remove-Item -LiteralPath $folder.FullName -Recurse -Force
    }
}
