# Run from a project checkout with the v1.1.1 release tag available.
$ErrorActionPreference = 'Stop'
$repo = (Get-Item -LiteralPath (Join-Path $PSScriptRoot '..')).FullName
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('SootKeys-Art-' + [Guid]::NewGuid().ToString('N'))
$null = [IO.Directory]::CreateDirectory($fixture)
try {
    $package = Join-Path $fixture 'package'
    $homeDir = Join-Path $fixture 'codex'
    $dest = Join-Path $homeDir 'pets\soot-keys'
    $null = [IO.Directory]::CreateDirectory($package)
    $null = [IO.Directory]::CreateDirectory($dest)
    Copy-Item -LiteralPath (Join-Path $repo 'pet') -Destination $package -Recurse
    Copy-Item -LiteralPath (Join-Path $repo 'Install-CodexPet.ps1') -Destination $package
    # The bridge and real Startup folder are exercised separately.
    'function Update-CodexLink { param($PetHome, [switch]$Verify) }' |
        Set-Content -LiteralPath (Join-Path $package 'WindowsLink.ps1') -Encoding Ascii
    $oldZip = Join-Path $fixture 'old.zip'
    & git -C $repo archive --format=zip -o $oldZip v1.1.1:pet
    if ($LASTEXITCODE -ne 0) { throw 'Old artwork fixture unavailable.' }
    Expand-Archive -LiteralPath $oldZip -DestinationPath $dest
    $script = Join-Path $package 'Install-CodexPet.ps1'
    $sheet = Join-Path $dest 'spritesheet.webp'
    $newHash = (Get-FileHash -LiteralPath (Join-Path $package 'pet\spritesheet.webp')).Hash
    if ((Get-FileHash -LiteralPath $sheet).Hash -eq $newHash) { throw 'Fixture needs two different releases.' }
    & $script -CodexHome $homeDir
    & $script -CodexHome $homeDir
    & $script -CodexHome $homeDir -VerifyOnly
    if ((Get-FileHash -LiteralPath $sheet).Hash -ne $newHash) { throw 'Upgrade did not install new art.' }
    [IO.File]::AppendAllText($sheet, 'modified')
    $custom = (Get-FileHash -LiteralPath $sheet).Hash
    $refused = $false
    try { & $script -CodexHome $homeDir } catch { $refused = $true }
    if (-not $refused -or (Get-FileHash -LiteralPath $sheet).Hash -ne $custom) { throw 'Custom artwork was overwritten.' }
    'PASS - previous release upgraded, repeat install verified, custom art preserved'
} finally {
    $dir = Get-Item -LiteralPath $fixture
    if ($dir.Parent.FullName -eq [IO.Path]::GetTempPath().TrimEnd('\') -and $dir.Name -match '^SootKeys-Art-[a-f0-9]{32}$') {
        Remove-Item -LiteralPath $dir.FullName -Recurse -Force
    }
}
