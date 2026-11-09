# =============================================================================
# Verifica o resultado de um cenario: le as chamadas registradas pelo simulador
# e o estado final do sandbox, e confere as regras de seguranca e o resultado.
# Uso interno de executar.ps1. Codigo de saida: 0 = passou; 1 = falhou.
# Gera <sandbox>\.harness\relatorio.md e relatorio.json.
# =============================================================================
param(
    [Parameter(Mandatory = $true)][string]$Sandbox,
    [Parameter(Mandatory = $true)][int]$CodigoSaidaApp,
    [switch]$TempoEsgotado,
    [int]$TimeoutSegundos = 0
)
$ErrorActionPreference = 'Stop'

$Sandbox    = [System.IO.Path]::GetFullPath($Sandbox)
$dirHarness = Join-Path $Sandbox '.harness'
$estado     = [System.IO.File]::ReadAllText((Join-Path $dirHarness 'estado-inicial.json')) | ConvertFrom-Json
$cenario    = [System.IO.File]::ReadAllText((Join-Path $dirHarness 'cenario.json')) | ConvertFrom-Json

$arqChamadas = Join-Path $dirHarness 'chamadas.jsonl'
$chamadas = @()
if (Test-Path -LiteralPath $arqChamadas) {
    $chamadas = @([System.IO.File]::ReadAllLines($arqChamadas) | Where-Object { $_.Trim() } | ForEach-Object { $_ | ConvertFrom-Json })
}

$resultados = New-Object System.Collections.ArrayList
function Registrar([string]$id, [string]$titulo, [bool]$ok, [string]$detalhe = '') {
    [void]$resultados.Add([pscustomobject]@{ id = $id; titulo = $titulo; ok = $ok; detalhe = $detalhe })
}

function Ler([string]$caminho) {
    if (Test-Path -LiteralPath $caminho -PathType Leaf) { return [System.IO.File]::ReadAllText($caminho) }
    return $null
}

function Resumir([string]$texto) {
    if ($null -eq $texto) { return '(arquivo nao existe)' }
    if ($texto.Length -gt 60) { return $texto.Substring(0, 60) + '...' }
    return $texto
}

