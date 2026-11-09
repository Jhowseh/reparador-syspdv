# =============================================================================
# Roda o app (ou a copia do script legado) em um ou mais cenarios simulados
# e verifica as regras de seguranca.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File harness/executar.ps1 -Alvo app
#   powershell -NoProfile -ExecutionPolicy Bypass -File harness/executar.ps1 -Alvo app -Cenario falha-backup
#   powershell -NoProfile -ExecutionPolicy Bypass -File harness/executar.ps1 -Alvo legado
#
# Codigo de saida: 0 = todos os cenarios passaram; 1 = algum falhou; 2 = erro de uso.
# =============================================================================
param(
    [ValidateSet('app', 'legado')][string]$Alvo = 'app',
    [string]$Cenario = 'todos',
    [int]$TimeoutSegundos = 0
)
$ErrorActionPreference = 'Stop'

$raizHarness = $PSScriptRoot
$raizRepo    = Split-Path -Parent $raizHarness
$bin         = Join-Path $raizHarness 'bin'
$cfg         = [System.IO.File]::ReadAllText((Join-Path $raizHarness 'harness.config.json')) | ConvertFrom-Json

if ($TimeoutSegundos -le 0) { $TimeoutSegundos = [int]$cfg.timeoutSegundos }
if ($TimeoutSegundos -le 0) { $TimeoutSegundos = 300 }

if ($Alvo -eq 'legado') {
    $comando = 'powershell.exe -NoProfile -ExecutionPolicy Bypass -File "' + (Join-Path $raizHarness 'referencia\reparo-legado.ps1') + '"'
} else {
    $comando = [string]$cfg.comandoApp
    if ([string]::IsNullOrWhiteSpace($comando)) {
        Write-Host 'comandoApp esta vazio em harness/harness.config.json.' -ForegroundColor Yellow
        Write-Host 'Defina o comando que inicia o app em modo nao interativo (o /planejar-app faz isso).'
        exit 2
    }
}

$dirCenarios = Join-Path $raizHarness 'cenarios'
if ($Cenario -eq 'todos') {
    $ordem = @('normal', 'banco-corrompido', 'servico-parado', 'falha-backup', 'falha-restore')
    $existentes = @(Get-ChildItem -LiteralPath $dirCenarios -Filter '*.json' | ForEach-Object { $_.BaseName })
    $cenarios = @($ordem | Where-Object { $existentes -contains $_ }) + @($existentes | Where-Object { $ordem -notcontains $_ } | Sort-Object)
} else {
    $cenarios = @($Cenario -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
}

$cmdExe = $env:ComSpec
if (-not $cmdExe) { $cmdExe = 'cmd.exe' }

$resumo = @()
foreach ($c in $cenarios) {
    Write-Host ''
    Write-Host ('=' * 78) -ForegroundColor Cyan
    Write-Host "Cenario: $c   (alvo: $Alvo)" -ForegroundColor Cyan
    Write-Host ('=' * 78) -ForegroundColor Cyan

    $sandbox = @(& (Join-Path $raizHarness 'preparar.ps1') -Cenario $c)[-1]
    $dirHarness = Join-Path $sandbox '.harness'
    $estado = [System.IO.File]::ReadAllText((Join-Path $dirHarness 'estado-inicial.json')) | ConvertFrom-Json
    $arqSaida = Join-Path $dirHarness 'saida-app.txt'

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $cmdExe
    # /s: o cmd remove so a primeira e a ultima aspas; stdin vazio para nenhum "pause" travar
    $psi.Arguments = '/d /s /c "' + $comando + ' > "' + $arqSaida + '" 2>&1 < NUL"'
    $psi.WorkingDirectory = $raizRepo
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true

    $vars = [ordered]@{
        # contrato do app
        REPARO_BASE_DIR          = $estado.baseDir
        REPARO_FERRAMENTAS_DIR   = $bin
        REPARO_DB_USUARIO        = $estado.usuario
        REPARO_DB_SENHA          = $estado.senha
        REPARO_SERVICO_FIREBIRD  = $estado.servico
        REPARO_LOG_DIR           = $estado.logDir
        REPARO_NAO_INTERATIVO    = '1'
        # uso interno do simulador
        HARNESS_SANDBOX          = $sandbox
        HARNESS_BASE_DIR         = $estado.baseDir
        HARNESS_CENARIO_ARQUIVO  = (Join-Path $dirHarness 'cenario.json')
        HARNESS_USUARIO_ESPERADO = $estado.usuario
        HARNESS_SENHA_ESPERADA   = $estado.senha
        HARNESS_SERVICO          = $estado.servico
    }
    foreach ($k in $vars.Keys) { $psi.EnvironmentVariables[$k] = [string]$vars[$k] }
    # Rede de seguranca: mesmo que o app chame "gfix" ou "net" sem caminho, acha o simulador primeiro
    $psi.EnvironmentVariables['PATH'] = $bin + ';' + $psi.EnvironmentVariables['PATH']

    $inicio = Get-Date
    $proc = [System.Diagnostics.Process]::Start($psi)
    $esgotado = $false
    if (-not $proc.WaitForExit($TimeoutSegundos * 1000)) {
        $esgotado = $true
        $null = & $cmdExe /d /c "taskkill /PID $($proc.Id) /T /F >NUL 2>&1"
        $codigo = -1
    } else {
        $codigo = $proc.ExitCode
    }
    $duracao = [int]((Get-Date) - $inicio).TotalSeconds

    Write-Host "Saida do $Alvo (codigo $codigo, ${duracao}s):" -ForegroundColor DarkGray
    if (Test-Path -LiteralPath $arqSaida) {
        Get-Content -LiteralPath $arqSaida | ForEach-Object { Write-Host "  | $_" -ForegroundColor DarkGray }
    }

    & (Join-Path $raizHarness 'verificar.ps1') -Sandbox $sandbox -CodigoSaidaApp $codigo -TempoEsgotado:$esgotado -TimeoutSegundos $TimeoutSegundos
    $passou = ($LASTEXITCODE -eq 0)
    $resumo += [pscustomobject]@{ Cenario = $c; Resultado = $(if ($passou) { 'PASSOU' } else { 'FALHOU' }); Relatorio = (Join-Path $dirHarness 'relatorio.md') }
}

Write-Host ''
Write-Host ('=' * 78) -ForegroundColor Cyan
Write-Host "RESUMO (alvo: $Alvo)" -ForegroundColor Cyan
foreach ($r in $resumo) {
    $cor = 'Green'
    if ($r.Resultado -ne 'PASSOU') { $cor = 'Red' }
    Write-Host ('  {0,-20} {1}' -f $r.Cenario, $r.Resultado) -ForegroundColor $cor
}
Write-Host ''
Write-Host 'Relatorios detalhados: harness\.sandbox\<cenario>\.harness\relatorio.md'

if (@($resumo | Where-Object { $_.Resultado -ne 'PASSOU' }).Count -gt 0) { exit 1 }
exit 0
