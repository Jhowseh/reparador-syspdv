---
applyTo: 'harness/**'
---
Esta pasta é o harness de testes. Não altere nenhum arquivo aqui para fazer o app passar.
Se uma checagem parecer errada, explique o motivo ao usuário e espere a decisão dele.
Alterações no harness só com pedido explícito do usuário. Depois de qualquer alteração,
rode `harness/executar.ps1 -Alvo legado` e confira se o resultado bate com a tabela de
autoteste em `harness/README.md`.
