# Install the native artwork and the Windows companion-selection bridge.
[CmdletBinding()]
param(
    [string] $CodexHome = $env:CODEX_HOME,
    [switch] $VerifyOnly,
    [switch] $RemoveLink
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'WindowsLink.ps1')
if ($RemoveLink) { Update-CodexLink -Remove; return }
if ([string]::IsNullOrWhiteSpace($CodexHome)) {
    $CodexHome = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.codex'
}
$root = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($CodexHome)
$petRoot = Join-Path $root 'pets'
$destination = Join-Path $petRoot 'soot-keys'
$source = Join-Path $PSScriptRoot 'pet'
$files = @('pet.json', 'spritesheet.webp')

# Validate the same v2 dimensions used by Codex. Pillow writes these lossless
# sheets with either a VP8L header or an extended VP8X header.
function Test-Spritesheet([string] $Path) {
    $bytes = [IO.File]::ReadAllBytes($Path)
    $ascii = [Text.Encoding]::ASCII
    if ($bytes.Length -lt 25 -or $ascii.GetString($bytes, 0, 4) -ne 'RIFF' -or
        $ascii.GetString($bytes, 8, 4) -ne 'WEBP' -or
        [BitConverter]::ToUInt32($bytes, 4) -ne $bytes.Length - 8) {
        throw "Invalid WebP file: $Path"
    }
    switch ($ascii.GetString($bytes, 12, 4)) {
        'VP8L' {
            if ($bytes[20] -ne 47) { throw 'Invalid VP8L header.' }
            $bits = [BitConverter]::ToUInt32($bytes, 21)
            $width = 1 + ($bits -band 16383)
            $height = 1 + (($bits -shr 14) -band 16383)
        }
        'VP8X' {
            if ($bytes.Length -lt 30) { throw 'Incomplete VP8X header.' }
            $width = 1 + $bytes[24] + 256 * $bytes[25] + 65536 * $bytes[26]
            $height = 1 + $bytes[27] + 256 * $bytes[28] + 65536 * $bytes[29]
        }
        default { throw 'Expected a lossless or extended WebP spritesheet.' }
    }
    if ($width -ne 1536 -or $height -ne 2288) {
        throw "Expected a 1536 x 2288 v2 spritesheet, found $width x $height."
    }
}

function Confirm-Manifest([string] $Directory) {
    $installed = Join-Path $Directory 'pet.json'
    $expected = [IO.File]::ReadAllText((Join-Path $source 'pet.json'))
    if (-not [IO.File]::Exists($installed) -or
        [IO.File]::ReadAllText($installed).Replace("`r`n","`n") -cne $expected.Replace("`r`n","`n")) {
        throw "Missing or customized manifest at '$installed'."
    }
}

function Confirm-PetFiles([string] $Directory) {
    Confirm-Manifest $Directory
    $installed = Join-Path $Directory 'spritesheet.webp'
    if (-not [IO.File]::Exists($installed) -or (Get-FileHash -LiteralPath $installed).Hash -ne $newSpriteHash) {
        throw "Missing or different artwork at '$installed'."
    }
}

function Update-PublishedSprite([string] $Directory) {
    Confirm-Manifest $Directory
    $installed = Join-Path $Directory 'spritesheet.webp'
    $current = (Get-FileHash -LiteralPath $installed).Hash
    if ($current -eq $newSpriteHash) { return }
    # The art distributed in v1.0 through v1.1.1 has one shared digest.
    if ($current -ne '3DFA5888DF9F1B64D4FE5207F6EB0300D29AA1FB8D667BF06F84FA5C5E955BB0') {
        throw "Artwork at '$installed' has been customized or belongs to a newer release. Back it up before installing."
    }
    $staged = Join-Path $Directory ([IO.Path]::GetRandomFileName())
    try {
        [IO.File]::Copy($sourceSprite,$staged)
        if ((Get-FileHash -LiteralPath $staged).Hash -ne $newSpriteHash) { throw 'Artwork copy differs.' }
        [IO.File]::Replace($staged,$installed,[System.Management.Automation.Language.NullString]::Value)
    } finally {
        [IO.File]::Delete($staged)
    }
}

$manifest = [IO.File]::ReadAllText((Join-Path $source 'pet.json')) | ConvertFrom-Json
if ($manifest.spriteVersionNumber -ne 2 -or $manifest.spritesheetPath -cne 'spritesheet.webp' -or
    [string]::IsNullOrWhiteSpace($manifest.displayName)) {
    throw 'Invalid pet.json. Expected a named v2 companion using spritesheet.webp.'
}
$sourceSprite = Join-Path $source 'spritesheet.webp'
Test-Spritesheet $sourceSprite
$newSpriteHash = (Get-FileHash -LiteralPath $sourceSprite).Hash
if ($VerifyOnly -or (Test-Path -LiteralPath $destination)) {
    if (-not $VerifyOnly) { Update-PublishedSprite $destination }
    Confirm-PetFiles $destination
} else {
    [void][IO.Directory]::CreateDirectory($petRoot)
    # Stage outside the discovery directory. Directory.Move refuses to replace a
    # destination created by another installer while the files were being copied.
    $staging = Join-Path $root ('soot-keys-install-' + [Guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($staging)
    try {
        foreach ($name in $files) {
            [IO.File]::Copy((Join-Path $source $name), (Join-Path $staging $name))
        }
        Confirm-PetFiles $staging
        try { [IO.Directory]::Move($staging, $destination) }
        catch [IO.IOException] {
            if (-not [IO.Directory]::Exists($destination)) { throw }
            Confirm-PetFiles $destination
        }
    } finally {
        if ([IO.Directory]::Exists($staging)) {
            foreach ($name in $files) { [IO.File]::Delete((Join-Path $staging $name)) }
            [IO.Directory]::Delete($staging)
        }
    }
    Confirm-PetFiles $destination
}
Write-Output ("Verified {0} in {1}" -f $manifest.displayName, $destination)
Update-CodexLink $root -Verify:$VerifyOnly
