# =============================================================================
# Gera o pacote para as lojas: um único ReparoSysPDV.exe (sem instalar .NET e
# sem arquivo de configuração), que pede administrador ao abrir.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/publicar.ps1
#
# Saída: dist\ (ignorada pelo git).
# ATENÇÃO: não execute o .exe gerado neste computador. Ele usa as ferramentas e a
# pasta REAIS (C:\SYSPDV). Para testar, use o harness ou o roteiro docs/teste-real.md.
# =============================================================================
param(
    [string]$Saida = 'dist'
)
$ErrorActionPreference = 'Stop'

$raiz    = Split-Path -Parent $PSScriptRoot
$projeto = Join-Path $raiz 'src\Reparo.App\Reparo.App.csproj'
$destino = Join-Path $raiz $Saida

# Só limpa a pasta de saída se ela estiver dentro do repositório.
$destinoCompleto = [System.IO.Path]::GetFullPath($destino)
if (-not $destinoCompleto.StartsWith($raiz + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "A pasta de saída precisa ficar dentro do repositório: $destinoCompleto"
}
if (Test-Path -LiteralPath $destinoCompleto) { Remove-Item -LiteralPath $destinoCompleto -Recurse -Force }

Write-Host 'Rodando os testes de unidade...' -ForegroundColor Cyan
dotnet test (Join-Path $raiz 'Reparo.sln') -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Os testes de unidade falharam. Nada foi publicado.' }

Write-Host 'Publicando ReparoSysPDV.exe...' -ForegroundColor Cyan
dotnet publish $projeto -c Release -r win-x64 --self-contained true --nologo `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:ExigirAdministrador=true `
    -p:DebugType=none `
    -o $destinoCompleto
if ($LASTEXITCODE -ne 0) { throw 'Falha no dotnet publish.' }

# O pacote é só o .exe (D-011). Instruções de uso: src\Reparo.App\LEIA-ME.txt.
# Configuração opcional para técnicos: src\Reparo.App\reparo.exemplo.json.

Write-Host ''
Write-Host "Pacote pronto em: $destinoCompleto" -ForegroundColor Green
Get-ChildItem -LiteralPath $destinoCompleto | ForEach-Object {
    Write-Host ('  {0,-24} {1,8:N1} MB' -f $_.Name, ($_.Length / 1MB))
}
