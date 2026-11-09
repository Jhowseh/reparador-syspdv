---
applyTo: 'src/**,tests/**'
---
Código do app de reparo. Antes de editar, confira `docs/especificacao.md` (requisitos R01–R25)
e o contrato com o harness em `AGENTS.md` (seção 4).

- Toda execução de processo externo passa por uma única classe/módulo, que resolve o executável
  como `<REPARO_FERRAMENTAS_DIR>\<nome>.cmd` quando essa variável existir.
- Toda chamada externa registra no log: comando (senha mascarada), saída, código de saída e duração.
- Etapas críticas (backup, restore) checam o código de saída e o arquivo gerado antes de seguir.
- Em caso de falha depois de mover o banco: desfazer (devolver o original) antes de sair.
- Com `REPARO_NAO_INTERATIVO=1`: nenhuma pergunta, pausa ou janela.
