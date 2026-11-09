---
description: 'Roda o harness e explica as falhas em linguagem simples, com proposta de correção.'
agent: 'agent'
---
1. Rode `powershell -NoProfile -ExecutionPolicy Bypass -File harness/executar.ps1 -Alvo ${input:alvo:app} -Cenario ${input:cenario:todos}`.
2. Para cada cenário que falhou, leia `harness/.sandbox/<cenario>/.harness/relatorio.md`
   (tem a lista de chamadas feitas e os arquivos no final).
3. Explique cada checagem que falhou: o que aconteceu, por que é um problema para a loja, e qual
   parte do código do app causou. Use `harness/README.md` para o significado de cada ID.
4. Proponha a correção no app. Não altere `harness/`. Não aplique a correção sem eu pedir.
