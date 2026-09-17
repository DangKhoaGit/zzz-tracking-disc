param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/synthetic-replays'))
$ErrorActionPreference = 'Stop'
# Synthetic regression fixtures, never game accuracy evidence. No external assets.
foreach ($height in @(1080, 1440)) {
    $width = [int]($height * 16 / 9)
    $directory = Join-Path $OutputDirectory ($height.ToString() + 'p-' + [guid]::NewGuid().ToString('N'))
    [System.IO.Directory]::CreateDirectory($directory) | Out-Null
    $pattern = New-Object byte[] 64
    for ($y = 0; $y -lt 8; $y++) { for ($x = 0; $x -lt 8; $x++) { if (($x + $y) % 2 -eq 0) { $pattern[$y * 8 + $x] = 255 } } }
    $pack = @{ SchemaVersion = 1; Id = 'synthetic-checker'; Version = 1; ReferenceWidth = $width; ReferenceHeight = $height; UiScale = 1
        ClientArea = @{ X = 0; Y = 0; Width = 1; Height = 1 }
        Templates = @(@{ Id = 'checker'; SubjectId = 'demo-buff'; Kind = 'Buff'; Version = 1; Roi = @{ X = 0.25; Y = 0.25; Width = 0.25; Height = 0.25 }
            Width = 8; Height = 8; Pixels = [Convert]::ToBase64String($pattern); OnThreshold = 0.94; OffThreshold = 0.70; ConfirmationFrames = 3; CooldownMilliseconds = 300; CharacterId = 'demo-character' }) }
    [IO.File]::WriteAllText((Join-Path $directory 'templates.json'), ($pack | ConvertTo-Json -Depth 8))
    $present = New-Object byte[] ($width * $height)
    $rw = [int]($width / 4); $rh = [int]($height / 4)
    for ($y = 0; $y -lt $rh; $y++) { for ($x = 0; $x -lt $rw; $x++) {
        $sy = [int][math]::Floor($y * 8 / $rh); $sx = [int][math]::Floor($x * 8 / $rw)
        $present[($rh + $y) * $width + $rw + $x] = $pattern[$sy * 8 + $sx]
    } }
    $absent = New-Object byte[] ($width * $height)
    $entries = @()
    for ($i = 0; $i -lt 8; $i++) {
        $filename = 'frame-{0:D4}.gray' -f $i
        $stream = [IO.File]::Open((Join-Path $directory $filename), [IO.FileMode]::CreateNew)
        $writer = New-Object IO.BinaryWriter($stream)
        try {
            $writer.Write([int]0x315A5A47); $writer.Write([int]$width); $writer.Write([int]$height)
            if ($i -lt 5) { $writer.Write([byte[]]$present) } else { $writer.Write([byte[]]$absent) }
        } finally { $writer.Dispose() }
        $expected = ''
        if ($i -eq 2) { $expected = 'BuffIconAppeared:demo-buff' }
        if ($i -eq 7) { $expected = 'BuffIconDisappeared:demo-buff' }
        $entries += @{ File = $filename; OffsetMilliseconds = $i * 100; Expected = $expected }
    }
    $manifest = @{ SchemaVersion = 1; Description = 'SYNTHETIC checker appearance/absence; not ZZZ accuracy data'; TemplatePack = 'templates.json'; Frames = $entries }
    [IO.File]::WriteAllText((Join-Path $directory 'replay.json'), ($manifest | ConvertTo-Json -Depth 8))
    Write-Output (Join-Path $directory 'replay.json')
}
