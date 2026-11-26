# Run from a project checkout with the earlier artwork tags available.
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
    $script = Join-Path $package 'Install-CodexPet.ps1'
    $sheet = Join-Path $dest 'spritesheet.webp'
    $newHash = (Get-FileHash -LiteralPath (Join-Path $package 'pet\spritesheet.webp')).Hash
    foreach ($tag in 'v1.1.1', 'v1.2.0') {
        $oldZip = Join-Path $fixture "$tag.zip"
        & git -C $repo archive --format=zip -o $oldZip ("${tag}:pet")
        if ($LASTEXITCODE -ne 0) { throw "Artwork fixture unavailable for $tag." }
        Expand-Archive -LiteralPath $oldZip -DestinationPath $dest -Force
        if ((Get-FileHash -LiteralPath $sheet).Hash -eq $newHash) { throw "Fixture $tag already matches the new artwork." }
        & $script -CodexHome $homeDir
        if ((Get-FileHash -LiteralPath $sheet).Hash -ne $newHash) { throw "Upgrade from $tag did not install new art." }
    }
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
