# Instruções do Copilot — Reparo SysPDV

Leia e siga `AGENTS.md` na raiz do repositório. Ele é a fonte principal de instruções.

Resumo das regras que nunca podem ser quebradas:

- Este app repara o banco Firebird de um PDV. Nunca execute `gfix`, `gbak`, `net stop/start`,
  `tskill`, `taskkill` reais, nem apague/mova arquivos em `C:\SYSPDV`.
- Teste sempre pelo harness: `powershell -NoProfile -ExecutionPolicy Bypass -File harness/executar.ps1 -Alvo app`.
- Não altere `harness/` para fazer o app passar.
- Sem caminhos ou credenciais fixos no código; a senha nunca vai para log ou tela.
- O banco original só sai do lugar depois de um backup bem-sucedido; backups antigos nunca são sobrescritos.
- Textos para o usuário em português do Brasil, simples.

Prompts do projeto: `/planejar-app`, `/implementar-etapa`, `/rodar-harness`, `/revisar-seguranca`.
