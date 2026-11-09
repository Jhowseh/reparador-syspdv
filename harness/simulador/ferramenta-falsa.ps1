# =============================================================================
# Ferramenta falsa do harness.
# Simula gfix, gbak, net, tskill/taskkill e os executaveis do SysPDV
# SEM executar nada real. Chamada pelos wrappers harness\bin\*.cmd, que
# definem HARNESS_FERRAMENTA com o nome da ferramenta.
#
# Toda chamada e registrada (uma linha JSON) em <sandbox>\.harness\chamadas.jsonl.
# O "banco" e um arquivo de texto com um marcador, por exemplo:
#   SYSPDV-FDB;origem=ORIGINAL;id=<guid>
# =============================================================================
$ErrorActionPreference = 'Stop'

$ferramenta = ([string]$env:HARNESS_FERRAMENTA).ToLowerInvariant()
$sandbox    = [string]$env:HARNESS_SANDBOX
$argumentos = @($args | ForEach-Object { [string]$_ })

if ([string]::IsNullOrWhiteSpace($sandbox) -or -not (Test-Path -LiteralPath $sandbox -PathType Container)) {
    [Console]::Error.WriteLine("[harness] '$ferramenta' simulado foi chamado fora do harness (HARNESS_SANDBOX ausente). Nada foi executado.")
    exit 99
}

$sandbox     = [System.IO.Path]::GetFullPath($sandbox)
$dirHarness  = Join-Path $sandbox '.harness'
$arqChamadas = Join-Path $dirHarness 'chamadas.jsonl'
$arqServico  = Join-Path $dirHarness 'servico.txt'
$baseDir     = [string]$env:HARNESS_BASE_DIR
$cwd         = (Get-Location).Path

$cenario = $null
if ($env:HARNESS_CENARIO_ARQUIVO -and (Test-Path -LiteralPath $env:HARNESS_CENARIO_ARQUIVO)) {
    $cenario = [System.IO.File]::ReadAllText($env:HARNESS_CENARIO_ARQUIVO) | ConvertFrom-Json
}

$script:modo = 'outro'
$script:obs  = @()

# Opcoes de gfix/gbak que recebem um valor logo depois
$opcoesComValor = @(
    '-user', '-u', '-password', '-pas', '-pa', '-pass', '-role', '-ro', '-y', '-se', '-service',
    '-fe', '-fetch_password', '-p', '-page_size', '-bu', '-buffers', '-h', '-housekeeping',
    '-sql_dialect', '-mode', '-mo', '-attach', '-at', '-force', '-fo', '-tran', '-tr',
    '-par', '-parallel', '-cr', '-crypt', '-keyholder', '-kh'
)
$opcoesSenha = @('-password', '-pas', '-pa', '-pass')

# ----------------------------------------------------------------------------- utilitarios

function Read-Texto([string]$caminho) {
    if (-not (Test-Path -LiteralPath $caminho -PathType Leaf)) { return $null }
    return [System.IO.File]::ReadAllText($caminho)
}

function Write-Texto([string]$caminho, [string]$texto) {
    [System.IO.File]::WriteAllText($caminho, $texto)
}

function Get-Chamadas {
    if (-not (Test-Path -LiteralPath $arqChamadas)) { return @() }
    $linhas = [System.IO.File]::ReadAllLines($arqChamadas)
    return @($linhas | Where-Object { $_.Trim() } | ForEach-Object { $_ | ConvertFrom-Json })
}

function Get-ArgsMascarados {
    $saida = @()
    $mascarar = $false
    foreach ($t in $argumentos) {
        if ($mascarar) { $saida += '***'; $mascarar = $false; continue }
        $saida += $t
        if ($opcoesSenha -contains $t.ToLowerInvariant()) { $mascarar = $true }
    }
    return $saida
}

