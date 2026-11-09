---
description: 'Planeja o app que substitui o REPARO.CMD: perguntas, stack, arquitetura e plano (sem escrever código do app).'
agent: 'agent'
---
Você vai planejar o app que substitui `docs/original/REPARO.CMD`. **Não escreva código do app nesta etapa.**

1. Leia `AGENTS.md`, `docs/especificacao.md`, `docs/original/REPARO.CMD`, `docs/decisoes.md` e `harness/README.md`.
2. Faça para mim as perguntas da seção "Perguntas em aberto" da especificação que ainda não têm
   resposta (no máximo 5, as que mais mudam a decisão). Espere as respostas.
3. Proponha 2 ou 3 opções de stack, com prós e contras, e recomende uma. Considere:
   versões do Windows nas lojas, distribuição em um único executável, execução como administrador,
   interface para operador leigo, facilidade de manutenção por mim, e se roda sem instalar runtime.
4. Descreva a arquitetura em poucas linhas, incluindo:
   - módulo único de execução de processos externos (contrato do harness, `AGENTS.md` seção 4);
   - configuração (arquivo + variáveis `REPARO_*`), com a senha fora do código;
   - fluxo em etapas com checagem de resultado e desfazer (rollback);
   - log em arquivo com senha mascarada;
   - interface (tela ou console) separada da lógica, para a lógica rodar no harness sem interação.
5. Depois da minha aprovação:
   - registre as decisões em `docs/decisoes.md` (D-001 stack, D-003 arquitetura...);
   - reescreva `docs/plano.md` em etapas pequenas, cada uma terminando com o harness rodando
     (comece por: esqueleto + configuração + execução de processos + 1 etapa do fluxo);
   - preencha `comandoApp` em `harness/harness.config.json` com o comando que roda o app em
     modo não interativo a partir da raiz do repositório.
6. Termine com um resumo curto do que foi decidido e qual é a próxima etapa.
