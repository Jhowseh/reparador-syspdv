# =============================================================================
# Gera os logos da PBtec usados na tela (cabecalho e aba "Sobre"), a partir de
# assets\logos:
#   src\Reparo.App\Recursos\logo-claro.png   (tema claro: logo colorido)
#   src\Reparo.App\Recursos\logo-escuro.png  (tema escuro: logo branco/vermelho)
#
# O logo do tema escuro vem com fundo preto; aqui o preto vira transparente,
# para combinar com o fundo da tela. Os dois sao recortados (sem margem) e
# reduzidos. So precisa rodar de novo se os logos mudarem.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/gerar-logos.ps1
# =============================================================================
param(
    [int]$LadoMaximo = 480
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class Logos
{
    static byte[] Ler(Bitmap b, out BitmapData d)
    {
        d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        var px = new byte[d.Stride * b.Height];
        Marshal.Copy(d.Scan0, px, 0, px.Length);
        return px;
    }

    // Fundo preto -> transparente, mantendo as bordas suaves (cor sobre preto = cor * alfa).
    public static void PretoParaTransparente(Bitmap b)
    {
        BitmapData d;
        var px = Ler(b, out d);
        for (int i = 0; i < px.Length; i += 4)
        {
            int bl = px[i], g = px[i + 1], r = px[i + 2];
            int a = Math.Max(r, Math.Max(g, bl));
            if (a < 24) { px[i] = px[i + 1] = px[i + 2] = px[i + 3] = 0; continue; }
            px[i] = (byte)Math.Min(255, bl * 255 / a);
            px[i + 1] = (byte)Math.Min(255, g * 255 / a);
            px[i + 2] = (byte)Math.Min(255, r * 255 / a);
            px[i + 3] = (byte)a;
        }
        Marshal.Copy(px, 0, d.Scan0, px.Length);
        b.UnlockBits(d);
    }

    // Retangulo com tudo que nao e transparente.
    public static Rectangle Conteudo(Bitmap b)
    {
        BitmapData d;
        var px = Ler(b, out d);
        b.UnlockBits(d);
        int x0 = b.Width, y0 = b.Height, x1 = -1, y1 = -1;
        for (int y = 0; y < b.Height; y++)
            for (int x = 0; x < b.Width; x++)
                if (px[y * d.Stride + x * 4 + 3] > 16)
                {
                    if (x < x0) x0 = x; if (x > x1) x1 = x;
                    if (y < y0) y0 = y; if (y > y1) y1 = y;
                }
        return x1 < 0 ? new Rectangle(0, 0, b.Width, b.Height) : Rectangle.FromLTRB(x0, y0, x1 + 1, y1 + 1);
    }
}
'@

$raiz = Split-Path -Parent $PSScriptRoot
$origem = Join-Path $raiz 'assets\logos'
$destino = Join-Path $raiz 'src\Reparo.App\Recursos'
New-Item -ItemType Directory -Force $destino | Out-Null

function Gerar([string]$arquivo, [string]$saida, [bool]$tirarPreto) {
    $original = [System.Drawing.Bitmap]::FromFile((Join-Path $origem $arquivo))
    $bmp = New-Object System.Drawing.Bitmap($original.Width, $original.Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.DrawImage($original, 0, 0, $original.Width, $original.Height)
    $g.Dispose(); $original.Dispose()

    if ($tirarPreto) { [Logos]::PretoParaTransparente($bmp) }
    $corte = [Logos]::Conteudo($bmp)

    $escala = [Math]::Min(1.0, $LadoMaximo / [Math]::Max($corte.Width, $corte.Height))
    $w = [int][Math]::Round($corte.Width * $escala); $h = [int][Math]::Round($corte.Height * $escala)
    $final = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($final)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($bmp, (New-Object System.Drawing.Rectangle(0, 0, $w, $h)), $corte, [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose(); $bmp.Dispose()

    $caminho = Join-Path $destino $saida
    $final.Save($caminho, [System.Drawing.Imaging.ImageFormat]::Png)
    $final.Dispose()
    Write-Host "Logo gerado: $caminho (${w}x${h})" -ForegroundColor Green
}

Gerar 'Logo PBtec Light.png' 'logo-claro.png' $false
Gerar 'PBtec Black & White Logo.png' 'logo-escuro.png' $true
