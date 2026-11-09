# =============================================================================
# Gera imagens da tela do reparador (tema claro e escuro, em cada estado) SEM
# executar reparo nenhum: a janela é montada e preenchida com dados de exemplo.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/previa-tela.ps1
#
# Saída: docs\previa-tela\*.png (usadas no README).
# =============================================================================
param(
    [string]$Saida = 'docs\previa-tela'
)
$ErrorActionPreference = 'Stop'

$raiz = Split-Path -Parent $PSScriptRoot
$destino = [System.IO.Path]::GetFullPath((Join-Path $raiz $Saida))
if (-not $destino.StartsWith($raiz + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "A pasta de saida precisa ficar dentro do repositorio: $destino"
}

$env:REPARO_PREVIA_DIR = $destino
try {
    dotnet test (Join-Path $raiz 'Reparo.sln') --nologo -v q --filter 'FullyQualifiedName~PreviaDaTela'
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao gerar as previas.' }
} finally {
    Remove-Item Env:\REPARO_PREVIA_DIR -ErrorAction SilentlyContinue
}

Write-Host "Previas em: $destino" -ForegroundColor Green
Get-ChildItem -LiteralPath $destino -Filter *.png | ForEach-Object { Write-Host "  $($_.Name)" }
