# Plano de trabalho

> Este arquivo é preenchido pelo agente no `/planejar-app` e atualizado a cada `/implementar-etapa`.
> Cada etapa deve ser pequena e terminar com o harness rodando
> (`powershell -NoProfile -ExecutionPolicy Bypass -File harness/executar.ps1 -Alvo app`).
> Nas primeiras etapas é normal vários cenários falharem; anote a situação na tabela no fim.

Stack: D-001 (C# .NET 8 + WinForms). Arquitetura: D-003. Comportamentos: D-004.

Pré-requisito: .NET 8 SDK instalado (`winget install Microsoft.DotNet.SDK.8`).

## Etapas

- [x] 0. Planejamento: escolher stack, arquitetura e preencher este plano (`/planejar-app`)
- [x] 1. Esqueleto: solução `src/Reparo.sln` com `Reparo.Core`, `Reparo.App` (WinForms) e
      `Reparo.Testes` (xUnit). `Reparo.App --nao-interativo` (ou `REPARO_NAO_INTERATIVO=1`)
      roda sem janela e sai com código ≠ 0 ("ainda não implementado"). Harness roda e reprova.
- [x] 2. Configuração: `Configuracao` lendo `reparo.json` ao lado do `.exe` + variáveis `REPARO_*`
      (variáveis têm prioridade), com padrões de produção. Senha nunca no código. Testes de unidade.
- [x] 3. Log: arquivo `reparo_AAAAMMDD_HHMMSS.log` em `REPARO_LOG_DIR`, com mascarador de senha
      aplicado a toda linha. Testes de unidade do mascarador.
> Etapas 4 a 13 do plano original foram juntadas em duas, a pedido do usuário (D-008).
> Dentro de cada etapa o agente roda o harness a cada parte concluída, na ordem abaixo,
> e corrige antes de seguir para a próxima parte.

- [x] 4. **Reparo completo (lógica, sem tela)** — ao final, **todos os cenários do harness passam**.
      Partes, nesta ordem:
      a. `ExecutorProcessos` (único ponto de chamada externa): `<REPARO_FERRAMENTAS_DIR>\<nome>.cmd`
         quando definida; captura saída, código e duração e grava no log (mascarado).
      b. Motor do fluxo: `Etapa` (executar / desfazer / crítica?); falha crítica interrompe e roda os
         desfazer; no final, sempre liga o Firebird (I6). Parar serviço tolera "já parado"; se não
         parar no tempo limite, mata o processo configurado.
      c. Pré-checagens: administrador (vira aviso em modo teste), banco existe, espaço em disco (~2x).
      d. Limpeza: `srv*`, `*log`, `*err`, `banc*`, `*txt` só na pasta base, sem subpastas, nunca
         o próprio log nem o banco (F3, I5).
      e. Validação e mend: `gfix -v -f` → reinicia → `gfix -m -f -i` → reinicia → `gfix -v -f`;
         erros de validação são aviso (cenário `banco-corrompido`).
      f. Backup: `gbak -b -v -i -g -l` para `.fbk` com data e hora; sucesso = código 0 e arquivo > 0;
         falha para tudo com o banco no lugar (cenário `falha-backup`).
      g. Troca + restore: move o banco para `bkpBd\syspdv_srv_<data-hora>.fdb` (cria a pasta, nunca
         sobrescreve), `gbak -c -v -i`; desfazer: apaga parcial, devolve original, liga serviço
         (cenário `falha-restore`, I2).
      h. Pós-restore: `VERIFICAINDICE` (espera fechar), `dbMonitor` (sem esperar),
         `atualizasyspdv_server` (espera fechar), `syspdv_server` (sem esperar).
      Testes de unidade: executor, motor do fluxo e rollback com executor falso.
- [x] 5. **Tela e entrega** — harness continua passando em todos os cenários.
      a. Tela WinForms: aviso de caixas desconectados + confirmação, etapas com progresso, aviso antes
         dos programas que abrem janela, resultado final claro, pedido de senha se faltar.
         Lógica continua só no Core (execução em segundo plano para a janela não travar).
      b. Publicação: manifesto `requireAdministrator`, `dotnet publish` single-file self-contained
         `win-x64`, `reparo.json` de exemplo, instruções curtas de uso para a loja.
      c. `/revisar-seguranca` e roteiro de teste real (máquina de testes, cópia do banco).

## Pendências (da revisão de segurança da etapa 5 — aguardando decisão do usuário)

- [x] R1 (médio) Recusar pasta base perigosa + exigir que a pasta do banco seja a do SysPDV (D-011).
- [x] R2 (médio) `--nao-interativo` em produção: VERIFICAINDICE/atualização abrem janela e esperam sem tempo limite — resolvido: sem janela só no modo teste (harness); na loja recusa com código 4 (D-013).
- [x] R3 (médio) Senha em texto puro no `reparo.json` — resolvido: a senha é digitada na tela a cada reparo (D-011).
- [x] R4 (baixo) `net`/`taskkill` só da pasta do sistema do Windows (D-011).
- [x] R5 (baixo) Conferir o banco restaurado com `gfix -v` antes de seguir — nova etapa 16; se falhar, devolve o original (D-012).
- [x] Parar na etapa 5 quando o gfix nem conecta ao banco (D-014), achado no 1º teste real.
- [x] gfix/gbak/fbclient 2.5.1 embutidos e colocados na pasta do SysPDV (D-015).
- [x] VerificaIndice e dbMonitor também embutidos (D-016).
- [x] Reiniciar o Firebird antes de conferir o banco reconstruído (D-017), achado no teste de 05/11.
- [x] Tela nova: única, minimalista, tema claro/escuro (D-018). Prévias: `scripts/previa-tela.ps1`.
- [x] Logo da PBtec (claro/escuro) no cabeçalho e aba "Sobre" (D-021).
- [x] Pasta organizada para o GitHub público, sem programas de terceiros (D-022).
- [x] Pastas no padrão .NET: solução na raiz, testes em `tests/`, Core em subpastas (D-023).
- [ ] Teste real conforme `docs/teste-real.md`. 1ª tentativa (04/11, SERVIDOR): `unknown ISC error
      335545106` em gfix/gbak, provável Firebird 3+ na máquina; aguardando diagnóstico.

## Situação do harness por cenário

Última execução: 2026-11-09, depois do D-023. 135 testes de unidade passando.

| Cenário | Status | Checagens que falham |
|---|---|---|
| normal | **passou** | — |
| banco-corrompido | **passou** | — |
| servico-parado | **passou** | — |
| falha-backup | **passou** | — |
| falha-restore | **passou** | — |