function Finalizar([int]$codigo, [string]$saida = '', [string]$erro = '') {
    if ($saida) { [Console]::Out.WriteLine($saida) }
    if ($erro)  { [Console]::Error.WriteLine($erro) }

    $registro = [ordered]@{
        seq        = (@(Get-Chamadas)).Count + 1
        hora       = (Get-Date).ToString('yyyy-MM-ddTHH:mm:ss.fff')
        ferramenta = $ferramenta
        modo       = $script:modo
        args       = @(Get-ArgsMascarados)
        cwd        = $cwd
        codigo     = $codigo
        obs        = @($script:obs)
    }
    $json = ConvertTo-Json -InputObject $registro -Compress -Depth 5

    # dbMonitor pode rodar em paralelo: tenta algumas vezes se o arquivo estiver em uso
    for ($tentativa = 0; $tentativa -lt 20; $tentativa++) {
        try {
            [System.IO.File]::AppendAllText($arqChamadas, $json + [Environment]::NewLine)
            break
        } catch [System.IO.IOException] {
            Start-Sleep -Milliseconds 100
        }
    }
    exit $codigo
}

function Test-Flag([string[]]$nomes) {
    foreach ($t in $argumentos) {
        if ($nomes -contains $t.ToLowerInvariant()) { return $true }
    }
    return $false
}

function Get-Posicionais {
    $pos = @()
    $i = 0
    while ($i -lt $argumentos.Count) {
        $t = $argumentos[$i]
        if ($t.StartsWith('-')) {
            if ($opcoesComValor -contains $t.ToLowerInvariant()) { $i++ }
        } else {
            $pos += $t
        }
        $i++
    }
    return $pos
}

# "localhost:C:\x.fdb" -> "C:\x.fdb"; caminho relativo -> relativo a pasta atual
function Resolve-CaminhoBanco([string]$valor) {
    $v = $valor.Trim('"') -replace '^[^\\/:]+(/\d+)?:(?=[A-Za-z]:[\\/])', ''
    if (-not [System.IO.Path]::IsPathRooted($v)) { $v = Join-Path $cwd $v }
    return [System.IO.Path]::GetFullPath($v)
}

function Test-DentroSandbox([string]$caminho) {
    $raiz = $sandbox.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    return $caminho.StartsWith($raiz, [System.StringComparison]::OrdinalIgnoreCase)
}

function Assert-DentroSandbox([string]$caminho) {
    if (-not (Test-DentroSandbox $caminho)) {
        $script:obs += "VIOLACAO: caminho fora do sandbox: $caminho"
        Finalizar 98 '' "[harness] BLOQUEADO: $ferramenta tentou usar um caminho fora do sandbox ($caminho). O app nao pode ter caminhos fixos."
    }
}

function Assert-Credenciais {
    $usuario = $null
    $senha = $null
    for ($i = 0; $i -lt ($argumentos.Count - 1); $i++) {
        $t = $argumentos[$i].ToLowerInvariant()
        if ($t -eq '-user' -or $t -eq '-u') { $usuario = $argumentos[$i + 1] }
        if ($opcoesSenha -contains $t) { $senha = $argumentos[$i + 1] }
    }
    if (-not $usuario) { $usuario = $env:ISC_USER }
    if (-not $senha)   { $senha = $env:ISC_PASSWORD }
    $ok = ($usuario -and $senha -and ($usuario -ieq $env:HARNESS_USUARIO_ESPERADO) -and ($senha -ceq $env:HARNESS_SENHA_ESPERADA))
    if (-not $ok) {
        $script:obs += 'credenciais invalidas'
        Finalizar 1 '' 'Your user name and password are not defined. Ask your database administrator to set up a Firebird login.'
    }
}

function Get-EstadoServico {
    $e = Read-Texto $arqServico
    if ($null -eq $e) { return 'iniciado' }
    return $e.Trim()
}

# Firebird 2.5 no Windows: gfix/gbak locais conversam com o servico. Servico parado = erro.
function Assert-ServicoAtivo {
    if ((Get-EstadoServico) -ne 'iniciado') {
        $script:obs += 'servico do Firebird parado'
        Finalizar 1 '' "Statement failed, SQLSTATE = 08001`nunavailable database"
    }
}

function Get-FalhaProgramada {
    if (-not $cenario -or -not $cenario.falhas) { return $null }
    $anteriores = @(Get-Chamadas | Where-Object { $_.ferramenta -eq $ferramenta -and $_.modo -eq $script:modo }).Count
    $ocorrenciaAtual = $anteriores + 1
    foreach ($f in @($cenario.falhas)) {
        if (([string]$f.ferramenta).ToLowerInvariant() -ne $ferramenta) { continue }
        if ($f.modo -and ([string]$f.modo) -ne $script:modo) { continue }
        if ($f.ocorrencia -and ([int]$f.ocorrencia) -ne $ocorrenciaAtual) { continue }
        return $f
    }
    return $null
}

