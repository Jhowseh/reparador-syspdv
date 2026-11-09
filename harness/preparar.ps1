# =============================================================================
# Cria um sandbox limpo para um cenario: uma copia falsa de C:\SYSPDV.
# Uso interno de executar.ps1. Devolve (Write-Output) o caminho do sandbox.
#
# harness\.sandbox\<cenario>\
#   .harness\            estado do harness (cenario, chamadas, servico, relatorio)
#   C\SYSPDV\            pasta base falsa (REPARO_BASE_DIR)
#   C\SYSPDV\bkpBd\      backups antigos (um deles nao pode ser perdido)
#   C\OUTRO\, C\         arquivos fora da pasta base (nao podem ser tocados)
#   logs\                REPARO_LOG_DIR
# =============================================================================
param(
    [Parameter(Mandatory = $true)][string]$Cenario
)
$ErrorActionPreference = 'Stop'

$raizHarness = $PSScriptRoot
$arqCenario  = Join-Path $raizHarness "cenarios\$Cenario.json"
if (-not (Test-Path -LiteralPath $arqCenario)) { throw "Cenario nao encontrado: $arqCenario" }
$cfg = [System.IO.File]::ReadAllText($arqCenario) | ConvertFrom-Json

$raizSandbox = Join-Path $raizHarness '.sandbox'
$sandbox     = Join-Path $raizSandbox $Cenario
if (Test-Path -LiteralPath $sandbox) { Remove-Item -LiteralPath $sandbox -Recurse -Force }

$dirHarness = Join-Path $sandbox '.harness'
$disco      = Join-Path $sandbox 'C'
$base       = Join-Path $disco 'SYSPDV'
$bkp        = Join-Path $base 'bkpBd'
$outro      = Join-Path $disco 'OUTRO'
$logs       = Join-Path $sandbox 'logs'
foreach ($d in @($dirHarness, $base, $bkp, $outro, $logs)) {
    New-Item -ItemType Directory -Force -Path $d | Out-Null
}

function Escrever([string]$relativo, [string]$texto) {
    [System.IO.File]::WriteAllText((Join-Path $sandbox $relativo), $texto)
}

$idOriginal = [guid]::NewGuid().ToString('N')
$idAnterior = [guid]::NewGuid().ToString('N')

# Banco atual e um backup antigo com o MESMO nome (como acontece com o MOVE do script)
Escrever 'C\SYSPDV\syspdv_srv.fdb'       "SYSPDV-FDB;origem=ORIGINAL;id=$idOriginal"
Escrever 'C\SYSPDV\bkpBd\syspdv_srv.fdb' "SYSPDV-FDB;origem=BACKUP-ANTERIOR;id=$idAnterior"

# Arquivos que nao podem ser apagados nem alterados
$protegidos = [ordered]@{
    'C\SYSPDV\syspdv.ini'                = "[Config]`r`nServidor=localhost`r`nBanco=C:\SYSPDV\syspdv_srv.fdb`r`n"
    'C\SYSPDV\fbclient.dll'              = 'dll falsa do harness'
    'C\SYSPDV\VERIFICAINDICE.EXE'        = 'executavel falso do harness'
    'C\SYSPDV\atualizasyspdv_server.exe' = 'executavel falso do harness'
    'C\SYSPDV\dbMonitor.exe'             = 'executavel falso do harness'
    'C\SYSPDV\syspdv_server.exe'         = 'executavel falso do harness'
    'C\SYSPDV\bkpBd\leia-me.txt'         = 'Subpasta: a limpeza nao pode entrar aqui.'
    'C\OUTRO\importante.txt'             = 'Fora da pasta base: nao pode ser apagado.'
    'C\importante.log'                   = 'Fora da pasta base (raiz do disco): nao pode ser apagado.'
}
foreach ($k in $protegidos.Keys) { Escrever $k $protegidos[$k] }

# Lixo que a limpeza (srv*, *log, *err, banc*, *txt) deve remover da pasta base
$lixo = @(
    'C\SYSPDV\srv0001.tmp',
    'C\SYSPDV\syspdv.log',
    'C\SYSPDV\erros.err',
    'C\SYSPDV\banco_aux.tmp',
    'C\SYSPDV\relatorio.txt'
)
foreach ($l in $lixo) { Escrever $l 'arquivo temporario' }

$listaProtegidos = @()
foreach ($k in $protegidos.Keys) {
    $hash = (Get-FileHash -LiteralPath (Join-Path $sandbox $k) -Algorithm SHA256).Hash
    $listaProtegidos += [ordered]@{ caminho = $k; hash = $hash }
}

$servicoInicial = 'iniciado'
if ($cfg.servicoInicial) { $servicoInicial = [string]$cfg.servicoInicial }
[System.IO.File]::WriteAllText((Join-Path $dirHarness 'servico.txt'), $servicoInicial)
Copy-Item -LiteralPath $arqCenario -Destination (Join-Path $dirHarness 'cenario.json')

$estado = [ordered]@{
    cenario    = $Cenario
    sandbox    = $sandbox
    baseDir    = $base
    logDir     = $logs
    usuario    = 'SYSDBA'
    senha      = 'Harness-S3nh@-' + $idOriginal.Substring(0, 6)
    servico    = 'FirebirdServerDefaultInstance'
    idOriginal = $idOriginal
    idAnterior = $idAnterior
    protegidos = $listaProtegidos
    lixo       = $lixo
}
$json = ConvertTo-Json -InputObject $estado -Depth 5
[System.IO.File]::WriteAllText((Join-Path $dirHarness 'estado-inicial.json'), $json)

Write-Output $sandbox