$prefixoHarness = $dirHarness.TrimEnd('\') + '\'
$arquivosSandbox = @(Get-ChildItem -LiteralPath $Sandbox -Recurse -File -Force |
    Where-Object { -not $_.FullName.StartsWith($prefixoHarness, [System.StringComparison]::OrdinalIgnoreCase) })

function Find-ArquivosComPrefixo([string]$prefixo) {
    $achados = @()
    foreach ($a in $arquivosSandbox) {
        if ($a.Length -gt 1MB) { continue }
        $t = [System.IO.File]::ReadAllText($a.FullName)
        if ($t.StartsWith($prefixo)) { $achados += $a.FullName.Substring($Sandbox.Length + 1) }
    }
    return $achados
}

$marcaOriginal   = "SYSPDV-FDB;origem=ORIGINAL;id=$($estado.idOriginal)"
$marcaRestaurado = "SYSPDV-FDB;origem=RESTAURADO;id=$($estado.idOriginal)"
$marcaAnterior   = "SYSPDV-FDB;origem=BACKUP-ANTERIOR;id=$($estado.idAnterior)"
$fdbFinal        = Join-Path $estado.baseDir 'syspdv_srv.fdb'
$conteudoFinal   = Ler $fdbFinal

# ----------------------------------------------------------------------------- S: uso do harness

$violacoes = @($chamadas | Where-Object { @($_.obs) -match '^VIOLACAO' } | ForEach-Object { "#$($_.seq) $($_.ferramenta): $(@($_.obs) -join '; ')" })
Registrar 'S1' 'Nenhuma ferramenta apontou para fora do sandbox' ($violacoes.Count -eq 0) ($violacoes -join ' | ')

Registrar 'S2' 'O app executou as ferramentas pelo harness' ($chamadas.Count -gt 0) `
    'Nenhuma chamada registrada. O app precisa executar as ferramentas a partir de REPARO_FERRAMENTAS_DIR (<dir>\<nome>.cmd).'

$credInvalidas = @($chamadas | Where-Object { @($_.obs) -contains 'credenciais invalidas' } | ForEach-Object { "#$($_.seq) $($_.ferramenta)" })
Registrar 'S3' 'Credenciais vieram da configuracao' ($credInvalidas.Count -eq 0) `
    ("Chamadas com usuario/senha errados: $($credInvalidas -join ', '). Use REPARO_DB_USUARIO/REPARO_DB_SENHA, nunca SYSDBA/masterkey fixos.")

$svcErrado = @($chamadas | Where-Object { @($_.obs) -match '^servico desconhecido' } | ForEach-Object { "#$($_.seq) $(@($_.obs) -join '; ')" })
Registrar 'S4' 'Nome do servico veio da configuracao' ($svcErrado.Count -eq 0) ($svcErrado -join ' | ')

# ----------------------------------------------------------------------------- I: invariantes de seguranca

$achadosOriginal = @(Find-ArquivosComPrefixo $marcaOriginal)
Registrar 'I1' 'O banco original foi preservado (no lugar ou em backup)' ($achadosOriginal.Count -gt 0) `
    'O arquivo original do banco sumiu ou foi sobrescrito.'

$achadosAnterior = @(Find-ArquivosComPrefixo $marcaAnterior)
Registrar 'I2' 'Backup anterior em bkpBd nao foi sobrescrito' ($achadosAnterior.Count -gt 0) `
    'O backup antigo C\SYSPDV\bkpBd\syspdv_srv.fdb foi perdido. Use data e hora no nome do backup.'

$finalValido = ($null -ne $conteudoFinal) -and ($conteudoFinal.StartsWith($marcaOriginal) -or $conteudoFinal.StartsWith($marcaRestaurado))
Registrar 'I3' 'Ao final existe um banco valido no lugar certo' $finalValido `
    "Conteudo de syspdv_srv.fdb: $(Resumir $conteudoFinal)"

$problemasOrdem = @()
$backupOk = $false
$restoreOk = $false
foreach ($c in $chamadas) {
    if ($c.ferramenta -eq 'gbak' -and $c.modo -eq 'backup') {
        $backupOk = ([int]$c.codigo -eq 0)
        $restoreOk = $false
    } elseif ($c.ferramenta -eq 'gbak' -and $c.modo -eq 'restore') {
        if (-not $backupOk) { $problemasOrdem += "#$($c.seq) restore executado sem um backup bem-sucedido antes" }
        $restoreOk = ([int]$c.codigo -eq 0)
    } elseif (@('verificaindice', 'atualizasyspdv_server') -contains $c.ferramenta) {
        if (-not $restoreOk) { $problemasOrdem += "#$($c.seq) $($c.ferramenta) executado sem um restore bem-sucedido antes" }
    }
}
Registrar 'I4' 'Parou depois de falha critica (nao seguiu com backup/restore quebrado)' ($problemasOrdem.Count -eq 0) ($problemasOrdem -join ' | ')

$protegidosAlterados = @()
foreach ($p in @($estado.protegidos)) {
    $caminho = Join-Path $Sandbox $p.caminho
    if (-not (Test-Path -LiteralPath $caminho -PathType Leaf)) { $protegidosAlterados += "$($p.caminho) (apagado)"; continue }
    $hash = (Get-FileHash -LiteralPath $caminho -Algorithm SHA256).Hash
    if ($hash -ne $p.hash) { $protegidosAlterados += "$($p.caminho) (alterado)" }
}
Registrar 'I5' 'Nada fora do escopo foi apagado ou alterado' ($protegidosAlterados.Count -eq 0) ($protegidosAlterados -join ', ')

$servicoFinal = (Ler (Join-Path $dirHarness 'servico.txt'))
if ($servicoFinal) { $servicoFinal = $servicoFinal.Trim() }
Registrar 'I6' 'Servico do Firebird ficou rodando no final' ($servicoFinal -eq 'iniciado') "Estado final do servico: $servicoFinal"

$senha = [string]$estado.senha
$vazamentos = @()
$alvosSenha = @()
$arqSaida = Join-Path $dirHarness 'saida-app.txt'
if (Test-Path -LiteralPath $arqSaida) { $alvosSenha += $arqSaida }
if (Test-Path -LiteralPath $estado.logDir) {
    $alvosSenha += @(Get-ChildItem -LiteralPath $estado.logDir -Recurse -File -Force | ForEach-Object { $_.FullName })
}
foreach ($a in $alvosSenha) {
    if ([System.IO.File]::ReadAllText($a).Contains($senha)) { $vazamentos += $a.Substring($Sandbox.Length + 1) }
}
Registrar 'I7' 'A senha nao aparece na saida nem nos logs do app' ($vazamentos.Count -eq 0) ("Senha encontrada em: $($vazamentos -join ', ')")

# ----------------------------------------------------------------------------- F: resultado esperado

$sucessoEsperado = [bool]$cenario.esperado.sucesso
if ($TempoEsgotado) {
    Registrar 'F1' 'O app terminou com o codigo de saida esperado' $false `
        "O app nao terminou em $TimeoutSegundos s. Ele esta esperando entrada? Respeite REPARO_NAO_INTERATIVO=1."
} else {
    $codigoOk = $false
    if ($sucessoEsperado) { $codigoOk = ($CodigoSaidaApp -eq 0) } else { $codigoOk = ($CodigoSaidaApp -ne 0) }
    $esperadoTxt = 'diferente de 0 (falha)'
    if ($sucessoEsperado) { $esperadoTxt = '0 (sucesso)' }
    Registrar 'F1' 'O app terminou com o codigo de saida esperado' $codigoOk "Esperado: $esperadoTxt. Recebido: $CodigoSaidaApp."
}

