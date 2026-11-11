# Registro de decisões

Cada decisão importante ganha uma entrada. Não apague entradas antigas: se mudar de ideia,
crie uma nova que substitui a anterior.

Modelo:

```
## D-000 — Título
- Data: AAAA-MM-DD
- Status: proposta | aprovada | substituída por D-xxx
- Contexto: por que a decisão é necessária
- Decisão: o que foi decidido
- Consequências: o que muda, riscos
```

---

## D-001 — Stack do app: C# .NET 8 + WinForms
- Data: 2026-11-03
- Status: aprovada
- Contexto: lojas com Windows 10/11; quem roda é o operador da loja (leigo), com tela gráfica;
  o app chega à loja como um único `.exe` copiado (manual ou acesso remoto); precisa de administrador.
- Opções avaliadas:
  - **C# .NET 8 + WinForms** — `.exe` único self-contained (~60–70 MB, sem instalar runtime),
    tela nativa simples, manifesto `requireAdministrator`, xUnit, poucos falsos positivos de antivírus.
  - Python + Tkinter + PyInstaller — `.exe` menor, mas tela com cara antiga e alto índice de
    falso positivo em antivírus.
  - Go — `.exe` pequeno, mas interface gráfica nativa fraca e linguagem nova para o mantenedor.
- Decisão: C# com .NET 8 (LTS), WinForms, publicado com `PublishSingleFile` + `SelfContained`
  para `win-x64`. Testes de unidade com xUnit.
- Consequências: o computador de desenvolvimento precisa do .NET 8 SDK
  (`winget install Microsoft.DotNet.SDK.8`). O `.exe` fica grande (~60–70 MB), aceitável.
  Não roda em Windows 7/8.1 (fora do escopo, confirmado pelo usuário).

## D-002 — Harness de testes independente da stack
- Data: 2026-11-03
- Status: aprovada
- Contexto: o app executa comandos destrutivos (parar serviço, mover banco). Não dá para testar
  contra um banco real nem no computador de desenvolvimento.
- Decisão: o app executa ferramentas externas a partir de uma pasta configurável
  (`REPARO_FERRAMENTAS_DIR`). Nos testes, essa pasta contém simuladores que registram as chamadas
  e imitam o comportamento (inclusive falhas). Um verificador checa as regras de segurança.
- Consequências: o app precisa de uma camada única para executar processos externos.
  Detalhes em `harness/README.md`.

## D-003 — Arquitetura do app
- Data: 2026-11-03
- Status: aprovada
- Contexto: a lógica precisa rodar no harness sem interação e ser testável; a tela é para leigo.
- Decisão: solução `src/Reparo.sln` com três projetos:
  - `Reparo.Core` (biblioteca, sem tela):
    - `ExecutorProcessos`: único ponto que executa programas externos. Com `REPARO_FERRAMENTAS_DIR`
      definida, roda `<dir>\<nome>.cmd`; senão, o caminho configurado da ferramenta. Captura saída,
      código de saída e duração e grava no log. Serviço controlado sempre via `net stop/start`.
    - `Configuracao`: `reparo.json` ao lado do `.exe` + variáveis `REPARO_*` (prioridade das
      variáveis). Senha fora do código (arquivo, variável ou digitada na tela).
    - `Fluxo`: lista de etapas; cada etapa informa sucesso/aviso/falha crítica e pode ter ação de
      desfazer. Falha crítica interrompe e executa os desfazer pendentes. No final, sempre garante
      o serviço do Firebird rodando.
    - `Log`: arquivo com data e hora em `REPARO_LOG_DIR`; toda linha passa pelo mascarador de senha.
    - `IInteracao`: confirmar, avisar, mostrar progresso. O Core só fala com a interface.
  - `Reparo.App` (WinForms, gera o `.exe`): com tela por padrão; com `REPARO_NAO_INTERATIVO=1` ou
    `--nao-interativo`, não abre janela e usa uma `IInteracao` automática. Código de saída 0 = sucesso.
  - `Reparo.Testes` (xUnit): configuração, mascaramento, fluxo e rollback com executor falso.
- Consequências: comando do harness:
  `dotnet run --project src/Reparo.App -c Release -- --nao-interativo`.

## D-004 — Comportamentos definidos no planejamento
- Data: 2026-11-03
- Status: aprovada
- Contexto: respostas do usuário às perguntas em aberto da especificação.
- Decisão:
  - `VERIFICAINDICE` e `atualizasyspdv_server` pedem interação: o app avisa o operador que uma
    janela vai abrir, espera o programa fechar (sem tempo limite) e confere o código de saída.
    `dbMonitor` e `syspdv_server` são abertos sem esperar.
  - Firebird 2.5 assumido; o nome do processo a matar (`fbserver.exe`) é configurável e só é
    usado se o serviço não parar dentro do tempo limite (R25).
  - `bkpBd` é tratado como pasta: criada se não existir; o banco antigo vai para
    `bkpBd\syspdv_srv_AAAAMMDD_HHMMSS.fdb`, nunca sobrescrevendo.
  - Erros encontrados pelo `gfix -v` são aviso (são o motivo do reparo); falhas de backup e
    restore são críticas.
- Consequências: se alguma suposição (Firebird 2.5, `bkpBd`) estiver errada numa loja, ajusta-se
  pela configuração, sem mudar código.

