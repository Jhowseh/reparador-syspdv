# Harness de testes do Reparo SysPDV

Ambiente simulado para testar o app **sem tocar em banco, serviço ou pasta reais**.
Funciona com qualquer stack (C#, Python, Go, Delphi...), porque fala com o app só por
variáveis de ambiente, código de saída e chamadas a ferramentas externas.

Requisitos: Windows + PowerShell 5.1 (já vem no Windows). Nada mais.

## Como funciona

```
executar.ps1
  ├─ preparar.ps1      cria harness\.sandbox\<cenario>\C\SYSPDV falso (banco, backups, lixo, arquivos protegidos)
  ├─ roda o app        com REPARO_* apontando para o sandbox e REPARO_FERRAMENTAS_DIR = harness\bin
  │    └─ app chama harness\bin\gbak.cmd, net.cmd...  →  simulador\ferramenta-falsa.ps1
  │         (registra a chamada, imita o comportamento real e as falhas do cenário)
  └─ verificar.ps1     confere as regras e gera .harness\relatorio.md
```

O "banco" é um arquivo de texto com um marcador (`SYSPDV-FDB;origem=ORIGINAL;id=...`).
O simulador do `gbak` gera backups e restores com marcadores próprios, então dá para saber no
final se o banco é o original, um restaurado ou um parcial (quebrado).

O simulador imita detalhes do Firebird 2.5 e do Windows:
- `gfix`/`gbak` falham se o serviço estiver parado, se a senha estiver errada ou se o banco não existir;
- `gbak -c` falha se o banco de destino já existir;
- `net stop` em serviço parado (e `net start` em serviço rodando) retorna erro 2;
- `VERIFICAINDICE`, `atualizasyspdv_server` e `syspdv_server` falham se o banco estiver ausente ou quebrado;
- qualquer caminho fora do sandbox é **bloqueado** (código 98) e reprova o teste.

A senha muda a cada execução, então o app só passa se ler a senha da configuração.

## Uso

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File harness/executar.ps1 -Alvo app
powershell -NoProfile -ExecutionPolicy Bypass -File harness/executar.ps1 -Alvo app -Cenario falha-backup
powershell -NoProfile -ExecutionPolicy Bypass -File harness/executar.ps1 -Alvo app -Cenario normal,falha-restore
powershell -NoProfile -ExecutionPolicy Bypass -File harness/executar.ps1 -Alvo legado
```

No VS Code: **Terminal → Executar Tarefa → Harness: ...**

Antes do primeiro uso com `-Alvo app`, preencha `comandoApp` em `harness.config.json`
(comando executado na raiz do repositório pelo `cmd.exe`).

## Cenários

| Cenário | O que acontece | O app deve |
|---|---|---|
| `normal` | Tudo funciona | Reparar e terminar com código 0 |
| `banco-corrompido` | 1ª validação (`gfix -v`) acha erros | Seguir em frente (é o motivo do reparo) |
| `servico-parado` | Firebird já parado no início | Seguir em frente |
| `falha-backup` | `gbak -b` falha e deixa `.fbk` incompleto | Parar, manter o banco original no lugar, código ≠ 0 |
| `falha-restore` | `gbak -c` falha e deixa banco parcial | Parar, devolver o original ao lugar, código ≠ 0 |

Para criar um cenário novo, copie um `.json` de `cenarios/`. Cada falha tem `ferramenta`,
`modo` (`validar`, `mend`, `backup`, `restore`, `stop`, `start`, `matar`, `executar`),
`ocorrencia` (opcional: só a N-ésima chamada; sem ela, todas), `codigoSaida` e `mensagem`.

## Checagens

| ID | Regra |
|---|---|
| S1 | Nenhuma ferramenta apontou para fora do sandbox (sem caminhos fixos) |
| S2 | O app executou as ferramentas pelo harness |
| S3 | Usuário/senha vieram da configuração |
| S4 | Nome do serviço veio da configuração |
| I1 | O banco original foi preservado (no lugar ou em backup) |
| I2 | O backup anterior em `bkpBd` não foi sobrescrito |
| I3 | Ao final existe um banco válido em `syspdv_srv.fdb` |
| I4 | Restore só depois de backup bem-sucedido; índices/atualização só depois de restore bem-sucedido |
| I5 | Nada fora do escopo foi apagado ou alterado (subpastas, outras pastas, raiz do disco, `.ini`, `.exe`, `.dll`) |
| I6 | O serviço do Firebird ficou rodando |
| I7 | A senha não aparece na saída nem nos logs do app (`REPARO_LOG_DIR`) |
| F1 | Código de saída certo (0 = sucesso) e terminou dentro do tempo limite |
| F2 | Banco final é o esperado (restaurado ou original) |
| F3 | Limpeza removeu os temporários (só nos cenários de sucesso) |
| F4 | Fluxo completo e na ordem (só nos cenários de sucesso) |

## Autoteste: o script original

`-Alvo legado` roda `referencia/reparo-legado.ps1`, uma cópia fiel do `REPARO.CMD`. O resultado
esperado mostra os defeitos do script original:

| Cenário | Resultado esperado | Checagens que falham |
|---|---|---|
| normal | FALHOU | I2 (MOVE sobrescreve o backup anterior) |
| banco-corrompido | FALHOU | I2 |
| servico-parado | FALHOU | I2 |
| falha-backup | FALHOU | I2, I3, I4, F1, F2 (restaura de backup incompleto e abre o sistema com o banco quebrado) |
| falha-restore | FALHOU | I2, I3, I4, F1, F2 (deixa o banco parcial no lugar) |

Se o autoteste der resultado diferente dessa tabela, o problema está no harness: corrija-o antes de
usar com o app.

## Regra para agentes

Não altere arquivos desta pasta para fazer o app passar. Se uma checagem parecer errada, explique
ao usuário e espere a decisão dele.
