# Especificação — o que o REPARO.CMD faz e o que o app precisa fazer

Fonte: `docs/original/REPARO.CMD` (76 linhas, Windows batch).

## 1. Contexto

- SysPDV (sistema de frente de caixa) instalado em `C:\SYSPDV`.
- Banco Firebird `syspdv_srv.fdb`, acessado pelo serviço `FirebirdServerDefaultInstance`.
- O script mata o processo `fbserver.exe`, que é o servidor do **Firebird 2.5** (no Firebird 3+
  o processo se chama `firebird.exe`). Isso indica Firebird 2.5 — **confirmar com o usuário**.
- Usuário/senha do banco no script: `sysdba` / `masterkey` (padrão de fábrica do Firebird).
- Precisa rodar como administrador (por causa do `net stop/start`).

## 2. Fluxo do script, passo a passo

| Etapa | Linhas | Comando | O que faz |
|---|---|---|---|
| E01 | 2 | `tskill fbserver.exe` | Mata à força o processo do servidor Firebird |
| E02 | 4–6 | `c:` / `CD\SYSPDV` | Vai para a pasta fixa `C:\SYSPDV` |
| E03 | 7–15 | `del srv*`, `del *log`, `del *err`, `del banc*`, `del *txt` | Apaga arquivos temporários e logs na pasta (não recursivo) |
| E04 | 17–18 | `net stop` / `net start` do serviço | Reinicia o Firebird |
| E05 | 24 | `gfix -v -f` | Validação completa do banco (1ª de 3) |
| E06 | 25–26 | `net stop` / `net start` | Reinicia o Firebird |
| E07 | 33 | `gfix -m -f -i` | *Mend*: marca estruturas corrompidas como inutilizáveis para o backup conseguir passar; ignora erros de checksum |
| E08 | 34–35 | `net stop` / `net start` | Reinicia o Firebird |
| E09 | 43 | `gfix -v -f` | Nova validação completa (3ª etapa) |
| E10 | 44 | `pause` | Espera o operador |
| E11 | 48–50 | reinicia serviço + `gbak -b -v -i -g -l ... syspdv_srv.fbk` | Backup: verboso, ignora checksums (`-i`), sem coleta de lixo (`-g`), ignora transações em limbo (`-l`) |
| E12 | 51 | `pause` | Espera o operador |
| E13 | 52–54 | `net stop` + `MOVE SYSPDV_SRV.FDB bkpBd` | Para o serviço e tira o banco atual do lugar |
| E14 | 58–59 | `net start` + `gbak -c -v -i ... .fbk .fdb` | Restore criando banco novo, com índices inativos (`-i` no restore = `-inactive`) |
| E15 | 63 | `VERIFICAINDICE.EXE` | Reativa/verifica os índices (programa do SysPDV) |
| E16 | 68–69 | `start dbMonitor.exe` + `atualizasyspdv_server.exe` | Abre o monitor (sem esperar) e roda o atualizador do banco (esperando terminar) |
| E17 | 74 | `start syspdv_server` | Abre o sistema |

Resumo: **limpa → valida → conserta (mend) → valida → backup → troca o banco → restore → índices → atualiza → abre o sistema.**
O backup + restore reconstrói o banco do zero, o que elimina lixo e corrupções que o *mend* isolou.

## 3. Problemas encontrados no script

| # | Gravidade | Problema |
|---|---|---|
| P01 | **Crítico** | Nenhum comando tem o resultado verificado. Se o backup (E11) falhar, o script move o banco bom para `bkpBd` e restaura a partir de um backup incompleto → banco quebrado e sistema aberto em cima dele. |
| P02 | **Crítico** | Se o restore (E14) falhar, o banco fica parcial em `C:\SYSPDV` e não há volta automática para o original. |
| P03 | Alto | `MOVE` dentro de script **sobrescreve sem perguntar**: o backup anterior em `bkpBd` é perdido a cada execução. |
| P04 | Alto | Se `bkpBd` não for uma pasta, o banco é renomeado para um arquivo chamado `bkpBd` (sem extensão). |
| P05 | Alto | `tskill` mata o Firebird à força antes de parar o serviço, com caixas possivelmente conectados. Pode causar a própria corrupção que o script tenta consertar. `tskill` também pode não existir em algumas edições do Windows (`taskkill /IM` é o padrão). |
| P06 | Médio | Usuário e senha fixos no script (`sysdba`/`masterkey`). |
| P07 | Médio | Caminho fixo `C:\SYSPDV`. |
| P08 | Médio | `del *log`, `*txt` etc. apagam qualquer arquivo com esse final, sem lista, e apagam os logs **antes** do diagnóstico. |
| P09 | Médio | `gfix -mend` descarta registros corrompidos e ninguém fica sabendo o que foi perdido. |
| P10 | Médio | Não verifica espaço em disco (o backup + o novo banco precisam de mais ou menos 2x o tamanho do banco). |
| P11 | Médio | Não verifica se está rodando como administrador. |
| P12 | Médio | Não avisa que os caixas serão desconectados nem verifica se há conexões ativas. |
| P13 | Baixo | `cls` apaga as mensagens de erro da tela; não há log nenhum da operação. |
| P14 | Baixo | Os `pause` não dizem o que o operador deve conferir. |
| P15 | Baixo | Com o caminho relativo `syspdv_srv.fdb`, a conexão depende da pasta atual e do protocolo local do Firebird. |