## D-005 — Códigos de saída e modo não interativo
- Data: 2026-11-03
- Status: aprovada
- Contexto: o harness só enxerga o código de saída e a saída padrão do app.
- Decisão: `CodigoSaida` em `Reparo.Core`: 0 = sucesso, 1 = falha no reparo, 2 = cancelado pelo
  operador, 3 = erro inesperado, 9 = não implementado (usado só durante o desenvolvimento).
  O `.exe` é `WinExe` (sem janela de console); no modo não interativo escreve na saída padrão
  (o harness a redireciona). Erros inesperados mostram só o tipo da exceção, nunca a mensagem,
  para não vazar senha.
- Consequências: ao rodar o `.exe` com `--nao-interativo` direto num terminal, a saída não aparece
  na tela (só redirecionada ou no log, a partir da etapa 3).

## D-006 — Formato e regras da configuração
- Data: 2026-11-03
- Status: aprovada
- Contexto: R20 e contrato do harness (`docs/especificacao.md`).
- Decisão:
  - Arquivo `reparo.json` ao lado do `.exe` (aceita comentários `//`; nomes sem diferenciar
    maiúsculas). Modelo comentado em `src/Reparo.App/reparo.exemplo.json`. Prioridade:
    variáveis `REPARO_*` > `reparo.json` > padrões de produção.
  - Campos só do arquivo (sem variável no contrato): `banco`, `pastaBackupBanco`,
    `processoFirebird`, `tempoLimitePararServicoSegundos`, `ferramentas` (caminho de cada
    ferramenta em produção; padrão `<nome>.exe`, procurado na pasta base e no PATH).
  - Pasta de logs padrão: `<pastaBase>\logs-reparo`.
  - Senha: variável ou arquivo; nunca no código. Sem senha: a tela pede (etapa 12); no modo
    não interativo é configuração inválida (código 4). A senha não sofre `Trim`.
  - A pasta de backup do banco precisa ficar dentro da pasta base; o banco não aceita `*`/`?`.
  - Erros de leitura do `reparo.json` mostram só a linha, nunca o conteúdo (pode ter a senha).
    `Configuracao.ToString()`/`Descrever()` mascaram a senha.
- Consequências: nova entrada em `CodigoSaida`: 4 = configuração inválida. O `reparo.json` com
  senha fica em texto puro na loja; proteger a pasta é responsabilidade da instalação
  (reavaliar na etapa 13 se for preciso cifrar).

## D-007 — Log do reparo e mascaramento de senha
- Data: 2026-11-03
- Status: aprovada
- Contexto: R21 (log completo, senha mascarada) e checagem I7 do harness.
- Decisão:
  - Um arquivo por execução: `<pastaLog>\reparo_AAAAMMDD_HHMMSS.log`, UTF-8 com BOM (abre certo
    no Bloco de Notas). Criado com `CreateNew` (nunca sobrescreve; se o nome existir, usa `_2`, `_3`...).
    Cada linha é gravada na hora (sem buffer). Formato: `data hora [NIVEL] texto`, níveis
    INFO, AVISO, ERRO e SAIDA (saída de ferramenta: `ferramenta> linha`).
  - `MascaradorSenha` troca por `********`: a senha configurada (em qualquer posição) e, por
    garantia, o valor depois de `-pa`/`-pas`/`-password` e `ISC_PASSWORD=`, mesmo de senha
    desconhecida. Toda linha do log e toda mensagem da saída padrão passam por ele.
  - Se o log não puder ser criado, o reparo não começa (código 4, configuração inválida).
  - Erro inesperado: detalhes completos (mascarados) no log; na tela, só o tipo do erro.
- Consequências: senha muito curta (ex.: 1 letra) faz o log ficar cheio de `********`; é o preço
  de nunca vazar. O cabeçalho do log registra computador, usuário do Windows, versão e configuração.

## D-008 — Menos etapas no plano
- Data: 2026-11-03
- Status: aprovada
- Contexto: o usuário pediu o menor número possível de etapas.
- Decisão: as etapas 4 a 13 viraram duas: 4 = reparo completo sem tela (todos os cenários do
  harness passando) e 5 = tela + publicação + revisão de segurança. A ordem interna foi mantida
  como partes (a, b, c...), e o agente roda o harness ao fim de cada parte antes de seguir.
- Consequências: cada `/implementar-etapa` é mais longo e o usuário revisa menos vezes; o risco é
  compensado pelo harness rodando a cada parte dentro da etapa.

