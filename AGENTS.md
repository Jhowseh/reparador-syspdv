# AGENTS.md — Reparo SysPDV

Instruções para qualquer agente de IA que trabalhe neste repositório
(GitHub Copilot no VS Code, Claude Code, Codex etc.). Leia este arquivo inteiro antes de agir.

## 1. Objetivo

Construir um app que substitui o script `docs/original/REPARO.CMD`, usado para reparar o banco
Firebird do SysPDV (`C:\SYSPDV\syspdv_srv.fdb`). O app deve fazer o mesmo trabalho, mas com
segurança: parar quando algo der errado, nunca perder dados, registrar tudo em log e não exigir
que o operador entenda linha de comando.

- O que o script faz, passo a passo, e os problemas dele: `docs/especificacao.md`
- Decisões já tomadas (stack, arquitetura): `docs/decisoes.md`
- Plano de trabalho e progresso: `docs/plano.md`

## 2. Estado atual

- Stack escolhida: **C# .NET 8 + WinForms** (`docs/decisoes.md`, D-001/D-003). Solução `Reparo.sln` na raiz;
  código em `src/`, testes de unidade em `tests/` (D-023).
- Progresso por etapa em `docs/plano.md`; continue com `/implementar-etapa`.
- Pacote para as lojas: `scripts/publicar.ps1` gera `dist\ReparoSysPDV.exe`. **Nunca execute esse
  `.exe` neste computador** (ele usa a pasta e as ferramentas reais). Teste real: `docs/teste-real.md`.
- O harness de testes (`harness/`) está pronto e não depende da stack.

## 3. Regras de segurança (obrigatórias)

Este app mexe no banco de dados de uma loja. Um erro pode parar o caixa ou apagar vendas.

1. **Nunca** execute de verdade, neste computador: `gfix`, `gbak`, `isql`, `net stop`, `net start`,
   `sc stop/start/config`, `tskill`, `taskkill`, nem comandos que apaguem ou movam arquivos em
   `C:\SYSPDV` ou qualquer pasta fora do repositório.
2. Para testar o app, use **somente** o harness (`harness/executar.ps1`). Ele cria uma cópia falsa
   de `C:\SYSPDV` em `harness/.sandbox/` e troca as ferramentas reais por simuladores.
3. **Não altere o harness para fazer o app passar.** Se um teste do harness parecer errado,
   pare e explique para o usuário. Mudanças no harness só com pedido explícito do usuário.
4. Sem caminhos fixos no código: `C:\SYSPDV`, `gfix.exe`, nome do serviço etc. vêm de configuração.
5. Sem credenciais fixas no código (`SYSDBA`/`masterkey`). A senha nunca aparece em log ou na tela.
6. O app só apaga/move o banco depois de confirmar que o backup terminou com sucesso.
   Backups antigos nunca são sobrescritos (use data e hora no nome).

## 4. Contrato com o harness

O app precisa aceitar a configuração abaixo por variáveis de ambiente (pode ter também arquivo de
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

- Controle do serviço do Firebird: use o comando `net stop/start` (ou a mesma abstração) para que
  o harness consiga simular. Não use APIs que o harness não intercepta quando estiver em modo teste.
- O banco pode ser passado como caminho completo (`C:\...\syspdv_srv.fdb`) ou relativo à pasta base.
- Código de saída do app: `0` = reparo concluído; diferente de `0` = falhou/abortou.

## 5. Como validar

```powershell
# todos os cenários contra o app (precisa de comandoApp em harness/harness.config.json)
powershell -NoProfile -ExecutionPolicy Bypass -File harness/executar.ps1 -Alvo app

# um cenário só
powershell -NoProfile -ExecutionPolicy Bypass -File harness/executar.ps1 -Alvo app -Cenario falha-backup

# autoteste: roda uma cópia fiel do script antigo (ele DEVE falhar em várias checagens)
powershell -NoProfile -ExecutionPolicy Bypass -File harness/executar.ps1 -Alvo legado
```

O relatório de cada cenário fica em `harness/.sandbox/<cenario>/.harness/relatorio.md`, com a lista
de chamadas feitas pelo app. Detalhes das checagens: `harness/README.md`.

**Definição de pronto:** todos os cenários passam com `-Alvo app`, além dos testes de unidade da
stack escolhida.

## 6. Forma de trabalhar

- Trabalhe em etapas pequenas seguindo `docs/plano.md`. Ao terminar uma etapa, rode o harness,
  marque a etapa e registre decisões novas em `docs/decisoes.md`.
- Textos para o usuário final (telas, mensagens, logs) em português do Brasil, simples e sem jargão.
  O operador pode ser alguém da loja, não um técnico.
- Toda saída das ferramentas (`gfix`, `gbak`...) vai para o log do app, com a senha mascarada.
- Se uma regra deste arquivo impedir o que o usuário pediu, explique e pergunte antes.

## 7. Estrutura

```
AGENTS.md                 este arquivo
CLAUDE.md                 aponta para este arquivo (Claude Code)
.github/                  instruções e prompts do Copilot (/planejar-app etc.)
.claude/                  permissões e comandos do Claude Code
.vscode/                  tarefas do VS Code para rodar o harness
docs/original/REPARO.CMD  script original (não editar)
docs/especificacao.md     análise do script e requisitos do app
docs/decisoes.md          registro de decisões
docs/plano.md             plano e progresso
docs/previa-tela/         imagens da tela (scripts/previa-tela.ps1)
harness/                  ambiente de teste simulado (não editar sem pedido)
Reparo.sln                solução .NET (rode `dotnet test` na raiz)
Directory.Build.props     configurações comuns aos projetos (.NET 8, nullable)
src/Reparo.Core/          lógica do reparo, sem tela, em subpastas por assunto
                          (Fluxo, Configuracao, Firebird, Sistema, Log, Programas)
src/Reparo.App/           tela WinForms, LEIA-ME.txt da loja, reparo.exemplo.json
tests/Reparo.Testes/      testes de unidade (xUnit)
scripts/                  publicar, gerar ícone/logos/prévias
assets/logos/             logos da PBtec (origem dos logos da tela)
terceiros/                gfix/gbak/fbclient e VerificaIndice/dbMonitor embutidos no .exe
                          (fora do git, D-022; lista em terceiros/LEIA-ME.md)
```
