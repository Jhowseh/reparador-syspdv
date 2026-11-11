# Roteiro de teste real (antes de levar para as lojas)

O harness prova a lógica com ferramentas simuladas. Este roteiro confere o app com o Firebird e o
SysPDV **de verdade**, num ambiente onde nada pode dar prejuízo.

## Regras

- **Nunca** num computador de loja nem com o banco de produção.
- Use um computador de testes ou uma máquina virtual (de preferência com *snapshot* antes de cada teste).
- Use uma **cópia** de um `syspdv_srv.fdb` real (de preferência de uma loja que já precisou de reparo).
- Mesma versão do Firebird (2.5.x) e do SysPDV das lojas, instalados em `C:\SYSPDV`.

## Preparação

1. Gere o pacote: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/publicar.ps1`.
2. Copie **só** o `dist\ReparoSysPDV.exe` para a máquina de testes (ex.: Área de Trabalho).
   Não crie `reparo.json`: o teste é justamente ver o app funcionar sozinho.
3. Anote antes: tamanho do banco, número de vendas do último dia (no SysPDV), espaço livre no disco.
4. Crie o ponto de verificação (snapshot) da máquina virtual.

## Testes

| # | Teste | Como fazer | O que conferir |
|---|---|---|---|
| 1 | Caminho feliz | Abrir o `.exe`, aceitar o UAC, conferir o banco sugerido (ou Procurar...), digitar a senha, Iniciar, confirmar | Pede administrador; mostra etapas com ✔ (inclusive "Conferir o banco reconstruído"); abre a janela do VERIFICAINDICE e da atualização; termina VERDE; abre o sistema; vendas iguais às anotadas; `bkpBd` tem `.fbk` e `.fdb` com data e hora; backup antigo intacto |
| 1b | Programas na pasta | Antes do teste 1, anotar se `C:\SYSPDV` tem `gfix.exe`, `gbak.exe`, `fbclient.dll`, `VerificaIndice.exe`, `dbMonitor.exe` e qual versão (Propriedades > Detalhes) | Depois: os seis arquivos (com `firebird.msg`) estão em `C:\SYSPDV`; o Firebird é o 2.5.1.26351; se havia de outra versão, estão em `bkpBd\ferramentas-anteriores_...`; o VerificaIndice e o dbMonitor abrem durante o reparo; o SysPDV funciona normalmente depois (D-015, D-016) |
| 2 | Detecção e senha | Olhar o começo do log do teste 1 e as linhas do `gfix`/`gbak` | Linhas `gfix:` e `gbak:` mostram `C:\SYSPDV\gfix.exe` e `C:\SYSPDV\gbak.exe` (os do reparador, D-015); não aparece *"user name and password are not defined"* com a senha certa. **Se aparecer**, o gfix/gbak não aceita `ISC_USER`/`ISC_PASSWORD`: avise antes de usar (D-009) |
| 3 | Log sem senha | Procurar a senha no log e na tela | A senha não aparece em lugar nenhum (só `********`) |
| 4 | Senha errada | Digitar uma senha errada e iniciar | Para na etapa "Verificar o banco (1 de 3)" com a mensagem de senha incorreta; não chega ao backup; banco original no lugar; Firebird ligado |
| 4b | Sem conexão | Só se o computador tiver outra versão do Firebird: rodar normalmente | Para na etapa "Verificar o banco (1 de 3)" com "não foi possível conectar ao banco de dados"; o Firebird reinicia só uma vez no começo; banco original no lugar (D-014) |
| 5 | Backup falha | Deixar a pasta `bkpBd` somente leitura (Propriedades > Segurança: negar gravação) e rodar | Para no backup; termina VERMELHO "devolvido ao lugar"; banco original de volta em `C:\SYSPDV`; `.fbk.incompleto` (se criado); Firebird ligado |
| 6 | Serviço já parado | Parar o serviço do Firebird antes e rodar | Segue normalmente e termina VERDE |
| 7 | Caixa conectado | Deixar o SysPDV aberto em outro computador da rede e rodar | Serviço para (ou é encerrado à força, com aviso no log); reparo termina; o caixa precisa reconectar |
| 8 | Restore falha (opcional) | Com banco grande: durante a etapa "Reconstruir o banco", parar o serviço do Firebird pelo `services.msc` | Desfaz: apaga o parcial, devolve o original, liga o Firebird, termina VERMELHO "devolvido ao lugar" |
| 9 | Cancelar | Clicar em Iniciar e responder "Não" | Nada acontece; nenhuma linha de `net stop` no log |
| 10 | Banco fora da pasta do SysPDV | Copiar o `.fdb` para uma pasta qualquer (ex.: `C:\Teste\`) e escolher essa cópia | Recusa antes de parar o Firebird, dizendo que a pasta não é a do SysPDV; nenhum arquivo daquela pasta é apagado |
| 11 | Fechar no meio | Tentar fechar a janela durante o reparo | Não deixa fechar e explica o motivo |
| 12 | Linha de comando recusada | `start /wait ReparoSysPDV.exe --nao-interativo` num prompt de administrador | Código de saída 4 (`echo %ERRORLEVEL%`); o log diz que o modo sem janela é só para testes; nenhuma linha de `net stop`; banco intacto (D-013) |
| 13 | Banco danificado com o SysPDVService rodando | Repetir o reparo de 11/11 (banco de 5,9 GB com erros na `TRANSACAO_XMLNOTA`), sem parar o `SysPDVService` | No log, os três `gfix` e o `gbak -b` usam `C:\SYSPDV\bkpBd\syspdv_srv_<data-hora>.fdb`; não aparece *"bad parameters on attach"* nem *"secondary server attachments"*; a 1ª verificação mostra aviso com a quantidade de erros (não "nenhum problema"); "Consertar o banco (2 de 3)" roda de verdade (no `firebird.log`, nenhum *"XNET server initialization failed"*; a 3ª verificação acha menos erros que a 1ª); o backup passa da `TRANSACAO_XMLNOTA` (D-025, D-026) |

## Registro

Para cada teste, anote: data, versão do app, resultado (ok/problema), tempo total e o arquivo de log.
Problemas encontrados viram correções no app (e, se fizer sentido, um cenário novo no harness).
