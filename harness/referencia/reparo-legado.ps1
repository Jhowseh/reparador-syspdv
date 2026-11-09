# =============================================================================
# Copia FIEL do docs/original/REPARO.CMD, para autoteste do harness.
# Mesmos passos, mesma ordem, mesma falta de checagem de erros.
# Unicas diferencas:
#   - caminhos, usuario, senha e servico vem das variaveis REPARO_* (para rodar no sandbox);
#   - sem "pause" e sem "cls" (modo nao interativo);
#   - "start dbMonitor.exe" roda esperando terminar (o simulador termina na hora).
# Resultado esperado: o harness REPROVA este script em varios cenarios. Isso e proposital:
# mostra que o harness pega os problemas do script original.
# =============================================================================
$ErrorActionPreference = 'Continue'

$base    = $env:REPARO_BASE_DIR
$bin     = $env:REPARO_FERRAMENTAS_DIR
$usuario = $env:REPARO_DB_USUARIO
$senha   = $env:REPARO_DB_SENHA
$servico = $env:REPARO_SERVICO_FIREBIRD

function F([string]$nome, [string[]]$argumentos) {
    & (Join-Path $bin "$nome.cmd") @argumentos
    Write-Host "    ($nome terminou com codigo $LASTEXITCODE - o script original ignora)"
}

function ReiniciarServico {
    F 'net' @('stop', $servico)
    F 'net' @('start', $servico)
}

F 'tskill' @('fbserver.exe')                                         # @tskill fbserver.exe
Set-Location -LiteralPath $base                                      # c: / CD\SYSPDV
foreach ($padrao in @('srv*', '*log', '*err', 'banc*', '*txt')) {    # del srv* / *log / *err / banc* / *txt
    Get-ChildItem -LiteralPath $base -File -Filter $padrao | Remove-Item -Force
}
ReiniciarServico

Write-Host 'Verificando integridade do sistema... I / III'
F 'gfix' @('-v', '-f', '-user', $usuario, '-password', $senha, 'syspdv_srv.fdb')
ReiniciarServico

Write-Host 'Verificando integridade do sistema... II / III'
F 'gfix' @('-m', '-f', '-i', '-user', $usuario, '-password', $senha, 'syspdv_srv.fdb')
ReiniciarServico

Write-Host 'Verificando integridade do sistema... III / III'
F 'gfix' @('-v', '-f', '-user', $usuario, '-password', $senha, 'syspdv_srv.fdb')
# @pause

Write-Host 'Iniciando backup...'
ReiniciarServico
F 'gbak' @('-b', '-v', '-i', '-g', '-l', '-user', $usuario, '-password', $senha, 'syspdv_srv.fdb', 'syspdv_srv.fbk')
# pause
F 'net' @('stop', $servico)

# @MOVE SYSPDV_SRV.FDB bkpBd  -> dentro de um .cmd, MOVE sobrescreve sem perguntar.
# Se bkpBd for pasta, vai para bkpBd\SYSPDV_SRV.FDB; senao, vira um arquivo chamado "bkpBd".
$origem = Join-Path $base 'SYSPDV_SRV.FDB'
$destino = Join-Path $base 'bkpBd'
if (Test-Path -LiteralPath $destino -PathType Container) { $destino = Join-Path $destino 'SYSPDV_SRV.FDB' }
if (Test-Path -LiteralPath $origem) {
    if (Test-Path -LiteralPath $destino) { Remove-Item -LiteralPath $destino -Force }
    Move-Item -LiteralPath $origem -Destination $destino
}

Write-Host 'Iniciando restore...'
F 'net' @('start', $servico)
F 'gbak' @('-c', '-v', '-i', '-user', $usuario, '-password', $senha, 'syspdv_srv.fbk', 'syspdv_srv.fdb')

Write-Host 'Ativando indices...'
F 'VERIFICAINDICE' @()

Write-Host 'Atualizando banco de dados...'
F 'dbMonitor' @()     # @start dbMonitor.exe (no original nao espera; o simulador termina na hora)
F 'atualizasyspdv_server' @()

Write-Host 'Abrindo o sistema...'
F 'syspdv_server' @()                                                # @start syspdv_server

exit 0
