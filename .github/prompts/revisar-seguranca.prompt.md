---
description: 'Revisa as mudanças do app contra as regras de segurança do projeto.'
agent: 'agent'
---
Revise as mudanças pendentes (use `git diff` e `git diff --staged`; se não houver git, revise `src/` e `tests/`).
Para cada item abaixo, responda OK ou PROBLEMA com arquivo e linha:

1. Caminho fixo (`C:\SYSPDV`, `gfix.exe`, nome do serviço) fora da configuração padrão.
2. Usuário/senha no código, ou senha em log, tela, exceção ou linha de comando registrada.
3. Processo externo executado fora do módulo único de execução (quebra o contrato do harness).
4. Código de saída ou arquivo gerado não verificados em backup/restore.
5. Banco movido/apagado antes de backup confirmado; falta de rollback quando o restore falha.
6. Backup que pode sobrescrever outro (nome sem data e hora).
7. Apagar arquivos fora da pasta base, ou de forma recursiva.
8. Pergunta, pausa ou janela que trava com `REPARO_NAO_INTERATIVO=1`.
9. Serviço do Firebird pode ficar parado ao sair (inclusive em caso de erro).
10. Mudanças em `harness/` (não devem existir sem pedido explícito).

Termine com a lista de problemas por gravidade. Não corrija nada sem eu pedir.