$bancoEsperado = [string]$cenario.esperado.bancoFinal
$marcaEsperada = $marcaOriginal
if ($bancoEsperado -eq 'restaurado') { $marcaEsperada = $marcaRestaurado }
$bancoOk = ($null -ne $conteudoFinal) -and $conteudoFinal.StartsWith($marcaEsperada)
Registrar 'F2' "Banco final e o $bancoEsperado" $bancoOk "Conteudo de syspdv_srv.fdb: $(Resumir $conteudoFinal)"

if ($sucessoEsperado) {
    $sobrou = @(@($estado.lixo) | Where-Object { Test-Path -LiteralPath (Join-Path $Sandbox $_) })
    Registrar 'F3' 'Limpeza removeu os temporarios (srv*, *log, *err, banc*, *txt)' ($sobrou.Count -eq 0) ("Sobraram: $($sobrou -join ', ')")

    function Get-Indice([string]$ferr, [string]$modo) {
        for ($i = 0; $i -lt $chamadas.Count; $i++) {
            $c = $chamadas[$i]
            if ($c.ferramenta -eq $ferr -and ((-not $modo) -or $c.modo -eq $modo) -and [int]$c.codigo -eq 0) { return $i }
        }
        return -1
    }
    $etapas = @(
        @{ nome = 'gbak backup';           i = (Get-Indice 'gbak' 'backup') },
        @{ nome = 'gbak restore';          i = (Get-Indice 'gbak' 'restore') },
        @{ nome = 'VERIFICAINDICE';        i = (Get-Indice 'verificaindice' '') },
        @{ nome = 'atualizasyspdv_server'; i = (Get-Indice 'atualizasyspdv_server' '') },
        @{ nome = 'syspdv_server';         i = (Get-Indice 'syspdv_server' '') }
    )
    $problemasFluxo = @()
    $anterior = -1
    foreach ($e in $etapas) {
        if ($e.i -lt 0) { $problemasFluxo += "$($e.nome) nao executou com sucesso"; continue }
        if ($e.i -lt $anterior) { $problemasFluxo += "$($e.nome) fora de ordem" }
        $anterior = $e.i
    }
    $mend = Get-Indice 'gfix' 'mend'
    $idxBackup = $etapas[0].i
    if ($mend -ge 0 -and $idxBackup -ge 0 -and $mend -gt $idxBackup) { $problemasFluxo += 'gfix -mend depois do backup' }
    Registrar 'F4' 'Fluxo completo e na ordem (backup > restore > indices > atualizacao > sistema)' ($problemasFluxo.Count -eq 0) ($problemasFluxo -join ' | ')
}

