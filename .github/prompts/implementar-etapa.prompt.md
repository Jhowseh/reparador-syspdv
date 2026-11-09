---
description: 'Implementa a próxima etapa do docs/plano.md e valida no harness.'
agent: 'agent'
---
Implemente a próxima etapa não marcada de `docs/plano.md` (ou a etapa que eu indicar: ${input:etapa:próxima}).

1. Leia `AGENTS.md`, a etapa no plano e os requisitos relacionados em `docs/especificacao.md`.
2. Implemente **só essa etapa**, com testes de unidade quando fizer sentido.
3. Compile e rode os testes de unidade.
4. Rode o harness: `powershell -NoProfile -ExecutionPolicy Bypass -File harness/executar.ps1 -Alvo app`.
   Nas etapas iniciais é normal alguns cenários falharem; o que importa é a etapa atual não
   introduzir falhas de segurança (S1–S4, I1, I2, I5, I7).
5. Se algo falhar, corrija o **app**. Nunca altere `harness/` para passar.
6. Marque a etapa em `docs/plano.md`, atualize a tabela de cenários e registre decisões novas em `docs/decisoes.md`.
7. Responda com: o que mudou, resultado do harness por cenário, e próxima etapa.
