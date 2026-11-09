# =============================================================================
# Gera src\Reparo.App\app.ico: quadrado azul arredondado com a chave inglesa branca
# (mesmo selo do cabecalho da tela, D-018/D-019). So precisa rodar de novo se o
# desenho mudar; o .ico gerado fica no repositorio.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/gerar-icone.ps1
#
# Precisa da fonte "Segoe MDL2 Assets" (Windows 10/11).
# =============================================================================
param(
    [string]$Saida = 'src\Reparo.App\app.ico',
    [string]$PastaPrevia = ''
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$raiz = Split-Path -Parent $PSScriptRoot
$destino = [System.IO.Path]::GetFullPath((Join-Path $raiz $Saida))

$tamanhos = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256
$azulTopo = [System.Drawing.Color]::FromArgb(59, 130, 246)   # 3B82F6
$azulBase = [System.Drawing.Color]::FromArgb(29, 78, 216)    # 1D4ED8
$glifo = [string][char]0xE90F                                # chave inglesa (Repair)

function Arredondado([single]$x, [single]$y, [single]$lado, [single]$raio) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $raio * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $lado - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $lado - $d, $y + $lado - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $lado - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Desenhar([int]$t) {
    $bmp = New-Object System.Drawing.Bitmap($t, $t, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([System.Drawing.Color]::Transparent)

    # Ícones pequenos ocupam tudo; os grandes têm uma margem, como os ícones do Windows.
    $margem = if ($t -le 24) { 0 } elseif ($t -le 48) { [Math]::Round($t * 0.04) } else { [Math]::Round($t * 0.06) }
    $lado = $t - 2 * $margem
    $raio = [Math]::Max(2, $lado * 0.22)
    $caminho = Arredondado $margem $margem $lado $raio
    $ret = New-Object System.Drawing.RectangleF($margem, $margem, $lado, $lado)
    $degrade = New-Object System.Drawing.Drawing2D.LinearGradientBrush($ret, $azulTopo, $azulBase, 90)
    $g.FillPath($degrade, $caminho)

    # A chave vira um contorno desenhado com traço extra, para não sumir nos tamanhos pequenos.
    $proporcao = if ($t -le 20) { 0.74 } elseif ($t -le 32) { 0.66 } else { 0.56 }
    $traco = if ($t -le 16) { 1.3 } elseif ($t -le 24) { 1.4 } elseif ($t -le 32) { 1.3 } else { $lado * 0.018 }
    $familia = New-Object System.Drawing.FontFamily('Segoe MDL2 Assets')
    $formato = New-Object System.Drawing.StringFormat
    $formato.Alignment = [System.Drawing.StringAlignment]::Center
    $formato.LineAlignment = [System.Drawing.StringAlignment]::Center
    $chave = New-Object System.Drawing.Drawing2D.GraphicsPath
    $chave.AddString($glifo, $familia, 0, [single]($lado * $proporcao), $ret, $formato)
    $caneta = New-Object System.Drawing.Pen([System.Drawing.Color]::White, [single]$traco)
    $caneta.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $g.FillPath([System.Drawing.Brushes]::White, $chave)
    $g.DrawPath($caneta, $chave)

    $g.Dispose(); $chave.Dispose(); $caneta.Dispose(); $familia.Dispose(); $degrade.Dispose(); $caminho.Dispose()
    return $bmp
}

# ICO com cada tamanho guardado como PNG (aceito pelo Windows Vista em diante).
$pngs = @()
foreach ($t in $tamanhos) {
    $bmp = Desenhar $t
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    if ($PastaPrevia) {
        New-Item -ItemType Directory -Force $PastaPrevia | Out-Null
        $bmp.Save((Join-Path $PastaPrevia "icone-$t.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    }
    $bmp.Dispose()
    $pngs += , @($t, $ms.ToArray())
}

$saidaIco = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter($saidaIco)
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$pngs.Count)
$deslocamento = 6 + 16 * $pngs.Count
foreach ($p in $pngs) {
    $t = $p[0]; $dados = $p[1]
    $lado = if ($t -ge 256) { 0 } else { $t }
    $w.Write([byte]$lado); $w.Write([byte]$lado); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$dados.Length); $w.Write([uint32]$deslocamento)
    $deslocamento += $dados.Length
}
foreach ($p in $pngs) { $w.Write([byte[]]$p[1]) }
$w.Flush()
[System.IO.File]::WriteAllBytes($destino, $saidaIco.ToArray())
Write-Host "Icone gerado: $destino ($($tamanhos -join ', ') px)" -ForegroundColor Green