## D-009 — Fluxo do reparo, ferramentas externas e desfazer
- Data: 2026-11-03
- Status: aprovada
- Contexto: etapa 4 (reparo completo sem tela); requisitos R01–R25 e problemas P01–P15.
- Decisão:
  - **Credenciais**: `gfix`/`gbak` recebem usuário e senha pelas variáveis `ISC_USER`/`ISC_PASSWORD`
    só do processo filho; a senha nunca vai na linha de comando (não aparece na lista de processos
    nem precisa de escape). Outras ferramentas não herdam essas variáveis. **Confirmar no teste
    real** que o gfix/gbak 2.5 da loja aceitam (é o comportamento padrão do cliente Firebird).
  - **Banco** sempre passado com caminho completo (resolve P15). Pasta de trabalho dos programas =
    pasta base (como o `CD\SYSPDV` do script).
  - **Ferramentas**: modo teste = sempre `<REPARO_FERRAMENTAS_DIR>\<nome>.cmd` via `cmd.exe /d /s /c`,
    sem cair para a ferramenta real se faltar. Produção = caminho do `reparo.json`, senão pasta base,
    senão PATH. Ferramentas obrigatórias são conferidas antes de começar.
  - **Serviço**: `net stop/start`. Erro 2 com mensagem 3521 (parado) / 2182 (já ligado) é aceito
    (número igual em todo idioma). Se `net stop` falhar ou passar do tempo limite:
    `taskkill /F /IM <processoFirebird>` (R25, troca o `tskill` do script — P05).
    No fim, sempre `net start` (R15).
  - **Etapas** (19): pré-checagens → parar → limpar → ligar → gfix -v → reiniciar → gfix -m →
    reiniciar → gfix -v → reiniciar → backup → parar → guardar banco → ligar → restore →
    VERIFICAINDICE → dbMonitor → atualizasyspdv_server → syspdv_server.
    Não críticas (viram aviso): limpeza, as três do gfix, dbMonitor, syspdv_server.
  - **Arquivos**: backup em `bkpBd\syspdv_srv_<data-hora>.fbk`; banco antigo em
    `bkpBd\syspdv_srv_<data-hora>.fdb`; nomes repetidos ganham `_2`, `_3`. Backup só vale com
    código 0 **e** arquivo > 0 bytes (conferido de novo antes de mover o banco). Backup que falhou
    é renomeado para `.incompleto` (não é apagado).
  - **Desfazer**: falha crítica até o restore → para o Firebird, apaga o banco parcial (só se o
    original estiver a salvo) e devolve o original. O restore bem-sucedido é o **ponto sem volta**:
    falha depois dele (índices, atualização) mantém o banco novo, que é válido, deixa o antigo
    guardado e pede para chamar o suporte, sem abrir o sistema.
  - **Limpeza**: padrões do script só na pasta base, sem subpastas; além disso nunca apaga
    `.exe .dll .ini .fdb .fbk .gdb .gbk .cfg .conf .json`, o banco nem o log do reparo (o `del` do
    script apagaria, por exemplo, um `srvconfig.ini`). Cada arquivo apagado vai para o log.
  - **Programas do SysPDV**: VERIFICAINDICE e atualizasyspdv_server: avisa o operador (com tela) e
    espera fechar, sem tempo limite. dbMonitor e syspdv_server: abre sem esperar e observa 10 s
    para saber se fechou com erro.
  - **Pré-checagens**: banco existe; administrador (só aviso no modo teste); ferramentas;
    espaço livre ≥ 2× o banco + 50 MB.
  - **Uma execução por vez**: mutex `Global\ReparoSysPDV`.
  - Novo código de saída **5**: falhou e não foi possível devolver o banco original (o mais grave).
- Consequências: o app faz mais reinícios do Firebird que o necessário (iguais ao script; podem ser
  revistos depois do teste real). O `.fbk` e o banco antigo ficam guardados em `bkpBd` a cada
  reparo: a pasta cresce e precisa de limpeza manual de vez em quando.