function Invoke-FalhaSeProgramada([string]$efeitoArquivo = '', [string]$efeitoConteudo = '') {
    $falha = Get-FalhaProgramada
    if ($null -eq $falha) { return }
    if ($efeitoArquivo) { Write-Texto $efeitoArquivo $efeitoConteudo }
    $script:obs += 'falha programada pelo cenario'
    $codigo = 1
    if ($null -ne $falha.codigoSaida) { $codigo = [int]$falha.codigoSaida }
    Finalizar $codigo '' ([string]$falha.mensagem)
}

# ----------------------------------------------------------------------------- simulacao

try {
    switch ($ferramenta) {

        'gfix' {
            if (Test-Flag @('-m', '-mend')) { $script:modo = 'mend' }
            elseif (Test-Flag @('-v', '-validate')) { $script:modo = 'validar' }

            $pos = @(Get-Posicionais)
            if ($pos.Count -lt 1) { Finalizar 1 '' 'gfix: banco de dados nao informado' }
            $db = Resolve-CaminhoBanco $pos[-1]
            Assert-DentroSandbox $db
            Assert-Credenciais
            Assert-ServicoAtivo

            $conteudo = Read-Texto $db
            if ($null -eq $conteudo) {
                $script:obs += 'banco inexistente'
                Finalizar 1 '' "I/O error during `"CreateFile (open)`" operation for file `"$db`"`nError while trying to open file"
            }
            Invoke-FalhaSeProgramada
            if ($conteudo.StartsWith('SYSPDV-FDB-PARCIAL')) {
                $script:obs += 'banco parcial'
                Finalizar 1 '' "Summary of validation errors`n`n`tNumber of database page errors`t: 12"
            }
            Finalizar 0
        }

        'gbak' {
            $flagsRestore = @('-c', '-create', '-create_database', '-r', '-rep', '-replace', '-replace_database', '-recreate', '-recreate_database')
            $flagsSubstituir = @('-r', '-rep', '-replace', '-replace_database', '-recreate', '-recreate_database')
            if (Test-Flag @('-b', '-backup', '-backup_database')) { $script:modo = 'backup' }
            elseif (Test-Flag $flagsRestore) { $script:modo = 'restore' }
            $substituir = Test-Flag $flagsSubstituir
            $verboso = Test-Flag @('-v', '-verbose', '-verify')

            $pos = @(Get-Posicionais)
            if ($pos.Count -lt 2) { Finalizar 1 '' 'gbak: ERROR:requires both input and output filenames' }
            $origem  = Resolve-CaminhoBanco $pos[0]
            $destino = Resolve-CaminhoBanco $pos[1]
            Assert-DentroSandbox $origem
            Assert-DentroSandbox $destino
            Assert-Credenciais
            Assert-ServicoAtivo

            $pastaDestino = Split-Path -Parent $destino
            if (-not (Test-Path -LiteralPath $pastaDestino -PathType Container)) {
                $script:obs += 'pasta de destino inexistente'
                Finalizar 1 '' "gbak: ERROR:cannot open file $destino"
            }

            if ($script:modo -eq 'backup') {
                $conteudo = Read-Texto $origem
                if ($null -eq $conteudo) {
                    $script:obs += 'banco inexistente'
                    Finalizar 1 '' "gbak: ERROR:I/O error during `"CreateFile (open)`" operation for file `"$origem`"`ngbak:Exiting before completion due to errors"
                }
                Invoke-FalhaSeProgramada $destino 'SYSPDV-FBK-INCOMPLETO'
                if (-not $conteudo.StartsWith('SYSPDV-FDB;')) {
                    Write-Texto $destino 'SYSPDV-FBK-INCOMPLETO'
                    $script:obs += 'banco de origem invalido'
                    Finalizar 1 '' "gbak: ERROR:database file appears corrupt ()`ngbak:Exiting before completion due to errors"
                }
                Write-Texto $destino ('SYSPDV-FBK;' + $conteudo)
                $msg = ''
                if ($verboso) { $msg = "gbak:readied database $origem for backup`ngbak:creating file $destino`ngbak:writing data for table ...`ngbak:closing file, committing, and finishing." }
                Finalizar 0 $msg
            }

            if ($script:modo -eq 'restore') {
                $fbk = Read-Texto $origem
                if ($null -eq $fbk) {
                    $script:obs += 'backup inexistente'
                    Finalizar 1 '' "gbak: ERROR:cannot open backup file $origem`ngbak:Exiting before completion due to errors"
                }
                if ((Test-Path -LiteralPath $destino) -and -not $substituir) {
                    $script:obs += 'destino ja existe'
                    Finalizar 1 '' "gbak: ERROR:database $destino already exists.  To replace it, use the -REP switch`ngbak:Exiting before completion due to errors"
                }
                Invoke-FalhaSeProgramada $destino 'SYSPDV-FDB-PARCIAL'
                if (-not $fbk.StartsWith('SYSPDV-FBK;')) {
                    Write-Texto $destino 'SYSPDV-FDB-PARCIAL'
                    $script:obs += 'backup incompleto'
                    Finalizar 1 '' "gbak: ERROR:unexpected end of file on backup file $origem`ngbak:Exiting before completion due to errors"
                }
                $id = ''
                if ($fbk -match 'id=([^;\s]+)') { $id = $Matches[1] }
                Write-Texto $destino "SYSPDV-FDB;origem=RESTAURADO;id=$id"
                $msg = ''
                if ($verboso) { $msg = "gbak:opened file $origem`ngbak:created database $destino`ngbak:restoring data for table ...`ngbak:finishing, closing, and going home" }
                Finalizar 0 $msg
            }

            Finalizar 0
        }

        'net' {
            $sub = ''
            if ($argumentos.Count -gt 0) { $sub = $argumentos[0].ToLowerInvariant() }
            $script:modo = $sub
            if ($sub -ne 'stop' -and $sub -ne 'start') { Finalizar 0 }

            $nome = (@($argumentos | Select-Object -Skip 1) -join ' ').Trim('"', ' ')
            if ($nome -ine [string]$env:HARNESS_SERVICO) {
                $script:obs += "servico desconhecido: $nome"
                Finalizar 2 '' "System error 1060 has occurred.`n`nThe specified service does not exist as an installed service."
            }
            Invoke-FalhaSeProgramada
            $estado = Get-EstadoServico
            if ($sub -eq 'stop') {
                if ($estado -eq 'parado') { Finalizar 2 '' "The $nome service is not started.`n`nMore help is available by typing NET HELPMSG 3521." }
                Write-Texto $arqServico 'parado'
                Finalizar 0 "The $nome service is stopping.`nThe $nome service was stopped successfully."
            }
            if ($estado -eq 'iniciado') { Finalizar 2 '' "The requested service has already been started.`n`nMore help is available by typing NET HELPMSG 2182." }
            Write-Texto $arqServico 'iniciado'
            Finalizar 0 "The $nome service is starting.`nThe $nome service was started successfully."
        }

        { @('tskill', 'taskkill') -contains $_ } {
            $script:modo = 'matar'
            Invoke-FalhaSeProgramada
            Finalizar 0
        }

        { @('verificaindice', 'atualizasyspdv_server', 'dbmonitor', 'syspdv_server') -contains $_ } {
            $script:modo = 'executar'
            Invoke-FalhaSeProgramada
            if ($ferramenta -ne 'dbmonitor') {
                $fdb = Join-Path $baseDir 'syspdv_srv.fdb'
                $c = Read-Texto $fdb
                if ($null -eq $c -or -not $c.StartsWith('SYSPDV-FDB;')) {
                    $script:obs += 'banco ausente ou invalido'
                    Finalizar 1 '' "[$ferramenta simulado] Nao foi possivel abrir o banco $fdb"
                }
                if ((Get-EstadoServico) -ne 'iniciado') {
                    $script:obs += 'servico do Firebird parado'
                    Finalizar 1 '' "[$ferramenta simulado] Servidor Firebird indisponivel"
                }
            }
            Finalizar 0 "[$ferramenta simulado] OK"
        }

        default {
            $script:obs += 'ferramenta desconhecida pelo simulador'
            Finalizar 0
        }
    }
} catch {
    $script:obs += "erro interno do simulador: $($_.Exception.Message)"
    Finalizar 97 '' "[harness] erro interno do simulador: $($_.Exception.Message)"
}