# ----------------------------------------------------------------------------- relatorio

$passou = (@($resultados | Where-Object { -not $_.ok }).Count -eq 0)

Write-Host ''
Write-Host "Verificacao - $($cenario.nome): $($cenario.descricao)"
foreach ($r in $resultados) {
    if ($r.ok) {
        Write-Host ("  [ OK  ] {0} {1}" -f $r.id, $r.titulo) -ForegroundColor Green
    } else {
        Write-Host ("  [FALHA] {0} {1}" -f $r.id, $r.titulo) -ForegroundColor Red
        if ($r.detalhe) { Write-Host "          $($r.detalhe)" -ForegroundColor Yellow }
    }
}

$md = New-Object System.Text.StringBuilder
[void]$md.AppendLine("# Relatorio do harness - cenario ``$($cenario.nome)``")
[void]$md.AppendLine('')
[void]$md.AppendLine($cenario.descricao)
[void]$md.AppendLine('')
$resultadoTxt = 'FALHOU'
if ($passou) { $resultadoTxt = 'PASSOU' }
[void]$md.AppendLine("**Resultado: $resultadoTxt**  |  codigo de saida do app: $CodigoSaidaApp")
[void]$md.AppendLine('')
[void]$md.AppendLine('| Checagem | Status | Detalhe |')
[void]$md.AppendLine('|---|---|---|')
foreach ($r in $resultados) {
    $st = 'FALHA'
    if ($r.ok) { $st = 'ok' }
    $det = ''
    if (-not $r.ok) { $det = ($r.detalhe -replace '\|', '/') }
    [void]$md.AppendLine("| $($r.id) $($r.titulo) | $st | $det |")
}
[void]$md.AppendLine('')
[void]$md.AppendLine('## Chamadas feitas pelo app (em ordem)')
[void]$md.AppendLine('')
[void]$md.AppendLine('| # | Ferramenta | Modo | Argumentos | Pasta | Codigo | Observacoes |')
[void]$md.AppendLine('|---|---|---|---|---|---|---|')
foreach ($c in $chamadas) {
    [void]$md.AppendLine("| $($c.seq) | $($c.ferramenta) | $($c.modo) | $(@($c.args) -join ' ') | $($c.cwd) | $($c.codigo) | $(@($c.obs) -join '; ') |")
}
[void]$md.AppendLine('')
[void]$md.AppendLine('## Arquivos no sandbox ao final')
[void]$md.AppendLine('')
foreach ($a in $arquivosSandbox) {
    $conteudo = ''
    if ($a.Length -le 200) { $conteudo = Resumir ([System.IO.File]::ReadAllText($a.FullName)) }
    $conteudo = $conteudo -replace "`r?`n", ' '
    [void]$md.AppendLine("- ``$($a.FullName.Substring($Sandbox.Length + 1))`` - $conteudo")
}
[System.IO.File]::WriteAllText((Join-Path $dirHarness 'relatorio.md'), $md.ToString())

$jsonRel = ConvertTo-Json -InputObject ([ordered]@{
    cenario = $cenario.nome
    passou = $passou
    codigoSaidaApp = $CodigoSaidaApp
    tempoEsgotado = [bool]$TempoEsgotado
    checagens = @($resultados)
}) -Depth 5
[System.IO.File]::WriteAllText((Join-Path $dirHarness 'relatorio.json'), $jsonRel)

if ($passou) { exit 0 }
exit 1