## D-010 — Tela, publicação e entrega
- Data: 2026-11-03
- Status: aprovada
- Contexto: etapa 5 (operador leigo, `.exe` único copiado para a loja, administrador).
- Decisão:
  - **Tela** (`JanelaPrincipal`): explicação curta, botão "Iniciar reparo", etapa atual + barra,
    lista de etapas com ✔ / ⚠ / ✖, resultado final em verde ou vermelho, caminho do log
    (selecionável, sem abrir o Explorer, para não executar processo fora do executor).
    O reparo roda em segundo plano (`Task.Run`); a janela não fecha durante o reparo; o botão
    Iniciar não volta a funcionar depois de um reparo (para repetir, abrir o app de novo).
    Problemas de configuração aparecem ao abrir. Sem senha configurada: janela pedindo a senha.
  - `IInteracao` ganhou `EtapaTerminou` e `MostrarResultado`; `SessaoReparo` (log + trava de
    execução única + erro inesperado) é a mesma nos dois modos.
  - Erro inesperado fora das etapas: o Firebird é religado num `finally` (R15).
  - **Publicação**: `scripts/publicar.ps1` → `dist\ReparoSysPDV.exe` (single-file, self-contained,
    `win-x64`, comprimido, ~68 MB) + `reparo.exemplo.json` + `LEIA-ME.txt`. Roda os testes antes.
  - **Administrador**: manifesto `requireAdministrator` só no `.exe` publicado
    (`-p:ExigirAdministrador=true`); o build normal não tem, para o harness (sem administrador)
    conseguir abrir o app. A pré-checagem de administrador continua valendo.
  - Instalação sugerida na loja: `C:\SYSPDV\ReparoBanco\` (subpasta: a limpeza não entra).
  - Roteiro de teste real em `docs/teste-real.md`.
- Consequências: o `.exe` publicado nunca deve ser executado no computador de desenvolvimento
  (usa `C:\SYSPDV` e as ferramentas reais).

## D-011 — Um único executável: escolher o banco e digitar a senha
- Data: 2026-11-03
- Status: aprovada (substitui partes de D-006 e D-010)
- Contexto: o usuário pediu que tudo se resuma a um único `.exe`, bastando escolher o banco.
- Decisão:
  - O pacote é só `ReparoSysPDV.exe`. O `reparo.json` virou opcional (técnico/linha de comando).
  - Tela: campo "Banco de dados" (já preenchido com `C:\SYSPDV\syspdv_srv.fdb` se existir) +
    "Procurar...", campo "Senha do banco" e "Iniciar reparo". A janela separada de senha saiu.
  - **Senha digitada na tela a cada reparo** (escolha do usuário). Na tela, a senha digitada vale
    mais que a do `reparo.json`. Nada de senha no código nem embutida no `.exe` (regra 5).
  - O banco escolhido define a pasta base (backup, log e limpeza ficam na pasta dele).
  - **Proteções da pasta** (R1): recusa raiz de disco, pastas do Windows, Arquivos de Programas,
    pasta do usuário, Área de Trabalho, Documentos e Downloads; e a pré-checagem exige que a pasta
    tenha `VERIFICAINDICE.exe` ou `syspdv_server.exe` (é a pasta do SysPDV) antes de parar o
    Firebird ou limpar qualquer coisa.
  - **Detecção do Firebird** (só leitura do registro, nunca no modo teste): caminho do executável
    do serviço (`HKLM\SYSTEM\CurrentControlSet\Services\<serviço>\ImagePath`) → pasta `bin`
    (`gfix`, `gbak`) e nome do processo; se o serviço configurado não existir, procura um serviço
    `Firebird*Server*` (nunca o Guardian); se não achar, usa `SOFTWARE\Firebird Project\Firebird
    Server\Instances\DefaultInstance`. Prioridade: variáveis > `reparo.json` > detecção > padrões.
    O log mostra os caminhos de `gfix`/`gbak` usados.
  - **Senha errada**: a primeira verificação (`gfix -v`) passa a ser crítica só para senha
    recusada (*"user name and password are not defined"*): para antes do backup, com mensagem clara.
  - **R4**: `net`, `taskkill` e `tskill` só da pasta do sistema do Windows.
- Consequências: o operador precisa saber a senha do banco. Se a detecção falhar numa loja, o
  técnico cria um `reparo.json` só com os caminhos.

## D-012 — Conferir o banco restaurado antes de seguir (R5)
- Data: 2026-11-04
- Status: aprovada
- Contexto: pendência R5 da revisão de segurança. Antes, o banco novo virava definitivo assim que o
  `gbak -c` terminava com código 0 e o arquivo não estava vazio, sem nenhuma validação.
- Decisão:
  - Nova etapa crítica logo depois do restore: **"Conferir o banco reconstruído"**, com
    `gfix -v -f` no banco novo (Firebird ligado, mesmas credenciais). Agora são 20 etapas.
  - O **ponto sem volta** passou do restore para essa conferência.
  - Se a conferência achar erro: o fluxo desfaz como numa falha de restore (para o Firebird, apaga
    o banco novo, devolve o original, liga o Firebird) e termina em VERMELHO. O `.fbk` continua em
    `bkpBd` para o suporte. VERIFICAINDICE, atualização e sistema não rodam.
  - Motivo: um restore recém-feito não deveria ter erro nenhum; se tiver, o original (já
    conhecido pela loja) é mais seguro, e nada se perde porque o backup fica guardado.
- Consequências: o reparo demora um pouco mais (uma validação a mais do banco). No teste real,
  conferir que a etapa 16 termina com ✔.

## D-013 — Modo sem janela só nos testes (R2)
- Data: 2026-11-04
- Status: aprovada (escolha do usuário; altera D-005)
- Contexto: pendência R2. Com `--nao-interativo` numa loja, o VERIFICAINDICE e a atualização abrem
  janela e o app espera sem tempo limite alguém fechá-las; sem ninguém na tela o reparo nunca
  termina. Isso também contraria o contrato (`docs/especificacao.md`: modo não interativo "não abre janelas").
  Alternativas descartadas: tempo limite que fecha o programa (pode interromper o VERIFICAINDICE
  no meio) e pular as janelas (banco fica com índices desativados).
- Decisão: `Configuracao.Validar` recusa o modo não interativo quando não há
  `REPARO_FERRAMENTAS_DIR` (fora do harness). O reparo termina como configuração inválida
  (código 4) antes da confirmação e antes de chamar qualquer programa. O harness continua igual.
- Consequências: na loja o reparo é sempre pela tela. O técnico perde a linha de comando; se ela
  voltar a ser necessária, precisa de outra solução para os programas que abrem janela.

## D-014 — Parar na primeira verificação quando não conecta ao banco
- Data: 2026-11-04
- Status: aprovada
- Contexto: no primeiro teste real (computador SERVIDOR, banco de 9 GB), todos os `gfix` e o
  `gbak` responderam `unknown ISC error 335545106` em menos de 1 s, sem abrir o banco. O app tratou
  isso como "banco com danos", reiniciou o Firebird três vezes e só parou no backup. O código
  provavelmente é o erro de login do Firebird 3+ (algo mais novo que a versão 2.5 atende a
  conexão); causa ainda em investigação na máquina.
- Decisão: a primeira verificação (`gfix -v`) também é crítica quando a resposta mostra que o banco
  nem abriu (lista fechada em `EtapasReparo.SinaisDeFalhaDeConexao`: `unknown ISC error`, erro de
  login, `unavailable database`, falha de rede, `CreateFile (open)`, ODS de outra versão). Para com
  "não foi possível conectar ao banco de dados" e a primeira linha da resposta do Firebird.
  Qualquer outra mensagem continua sendo dano no banco (aviso), como antes.
- Consequências: com versões do Firebird misturadas, o reparo para logo, sem reiniciar o serviço à
  toa. Uma mensagem de conexão desconhecida ainda cai no backup, que continua sendo a proteção.

## D-015 — gfix, gbak e fbclient embutidos, colocados na pasta do SysPDV
- Data: 2026-11-05
- Status: aprovada (escolha do usuário: raiz da pasta do SysPDV, não uma pasta só do reparador)
- Contexto: para o reparo funcionar de verdade, o SysPDV precisa do `gbak.exe`, `gfix.exe` e
  `fbclient.dll` (Firebird 2.5) na pasta dele. O `REPARO.CMD` já chamava `gfix`/`gbak` de dentro de
  `C:\SYSPDV`. Arquivos entregues pelo usuário em `terceiros\firebird\` (2.5.1.26351, 32 bits, + `firebird.msg`),
  idênticos aos do `C:\Syspdv` do computador de desenvolvimento. Dependem do MSVCR80 (Visual C++
  2005 x86) por manifesto.
- Decisão:
  - Os 4 arquivos vão como recurso dentro do `Reparo.Core` (e portanto do `ReparoSysPDV.exe`).
  - Nova etapa crítica 2, logo depois das pré-checagens (que confirmam que é a pasta do SysPDV) e
    antes de parar o Firebird: **"Colocar o gfix, o gbak e o fbclient na pasta do SysPDV"**.
    Por arquivo, nesta ordem: `fbclient.dll`, `gbak.exe`, `gfix.exe`, `firebird.msg`.
    - Igual (SHA256): não mexe.
    - Faltando: grava `<nome>.reparo-novo`, confere o SHA256 e só então renomeia para o nome final.
    - Diferente: grava e confere o novo primeiro; depois move o antigo para
      `bkpBd\ferramentas-anteriores_<data-hora>\` (nunca apaga, nunca sobrescreve) e coloca o novo.
      Se colocar o novo falhar, devolve o antigo. Termina com aviso listando o que foi trocado.
    - Arquivo preso (SysPDV aberto): para o reparo antes de mexer no Firebird, pedindo para fechar.
  - Os arquivos ficam na pasta depois do reparo (o SysPDV precisa deles); não há "desfazer".
  - `gfix`/`gbak` passam a rodar de `<pasta do SysPDV>\gfix.exe|gbak.exe` (vence a detecção; o
    `ferramentas` do `reparo.json` ainda vence tudo). `"copiarProgramas": false` (D-016) no
    `reparo.json` desliga a cópia. **Nunca no modo teste** (harness inalterado).
  - Pré-checagem: sem o runtime Visual C++ 2005 x86 (WinSxS ou `msvcr80.dll` + manifesto na pasta),
    recusa antes de copiar. Primeira verificação: `gfix` que nem abre também para o reparo.
- Consequências: o pacote cresce ~1 MB. Trocar o `fbclient.dll` da pasta muda o cliente usado por
  todos os programas do SysPDV daquela pasta; o anterior fica guardado para voltar à mão se preciso.

## D-016 — VerificaIndice e dbMonitor também embutidos
- Data: 2026-11-05
- Status: aprovada (pedido do usuário; altera D-015)
- Contexto: a instalação padrão do SysPDV não traz o `VerificaIndice.exe` nem o `dbMonitor.exe`
  (confirmado no computador de desenvolvimento: sem eles, a pré-checagem recusava o reparo). O
  usuário garantiu que são sempre os mesmos arquivos, qualquer que seja a versão do SysPDV.
  Entregues em `terceiros\syspdv\`: `VerificaIndice.exe` (32 bits, compactado com UPX, sem versão) e
  `dbMonitor.exe` (Devart dbMonitor 3.0.3.0, 32 bits). Ambos só importam DLLs do Windows.
- Decisão:
  - Entram no mesmo conjunto embutido de D-015 (`ProgramasDoReparo`), copiados na mesma etapa 2,
    com as mesmas regras (igual não mexe; diferente vai para `bkpBd\ferramentas-anteriores_...`).
    A etapa passou a se chamar **"Colocar os programas do reparo na pasta do SysPDV"**.
  - `VERIFICAINDICE` e `dbMonitor` rodam de `<pasta do SysPDV>\VerificaIndice.exe|dbMonitor.exe`;
    as pré-checagens deixam de cobrar esses dois (e o gfix/gbak), porque o reparo os leva.
  - Chave do `reparo.json` renomeada para `"copiarProgramas"` (era `copiarFerramentasFirebird`,
    nunca usada em loja).
  - A pré-checagem "é a pasta do SysPDV?" continua exigindo `VERIFICAINDICE.exe` ou
    `syspdv_server.exe` já existentes (na instalação padrão, o `syspdv_server.exe`).
- Consequências: o pacote cresce mais ~1 MB. Se uma loja tiver outra versão desses programas, ela
  é trocada pela do reparador (a anterior fica guardada).

## D-017 — Reiniciar o Firebird antes de conferir o banco reconstruído
- Data: 2026-11-05
- Status: aprovada (corrige D-012)
- Contexto: no teste real de 05/11, backup e restore deram certo, mas a conferência (`gfix -v -f`)
  respondeu *"secondary server attachments cannot validate databases"*: no Firebird 2.5 a validação
  só roda sem outras conexões no banco. D-012 não reiniciava o serviço antes (o script original
  reinicia antes de cada `gfix`). O reparo devolveu o banco antigo sem necessidade.
- Decisão:
  - Nova etapa "Reiniciar o Firebird" entre o restore e "Conferir o banco reconstruído".
  - Se mesmo assim o `gfix` recusar por esse motivo (algum programa se conectou), a conferência vira
    **aviso** e o reparo segue: não é sinal de dano e o restore já terminou com código 0. Qualquer
    outro erro do `gfix` continua devolvendo o banco original.
- Consequências: mais ~5 s no reparo. Etapas: 21 no harness, 22 com a cópia dos programas.

## D-018 — Tela única, minimalista, com tema claro/escuro
- Data: 2026-11-05
- Status: aprovada (pedido do usuário: "uma tela só, mais minimalista e moderna")
- Decisão:
  - Continua WinForms, uma janela só. Componentes desenhados à mão em `Reparo.App\Visual\`
    (cartão arredondado, botão primário/secundário, campo com ícone e borda de foco/erro, barra de
    progresso fina, lista de etapas em linha do tempo com ✓ / ! / ✕).
  - Segue o tema do Windows (claro/escuro, inclusive a barra de título). Ícones da fonte do próprio
    Windows (Segoe MDL2 / Fluent Icons); nenhum arquivo de imagem.
  - Ordem: cabeçalho → aviso dos caixas → banco e senha → "Iniciar reparo". Ao iniciar, a mesma
    janela troca para o acompanhamento (etapa atual, barra, linha do tempo) e, no fim, mostra o
    resultado (verde/vermelho; amarelo quando nada chegou a ser feito) e o caminho do log.
  - Erros de preenchimento aparecem embaixo do campo, não em janela de mensagem. A confirmação dos
    caixas e os avisos antes do VERIFICAINDICE continuam em janela de mensagem do Windows.
  - `scripts/previa-tela.ps1` gera as imagens de cada estado sem executar reparo nenhum.
- Consequências: a lógica (Core), o harness e o modo sem janela não mudaram. A troca de tema do
  Windows com o programa aberto só vale ao abrir de novo.

## D-019 — Ícone do programa
- Data: 2026-11-05
- Status: aprovada
- Decisão: `src\Reparo.App\app.ico` = o mesmo selo do cabeçalho da tela (quadrado azul arredondado,
  degradê 3B82F6→1D4ED8, chave inglesa branca da fonte Segoe MDL2). Tamanhos 16 a 256 px, com
  traço reforçado nos pequenos. Gerado por `scripts/gerar-icone.ps1` (só rodar de novo se o
  desenho mudar). Usado no `.exe` (`ApplicationIcon`) e na janela (barra de título e de tarefas).

## D-020 — Reconhecer a pasta do SysPDV por qualquer programa do SysPDV
- Data: 2026-11-05
- Status: aprovada (altera a proteção R1 de D-011)
- Contexto: em outro PC (DESKTOP-5QJ3953), `C:\Syspdv` não tinha `VerificaIndice.exe` nem
  `Syspdv_server.exe` com esse nome, e o reparo recusou a pasta. Os nomes dos programas variam
  entre instalações (`Syspdv_pdv.exe`, `SysPDV_Service.exe`, `AtualizaSysPDV_SERVER.exe`...).
- Decisão: a pasta é aceita se tiver, direto nela, algum `.exe` cujo nome comece com `Syspdv` ou
  `AtualizaSysPDV`, ou o `VerificaIndice.exe`. O `ReparoSysPDV.exe` não conta. Ao recusar, o log
  lista os programas que existem na pasta, para o suporte ver o que havia lá.
- Consequências: a proteção continua (pastas do Windows, raiz, Área de Trabalho etc. seguem
  bloqueadas por D-011, e uma pasta qualquer sem programas do SysPDV é recusada), mas aceita
  instalações com nomes diferentes.

## D-021 — Logo da PBtec e aba "Sobre"
- Data: 2026-11-06
- Status: aprovada (pedido do usuário)
- Decisão:
  - Logos em `assets\logos` viram `src\Reparo.App\Recursos\logo-claro.png` (colorido,
    tema claro) e `logo-escuro.png` (branco/vermelho, tema escuro; o fundo preto vira transparente).
    Gerados por `scripts/gerar-logos.ps1` (recorta e reduz) e embutidos no `.exe`. Altera o
    "nenhum arquivo de imagem" de D-018.
  - O logo do tema atual substitui o selo azul do cabeçalho. Se não carregar, volta o selo.
  - Abaixo do cabeçalho há duas abas: "Reparo" (a tela de sempre) e "Sobre" (logo grande,
    PBtec - Soluções em Automação, (83) 3224-3080, equipe de Desenvolvimento e Análise e a versão).
    Trocar de aba não interrompe o reparo; Enter só inicia o reparo com a aba "Reparo" aberta.
  - Troca de aba animada (~0,3 s): o sublinhado azul desliza até a aba clicada; a página antiga some
    e a nova entra deslizando de lado. Sem animação quando o Windows está com as animações
    desligadas. Equipe na ordem: José Henrique, Erick Henrique, Eduardo Vasconcelos.
  - O ícone do `.exe` (`app.ico`, D-019) não mudou: o Windows usa um ícone só, sem tema.

## D-022 — Repositório público no GitHub, sem programas de terceiros
- Data: 2026-11-09
- Status: aprovada (escolha do usuário)
- Contexto: o projeto vai para um repositório **público** no GitHub. Os programas embutidos
  (D-015, D-016) são de terceiros: Firebird (código aberto) e SysPDV (software proprietário).
- Decisão:
  - Os programas ficam em `terceiros\firebird\` e `terceiros\syspdv\` e **não vão para o git**
    (`.gitignore`). `terceiros\LEIA-ME.md` (versionado) diz quais arquivos colocar lá.
  - Sem eles, o build do `Reparo.Core` para com uma mensagem clara apontando para esse LEIA-ME.
  - Logos da PBtec em `assets\logos\`; prévias da tela em `docs\previa-tela\` (usadas no README).
    O modelo de README de perfil que estava em `referencia-design\` foi apagado (não era do projeto).
  - `dist\`, `bin\`, `obj\` e `harness\.sandbox\` continuam fora do git.
- Consequências: quem clonar precisa obter os 6 arquivos por conta própria antes de compilar.

## D-023 — Pastas no padrão .NET: solução na raiz, testes em `tests\`
- Data: 2026-11-09
- Status: aprovada (pedido do usuário: organizar as pastas antes de subir para o GitHub)
- Contexto: a solução ficava em `src\Reparo.sln`, os testes dentro de `src\` e o `Reparo.Core`
  tinha 17 arquivos soltos na mesma pasta.
- Decisão:
  - `Reparo.sln` na raiz (`dotnet test` funciona direto da raiz).
  - Testes de unidade em `tests\Reparo.Testes\`; o código do app continua em `src\`.
    `src\Reparo.App` não mudou de lugar, então `harness\harness.config.json` não precisou mudar.
  - `Directory.Build.props` na raiz com o que é comum aos projetos (`net8.0-windows`,
    `Nullable`, `ImplicitUsings`).
  - `Reparo.Core` em subpastas por assunto: `Fluxo` (motor e etapas), `Configuracao`,
    `Firebird` (serviço e detecção), `Sistema` (processos externos, arquivos, Windows),
    `Log` (log e máscara da senha) e `Programas` (programas embutidos). Na raiz ficam
    `Reparador`, `IInteracao` e `CodigoSaida`. O namespace continua `Reparo.Core` em todos os
    arquivos: as pastas são só para organizar, nenhum código mudou.
- Consequências: só caminhos mudaram (solução, referências entre projetos, `scripts\publicar.ps1`,
  `scripts\previa-tela.ps1`, documentação). Referências antigas a `src\Reparo.sln` nas decisões
  anteriores valem como histórico.

## D-024 — `.exe` publicado nos releases do GitHub
- Data: 2026-11-09
- Status: aprovada (escolha do usuário)
- Contexto: o usuário quer o `ReparoSysPDV.exe` disponível nos releases do repositório público.
  O `.exe` leva dentro os programas de D-015 e D-016, incluindo o VerificaIndice (SysPDV) e o
  dbMonitor (Devart), que são proprietários e por isso ficam fora do código-fonte (D-022).
- Decisão:
  - O `.exe` completo vai para os releases do GitHub, com os programas de terceiros embutidos.
    O usuário confirmou que a PBtec pode redistribuir o VerificaIndice e o dbMonitor.
  - Cada release leva junto o `LICENSE` e o `NOTICE` (a Apache 2.0 exige o `NOTICE` em toda
    redistribuição). `scripts\publicar.ps1` passou a copiar os dois para `dist\`.
  - O `NOTICE` diz que o `.exe` dos releases contém esses programas, que eles não são cobertos
    pela Apache 2.0, e onde está o código-fonte do Firebird (exigência da IPL).
  - Tag do release = `v` + `<Version>` do `Reparo.App.csproj` (primeira: `v1.0.0`).
- Consequências: o código-fonte continua sem programas de terceiros; quem baixa o release recebe
  o pacote pronto para a loja.

## D-025 — Banco fora do lugar durante o gfix e o backup; resumo do gfix lido de verdade
- Data: 2026-11-11
- Status: aprovada (escolha do usuário entre mover o banco, `gfix -shut` e trocar a porta)
- Contexto: no reparo real de 11/11 (banco de 5,9 GB), o `gfix -m` (conserto) e a 3ª verificação
  foram recusados com *"bad parameters on attach or create database"* / *"secondary server
  attachments cannot validate databases"*: outro programa se conectava ao banco segundos depois de
  o Firebird ligar (o `firebird.log` registrou 2 conexões ao reiniciar; o `SysPDVService.exe`
  rodava na máquina). Sem o conserto, o `gbak` parou num registro danificado da tabela
  `TRANSACAO_XMLNOTA` (*"wrong record length"*, bugcheck). Também se viu que:
  - o `gfix -v` do Firebird 2.5 sai com código 0 mesmo listando erros; o app dizia "nenhum problema
    encontrado" com 8 erros no resumo;
  - quando o gfix nem entrava no banco, o app dizia "o gfix encontrou problemas".
- Alternativas: trocar o IP do servidor (mexe na rede inteira) ou a porta no `firebird.conf` (como
  a equipe fazia à mão) só barram os caixas da rede, não programas do próprio servidor (conexão
  local). `gfix -shut single` só barra quem não é SYSDBA, e o SysPDV costuma entrar como SYSDBA.
- Decisão:
  - "Guardar o banco atual na pasta de backup" passa para logo depois da limpeza, com o Firebird
    parado. Os três `gfix` e o backup rodam no arquivo guardado (`bkpBd\syspdv_srv_<data-hora>.fdb`),
    que nenhum caixa ou programa local conhece, com qualquer usuário. O restore cria o banco novo no
    lugar de sempre. Saíram as etapas "Parar"/"Ligar" que ficavam em volta do antigo "Guardar".
  - O desfazer é o mesmo de antes (para o Firebird, apaga um banco parcial, devolve o original),
    agora valendo também para falha no backup. A mensagem do backup deixou de dizer "o banco não
    foi alterado" (o `gfix -m` pode ter mexido nele); a mensagem final diz que ele foi devolvido.
  - Erro inesperado fora das etapas (ex.: a tela caiu) com o banco fora do lugar: o `Reparador`
    devolve o original antes de relançar o erro, salvo se o banco novo já foi conferido.
  - Se o banco não estiver no lugar ao começar, as pré-checagens apontam o `.fdb` mais recente de
    `bkpBd` (reparo anterior interrompido, ex.: falta de luz).
  - `gfix`: o resumo ("Number of ... errors : N") é somado; código 0 com erros vira aviso com a
    quantidade (e, na conferência do banco reconstruído, falha). "bad parameters on attach..."
    também conta como "outro programa conectado": aviso dizendo que a etapa não foi feita.
  - R11 revisada na especificação.
- Consequências: os caixas recebem "arquivo não encontrado" (em vez de "banco em uso") até o
  restore terminar. Se a luz cair no meio, o sistema não abre até alguém devolver o banco de
  `bkpBd` (o próximo reparo aponta onde ele está). Etapas: 19 no harness, 20 com a cópia dos
  programas. O harness não mudou: ele não simula conexões simultâneas, então esse caso fica coberto
  pelos testes de unidade e pelo teste real.

## D-026 — Esperar o processo do Firebird fechar; repetir o conserto que perdeu a conexão
- Data: 2026-11-11
- Status: aprovada (pedido do usuário)
- Contexto: no segundo reparo real de 11/11, já com D-025, o reinício do Firebird depois da 1ª
  verificação ligou o serviço com o `fbserver.exe` antigo ainda fechando. O `firebird.log` registrou
  *"XNET server initialization failed. Probably another instance of server is already running"*
  (o XNET é a conexão local que o gfix e o gbak usam). O `gfix -m` ficou 355 s esperando e desistiu
  com *"Error reading data from the connection – tempo limite de espera"*, sem mexer no banco: a 3ª
  verificação achou os mesmos 8 erros da 1ª e o backup parou no mesmo registro da `TRANSACAO_XMLNOTA`.
  O app ainda chamou isso de "o gfix encontrou problemas".
- Decisão:
  - Depois de cada `net stop` que deu certo (ou "já estava parado"), o app espera o processo
    configurado (`processoFirebird`, padrão `fbserver.exe`) sumir, até `tempoLimitePararServicoSegundos`.
    Se não sumir, encerra com `taskkill` (aviso "o Firebird demorou a fechar"), como em R25.
    No modo teste não espera: o harness não tem Firebird de verdade, e o desta máquina não conta.
  - Nova consulta `IAmbienteSistema.EsperarProcessoTerminar` (lista de processos do .NET, sem
    processo externo).
  - "Error reading data from the connection", "Error writing data to the connection" e
    "connection shutdown" contam como "perdeu a conexão com o Firebird": aviso dizendo que a etapa
    não foi feita (não é dano no banco).
  - "Consertar o banco de dados (2 de 3)": se o `gfix -m` perder a conexão ou for barrado por outra
    conexão, o app reinicia o Firebird e tenta **mais uma vez**. Só uma: se falhar de novo, fica o aviso.
  - Quando o `gfix -m` termina listando erros no resumo, o aviso diz "o conserto tratou N erro(s)"
    (antes dizia "encontrou", o que parecia falha). No terceiro reparo real de 11/11, com D-026, o
    conserto tratou 8 erros, a página danificada da `TRANSACAO_XMLNOTA` saiu da lista e o reparo
    terminou com sucesso (472.256 registros nessa tabela no backup e no restore).
- Consequências: cada parada do Firebird pode levar alguns segundos a mais. Se houver outra instância
  do Firebird com o mesmo nome de processo, a espera vai até o tempo limite e o `taskkill` encerra as
  duas (já era assim quando o `net stop` falhava).