## 4. Requisitos do app

### Funcionais
- **R01** Executar o mesmo fluxo da seção 2 (o *mend* pode ser opcional, mas se rodar, é antes do backup).
- **R02** Ordem obrigatória: backup → restore → `VERIFICAINDICE` → `atualizasyspdv_server` → `syspdv_server`.
- **R03** Limpar os mesmos padrões de arquivo (`srv*`, `*log`, `*err`, `banc*`, `*txt`) **apenas** na pasta base, sem entrar em subpastas.
- **R04** Mostrar o progresso em linguagem simples (etapa X de Y) e um resultado final claro.

### Segurança dos dados
- **R10** Parar no primeiro erro crítico (backup ou restore) e não seguir para as etapas seguintes.
- **R11** Só tirar o banco original do lugar depois de um backup bem-sucedido (código de saída 0 e arquivo `.fbk` com tamanho > 0).
- **R12** Se o restore falhar: apagar o banco parcial, devolver o original ao lugar e deixar o serviço rodando.
- **R13** Guardar o banco original em `bkpBd` com data e hora no nome; nunca sobrescrever backups.
- **R14** Não apagar nada fora da pasta base.
- **R15** Ao terminar (com sucesso ou não), o serviço do Firebird deve estar rodando.

### Operação
- **R20** Caminhos, nome do serviço, usuário e senha configuráveis (ver "Contrato com o harness" abaixo).
- **R21** Log completo de cada etapa (comando, saída, código de saída, duração), senha mascarada.
- **R22** Verificar privilégio de administrador e espaço em disco antes de começar.
- **R23** Pedir confirmação antes de começar, avisando que os caixas serão desconectados (exceto em modo não interativo).
- **R24** Modo não interativo (`REPARO_NAO_INTERATIVO=1`), usado pelo harness.
- **R25** Preferir parar o serviço normalmente; matar o processo só se ele não parar dentro de um tempo limite.

### Contrato com o harness
O app aceita a configuração abaixo por variáveis de ambiente (pode ter também arquivo de
configuração ou argumentos, mas as variáveis têm prioridade). O harness define todas elas.

| Variável | Significado | Valor em produção (padrão) |
|---|---|---|
| `REPARO_BASE_DIR` | Pasta do SysPDV | `C:\SYSPDV` |
| `REPARO_FERRAMENTAS_DIR` | Se definida, **toda** ferramenta externa é executada como `<dir>\<nome>.cmd` | (vazia) |
| `REPARO_DB_USUARIO` | Usuário do Firebird | `SYSDBA` |
| `REPARO_DB_SENHA` | Senha do Firebird | lida da configuração, nunca no código |
| `REPARO_SERVICO_FIREBIRD` | Nome do serviço | `FirebirdServerDefaultInstance` |
| `REPARO_LOG_DIR` | Pasta onde o app grava seus logs | ex.: `C:\SYSPDV\logs-reparo` |
| `REPARO_NAO_INTERATIVO` | `1` = não perguntar nada, não pausar, não abrir janelas | (vazia) |

Ferramentas externas que o app pode chamar (nomes usados em `REPARO_FERRAMENTAS_DIR`):
`gfix`, `gbak`, `net`, `tskill`, `taskkill`, `VERIFICAINDICE`, `atualizasyspdv_server`,
`dbMonitor`, `syspdv_server`.

- Controle do serviço do Firebird: pelo comando `net stop/start` (ou a mesma abstração), para que
  o harness consiga simular. Nada de APIs que o harness não intercepta quando estiver em modo teste.
- O banco pode ser passado como caminho completo (`C:\...\syspdv_srv.fdb`) ou relativo à pasta base.
- Código de saída do app: `0` = reparo concluído; diferente de `0` = falhou/abortou.

## 5. Perguntas em aberto (fazer ao usuário)

1. Quais versões do Windows rodam nas lojas? (Afeta a stack: .NET 8+ não roda em Windows 7.)
2. Firebird 2.5 mesmo? Algum lugar usa Firebird 3 ou 4?
3. Quem vai usar: técnico de suporte ou alguém da loja?
4. Precisa de interface gráfica ou console basta?
5. `bkpBd` é uma pasta em todas as instalações?
6. `VERIFICAINDICE.EXE` e `atualizasyspdv_server.exe` aceitam argumentos ou rodam sem interação?
7. Como o app chega nas lojas (cópia manual, instalador, acesso remoto)?
