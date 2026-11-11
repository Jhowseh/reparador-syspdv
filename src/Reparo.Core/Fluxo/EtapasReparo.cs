namespace Reparo.Core;

/// <summary>
/// Monta as etapas do reparo (mesmo fluxo do REPARO.CMD, com checagens e desfazer).
/// Ver docs/especificacao.md seção 2 e D-009.
/// </summary>
public sealed class EtapasReparo
{
    /// <summary>Padrões de limpeza do script original (R03).</summary>
    public static readonly IReadOnlyList<string> PadroesLimpeza = new[] { "srv*", "*log", "*err", "banc*", "*txt" };

    /// <summary>Extensões que a limpeza nunca apaga, mesmo que o nome combine com um padrão.</summary>
    public static readonly IReadOnlySet<string> ExtensoesProtegidas = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".ini", ".fdb", ".fbk", ".gdb", ".gbk", ".cfg", ".conf", ".json",
    };

    /// <summary>Ferramentas sem as quais o reparo não pode começar.</summary>
    public static readonly IReadOnlyList<string> FerramentasObrigatorias = new[]
    {
        "net", "gfix", "gbak", "VERIFICAINDICE", "atualizasyspdv_server",
    };

    /// <summary>Ferramentas que, se faltarem, só geram aviso.</summary>
    public static readonly IReadOnlyList<string> FerramentasOpcionais = new[] { "taskkill", "dbMonitor", "syspdv_server" };

    /// <summary>Ferramentas que o próprio reparo leva e coloca na pasta (D-015, D-016).</summary>
    private static readonly IReadOnlySet<string> ProgramasEmbutidos =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "gfix", "gbak", "VERIFICAINDICE", "dbMonitor" };

    private readonly Configuracao _cfg;
    private readonly IExecutorProcessos _executor;
    private readonly IRegistro _registro;
    private readonly IInteracao _interacao;
    private readonly IAmbienteSistema _ambiente;
    private readonly ControleFirebird _firebird;
    private readonly IReadOnlyList<ArquivoEmbutido> _programas;
    private readonly string _carimbo;

    /// <summary>Arquivo .fbk desta execução (dentro da pasta de backup).</summary>
    public string CaminhoBackupFbk { get; }

    /// <summary>Onde o banco original fica guardado durante e depois do restore.</summary>
    public string CaminhoBancoGuardado { get; private set; }

    /// <summary>Verdadeiro depois que o banco original saiu do lugar (e ainda não voltou).</summary>
    public bool BancoOriginalForaDoLugar { get; private set; }

    public EtapasReparo(Configuracao configuracao, IExecutorProcessos executor, IRegistro registro,
        IInteracao interacao, IAmbienteSistema ambiente, ControleFirebird firebird, DateTime agora,
        IReadOnlyList<ArquivoEmbutido>? programas = null)
    {
        _cfg = configuracao;
        _executor = executor;
        _registro = registro;
        _interacao = interacao;
        _ambiente = ambiente;
        _firebird = firebird;
        _programas = programas ?? Array.Empty<ArquivoEmbutido>();
        _carimbo = agora.ToString("yyyyMMdd_HHmmss");

        var nomeBanco = Path.GetFileNameWithoutExtension(_cfg.CaminhoBanco);
        CaminhoBackupFbk = Arquivos.NomeLivre(Path.Combine(_cfg.PastaBackupBanco, $"{nomeBanco}_{_carimbo}.fbk"));
        CaminhoBancoGuardado = Arquivos.NomeLivre(Path.Combine(_cfg.PastaBackupBanco, $"{nomeBanco}_{_carimbo}{Path.GetExtension(_cfg.CaminhoBanco)}"));
    }

    /// <summary>Para onde vão os programas da pasta que forem substituídos (D-015, D-016).</summary>
    public string PastaFerramentasAnteriores => Path.Combine(_cfg.PastaBackupBanco, $"ferramentas-anteriores_{_carimbo}");

    public IReadOnlyList<Etapa> Montar()
    {
        var etapas = MontarFluxo().ToList();
        if (_cfg.CopiarProgramas)
        {
            // Depois das pré-checagens (só escreve na pasta se ela for mesmo a do SysPDV) e antes de parar o Firebird.
            etapas.Insert(1, new Etapa
            {
                Descricao = "Colocar os programas do reparo na pasta do SysPDV",
                Executar = () => new InstaladorFerramentas(_cfg.PastaBase, PastaFerramentasAnteriores, _registro)
                    .Instalar(_programas),
            });
        }
        return etapas;
    }

    private IEnumerable<Etapa> MontarFluxo() => new List<Etapa>
    {
        new() { Descricao = "Conferir se está tudo pronto", Executar = PreChecagens },
        new() { Descricao = "Parar o Firebird", Executar = _firebird.Parar },
        new() { Descricao = "Apagar arquivos temporários", Executar = Limpar, Critica = false },
        // D-025: com o banco fora da pasta, nenhum caixa nem programa local consegue se conectar a ele
        // (com qualquer usuário). O gfix e o backup trabalham no arquivo guardado; se algo falhar, ele volta.
        new() { Descricao = "Guardar o banco atual na pasta de backup", Executar = GuardarBancoOriginal, Desfazer = DevolverBancoOriginal },
        new() { Descricao = "Ligar o Firebird", Executar = _firebird.Ligar },
        // Crítica só por causa da senha: erros de validação viram aviso; senha errada para tudo.
        new() { Descricao = "Verificar o banco de dados (1 de 3)", Executar = PrimeiraVerificacao },
        new() { Descricao = "Reiniciar o Firebird", Executar = _firebird.Reiniciar },
        new() { Descricao = "Consertar o banco de dados (2 de 3)", Executar = Consertar, Critica = false },
        new() { Descricao = "Reiniciar o Firebird", Executar = _firebird.Reiniciar },
        new() { Descricao = "Verificar o banco de dados (3 de 3)", Executar = () => Gfix("-v", "-f"), Critica = false },
        new() { Descricao = "Reiniciar o Firebird", Executar = _firebird.Reiniciar },
        new() { Descricao = "Fazer a cópia de segurança (backup)", Executar = Backup },
        new() { Descricao = "Reconstruir o banco a partir do backup (restore)", Executar = Restore },
        // O gfix -v só valida sem outras conexões abertas (Firebird 2.5): reinicia antes, como nas outras verificações.
        new() { Descricao = "Reiniciar o Firebird", Executar = _firebird.Reiniciar },
        // R5: só depois de conferido o banco novo vira definitivo; se falhar, o original volta ao lugar.
        new() { Descricao = "Conferir o banco reconstruído", Executar = ConferirBancoNovo, PontoSemVolta = true },
        new() { Descricao = "Reativar os índices (VERIFICAINDICE)", Executar = () => ProgramaComJanela("VERIFICAINDICE", "reativar os índices") },
        new() { Descricao = "Abrir o monitor do banco (dbMonitor)", Executar = () => ProgramaSemEsperar("dbMonitor"), Critica = false },
        new() { Descricao = "Atualizar o banco de dados (atualizasyspdv_server)", Executar = () => ProgramaComJanela("atualizasyspdv_server", "atualizar o banco") },
        new() { Descricao = "Abrir o sistema (syspdv_server)", Executar = () => ProgramaSemEsperar("syspdv_server"), Critica = false },
    };

    // ------------------------------------------------------------------ pré-checagens (R22)

    private ResultadoEtapa PreChecagens()
    {
        var avisos = new List<string>();

        if (!File.Exists(_cfg.CaminhoBanco))
        {
            // D-025: um reparo interrompido (ex.: falta de luz) pode ter deixado o banco em bkpBd.
            var guardado = UltimoBancoGuardado();
            return ResultadoEtapa.Falha($"o banco de dados não foi encontrado em {_cfg.CaminhoBanco}" +
                (guardado is null ? "" : $". Se um reparo anterior foi interrompido, o banco pode estar em {guardado}. Chame o suporte"));
        }

        // A limpeza apaga srv*, *log, *txt... da pasta do banco: só se for mesmo a pasta do SysPDV.
        if (!PareceAPastaDoSysPdv())
        {
            var programas = ProgramasDaPasta();
            _registro.Aviso($"Programas encontrados em {_cfg.PastaBase}: " +
                            (programas.Count == 0 ? "nenhum" : string.Join(", ", programas.Take(40))));
            return ResultadoEtapa.Falha($"a pasta {_cfg.PastaBase} não parece ser a do SysPDV " +
                                        "(não tem nenhum programa do SysPDV, como Syspdv_server, Syspdv_pdv ou AtualizaSysPDV). " +
                                        "Escolha o banco que fica dentro da pasta do SysPDV");
        }

        if (!_ambiente.EhAdministrador())
        {
            if (!_cfg.ModoTeste)
                return ResultadoEtapa.Falha("o app precisa ser aberto como administrador (clique com o botão direito > Executar como administrador)");
            avisos.Add("sem privilégio de administrador (aceito no modo teste)");
        }

        foreach (var ferramenta in FerramentasObrigatorias)
        {
            // Programas embutidos: a etapa seguinte é que os coloca na pasta.
            if (_cfg.CopiarProgramas && ProgramasEmbutidos.Contains(ferramenta))
                continue;
            if (_executor.Localizar(ferramenta) is null)
                return ResultadoEtapa.Falha($"o programa \"{ferramenta}\" não foi encontrado; confira o reparo.json");
        }

        if (_cfg.CopiarProgramas && !_ambiente.TemRuntimeVisualC2005x86(_cfg.PastaBase))
            return ResultadoEtapa.Falha("falta neste computador o \"Microsoft Visual C++ 2005 Redistributable (x86)\", " +
                                        "que o gfix e o gbak precisam. Instale o Firebird 2.5 ou esse pacote e tente de novo");

        foreach (var ferramenta in FerramentasOpcionais)
        {
            if (_cfg.CopiarProgramas && ProgramasEmbutidos.Contains(ferramenta))
                continue;
            if (_executor.Localizar(ferramenta) is null)
                avisos.Add($"o programa \"{ferramenta}\" não foi encontrado");
        }

        // Backup (.fbk) + banco novo: precisa de mais ou menos 2x o tamanho do banco (P10).
        var tamanhoBanco = new FileInfo(_cfg.CaminhoBanco).Length;
        var necessario = tamanhoBanco * 2 + 50L * 1024 * 1024;
        var livre = _ambiente.EspacoLivreBytes(_cfg.PastaBase);
        if (livre is null)
            avisos.Add("não foi possível conferir o espaço livre em disco");
        else if (livre < necessario)
            return ResultadoEtapa.Falha($"pouco espaço em disco: precisa de {Mb(necessario)} MB livres e há {Mb(livre.Value)} MB");

        Directory.CreateDirectory(_cfg.PastaBackupBanco);
        _registro.Info($"Banco: {_cfg.CaminhoBanco} ({Mb(tamanhoBanco)} MB)");
        _registro.Info($"O backup será gravado em: {CaminhoBackupFbk}");
        _registro.Info($"O banco atual será guardado em: {CaminhoBancoGuardado}");

        return avisos.Count == 0 ? ResultadoEtapa.Ok() : ResultadoEtapa.Aviso(string.Join("; ", avisos));
    }

    private string? UltimoBancoGuardado()
    {
        if (!Directory.Exists(_cfg.PastaBackupBanco))
            return null;

        var nome = Path.GetFileNameWithoutExtension(_cfg.CaminhoBanco);
        var extensao = Path.GetExtension(_cfg.CaminhoBanco);
        try
        {
            return Directory.EnumerateFiles(_cfg.PastaBackupBanco, $"{nome}_*{extensao}", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // ------------------------------------------------------------------ limpeza (R03, R14)

    private ResultadoEtapa Limpar()
    {
        var apagados = 0;
        var problemas = new List<string>();

        foreach (var arquivo in ArquivosParaLimpar())
        {
            try
            {
                File.Delete(arquivo);
                apagados++;
                _registro.Info($"Apagado: {arquivo}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problemas.Add(Path.GetFileName(arquivo));
                _registro.Aviso($"Não foi possível apagar {arquivo}: {ex.Message}");
            }
        }

        _registro.Info($"{apagados} arquivo(s) temporário(s) apagado(s).");
        return problemas.Count == 0
            ? ResultadoEtapa.Ok($"{apagados} arquivo(s) apagado(s)")
            : ResultadoEtapa.Aviso($"não foi possível apagar: {string.Join(", ", problemas)}");
    }

    /// <summary>Só arquivos da própria pasta base (sem subpastas) que combinam com os padrões.</summary>
    public IReadOnlyList<string> ArquivosParaLimpar()
    {
        if (!Directory.Exists(_cfg.PastaBase))
            return Array.Empty<string>();

        return Directory.EnumerateFiles(_cfg.PastaBase, "*", SearchOption.TopDirectoryOnly)
            .Where(caminho =>
            {
                var nome = Path.GetFileName(caminho);
                return CombinaComLimpeza(nome)
                       && !ExtensoesProtegidas.Contains(Path.GetExtension(nome))
                       && !MesmoArquivo(caminho, _cfg.CaminhoBanco)
                       && !MesmoArquivo(caminho, _registro.CaminhoArquivo);
            })
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// A limpeza apaga srv*, *log, *txt... da pasta: só seguimos se ela tiver algum programa do SysPDV
    /// (D-020). O nome do programa varia entre instalações, por isso vale qualquer Syspdv*.exe ou
    /// AtualizaSysPDV*.exe (o ReparoSysPDV.exe não conta). O VerificaIndice também conta.
    /// </summary>
    private bool PareceAPastaDoSysPdv() => ProgramasDaPasta().Any(EhProgramaDoSysPdv);

    internal static bool EhProgramaDoSysPdv(string nome) =>
        nome.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
        && (nome.StartsWith("syspdv", StringComparison.OrdinalIgnoreCase)
            || nome.StartsWith("atualizasyspdv", StringComparison.OrdinalIgnoreCase)
            || nome.Equals("VerificaIndice.exe", StringComparison.OrdinalIgnoreCase));

    private IReadOnlyList<string> ProgramasDaPasta()
    {
        try
        {
            return Directory.EnumerateFiles(_cfg.PastaBase, "*.exe", SearchOption.TopDirectoryOnly)
                .Select(c => Path.GetFileName(c))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    public static bool CombinaComLimpeza(string nome) =>
        nome.StartsWith("srv", StringComparison.OrdinalIgnoreCase)
        || nome.StartsWith("banc", StringComparison.OrdinalIgnoreCase)
        || nome.EndsWith("log", StringComparison.OrdinalIgnoreCase)
        || nome.EndsWith("err", StringComparison.OrdinalIgnoreCase)
        || nome.EndsWith("txt", StringComparison.OrdinalIgnoreCase);

    // ------------------------------------------------------------------ validação e mend

    private ResultadoEtapa PrimeiraVerificacao() => Gfix(true, "-v", "-f");

    private ResultadoEtapa Gfix(params string[] opcoes) => Gfix(false, opcoes);

    private ResultadoEtapa Gfix(bool conferirSenha, params string[] opcoes) =>
        InterpretarGfix(ExecutarGfix(opcoes), conferirSenha);

    /// <summary>
    /// D-026: o conserto é o que deixa o backup passar pelos registros danificados. Se o gfix nem
    /// conseguiu falar com o banco, reinicia o Firebird e tenta mais uma vez.
    /// </summary>
    private ResultadoEtapa Consertar()
    {
        var opcoes = new[] { "-m", "-f", "-i" };
        var r = ExecutarGfix(opcoes);
        if (r.Sucesso || !(PerdeuConexao(r.Saida) || NaoPodeValidarAgora(r.Saida)))
            return InterpretarConserto(r);

        _registro.Aviso("O conserto não conseguiu falar com o banco. Reiniciando o Firebird para tentar mais uma vez.");
        var reinicio = _firebird.Reiniciar();
        if (reinicio.Status == StatusEtapa.Falha)
            return ResultadoEtapa.Aviso($"o conserto não foi feito e o Firebird não reiniciou ({reinicio.Mensagem})");

        return InterpretarConserto(ExecutarGfix(opcoes));
    }

    /// <summary>O resumo do gfix -m lista o que ele tratou: não é sinal de que o conserto falhou.</summary>
    private ResultadoEtapa InterpretarConserto(ResultadoProcesso r)
    {
        var erros = ErrosDeValidacao(r.Saida);
        if (r.Sucesso && erros > 0)
            return ResultadoEtapa.Aviso($"o conserto tratou {erros} erro(s) do banco; o reparo vai continuar");

        return InterpretarGfix(r, conferirSenha: false);
    }

    // D-025: o gfix trabalha no banco já guardado em bkpBd, onde ninguém mais o encontra.
    private ResultadoProcesso ExecutarGfix(string[] opcoes) =>
        _executor.Executar("gfix", opcoes.Append(CaminhoBancoGuardado).ToArray(),
            new OpcoesProcesso { UsarCredenciais = true });

    private ResultadoEtapa InterpretarGfix(ResultadoProcesso r, bool conferirSenha)
    {
        // O gfix 2.5 termina com código 0 mesmo listando erros: o resumo é que diz se há danos.
        var erros = ErrosDeValidacao(r.Saida);
        if (r.Sucesso && erros == 0)
            return ResultadoEtapa.Ok("nenhum problema encontrado");

        if (r.Sucesso)
            return ResultadoEtapa.Aviso($"o gfix encontrou {erros} erro(s) no banco; o reparo vai continuar");

        if (conferirSenha && SenhaRecusada(r.Saida))
            return ResultadoEtapa.Falha("a senha do banco de dados está incorreta (ou o usuário não existe). " +
                                        "Confira a senha e tente de novo");

        if (conferirSenha && r.NaoIniciou)
            return ResultadoEtapa.Falha($"não foi possível abrir o gfix ({PrimeiraLinha(r.Saida)}). Chame o suporte");

        // D-014: nem conseguiu abrir o banco. Seguir só reiniciaria o Firebird à toa até o backup falhar.
        if (conferirSenha && FalhaDeConexao(r.Saida))
            return ResultadoEtapa.Falha($"não foi possível conectar ao banco de dados (resposta do Firebird: \"{PrimeiraLinha(r.Saida)}\"). " +
                                        "Pode haver outra versão do Firebird neste computador. Chame o suporte");

        // Não é dano no banco: o gfix nem entrou, então esta etapa não foi feita (caso real de 11/11/2026).
        if (NaoPodeValidarAgora(r.Saida))
            return ResultadoEtapa.Aviso("o gfix não conseguiu entrar no banco porque outro programa estava conectado nele; " +
                                        "esta etapa não foi feita");

        // Também não é dano: o Firebird não respondeu (caso real de 11/11/2026, D-026).
        if (PerdeuConexao(r.Saida))
            return ResultadoEtapa.Aviso($"o gfix perdeu a conexão com o Firebird (\"{PrimeiraLinha(r.Saida)}\"); " +
                                        "esta etapa não foi feita");

        // Erros aqui são esperados (são o motivo do reparo). O backup é que decide se dá para seguir.
        return ResultadoEtapa.Aviso($"o gfix encontrou problemas (código {r.CodigoSaida}); o reparo vai continuar");
    }

    /// <summary>
    /// Soma os "Number of ... errors : N" do resumo do gfix ("Summary of validation errors").
    /// Sem resumo, 0.
    /// </summary>
    internal static int ErrosDeValidacao(string saida) =>
        System.Text.RegularExpressions.Regex
            .Matches(saida, @"Number of [^\r\n:]*errors\s*:\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .Sum(m => int.TryParse(m.Groups[1].Value, out var n) ? n : 0);

    /// <summary>Mensagem do Firebird 2.5 para usuário/senha inválidos.</summary>
    internal static bool SenhaRecusada(string saida) =>
        saida.Contains("user name and password are not defined", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Respostas que mostram que o gfix nem chegou a abrir o banco (não são danos no banco).
    /// Lista fechada de propósito: qualquer outra mensagem continua sendo tratada como dano (aviso).
    /// </summary>
    private static readonly string[] SinaisDeFalhaDeConexao =
    {
        "unknown ISC error",                     // ferramenta e servidor/cliente de versões diferentes
        "Error occurred during login",           // Firebird 3+ recusou o login
        "unavailable database",                  // serviço não responde
        "Unable to complete network request",
        "connection rejected",
        "CreateFile (open)",                     // arquivo não abre (não existe ou outro programa prendeu)
        "unsupported on-disk structure",         // banco de outra versão do Firebird
    };

    /// <summary>
    /// gfix -v/-m recusado porque há outras conexões no banco (não diz nada sobre danos). O -m responde
    /// só a primeira linha ("bad parameters on attach..."); o -v também traz a segunda.
    /// </summary>
    internal static bool NaoPodeValidarAgora(string saida) =>
        saida.Contains("secondary server attachments cannot validate", StringComparison.OrdinalIgnoreCase)
        || saida.Contains("bad parameters on attach or create database", StringComparison.OrdinalIgnoreCase);

    /// <summary>A conexão com o Firebird caiu ou ficou sem resposta no meio do comando (não é dano no banco).</summary>
    internal static bool PerdeuConexao(string saida) =>
        saida.Contains("Error reading data from the connection", StringComparison.OrdinalIgnoreCase)
        || saida.Contains("Error writing data to the connection", StringComparison.OrdinalIgnoreCase)
        || saida.Contains("connection shutdown", StringComparison.OrdinalIgnoreCase);

    internal static bool FalhaDeConexao(string saida) =>
        SinaisDeFalhaDeConexao.Any(s => saida.Contains(s, StringComparison.OrdinalIgnoreCase));

    private static string PrimeiraLinha(string saida)
    {
        var linha = saida.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        return linha.Length > 150 ? linha[..150] + "..." : linha;
    }

    // ------------------------------------------------------------------ backup (R10, R11)

    private ResultadoEtapa Backup()
    {
        var r = _executor.Executar("gbak", new[] { "-b", "-v", "-i", "-g", "-l", CaminhoBancoGuardado, CaminhoBackupFbk },
            new OpcoesProcesso { UsarCredenciais = true });

        var tamanho = File.Exists(CaminhoBackupFbk) ? new FileInfo(CaminhoBackupFbk).Length : 0;
        if (r.Sucesso && tamanho > 0)
        {
            _registro.Info($"Backup criado: {CaminhoBackupFbk} ({tamanho} bytes)");
            return ResultadoEtapa.Ok();
        }

        MarcarBackupIncompleto();
        var motivo = r.Sucesso ? "o arquivo de backup ficou vazio" : $"o gbak terminou com erro (código {r.CodigoSaida})";
        // Sem "o banco não foi alterado": o gfix -m pode ter mexido nele. A mensagem final diz que ele voltou.
        return ResultadoEtapa.Falha($"o backup não deu certo: {motivo}");
    }

    private void MarcarBackupIncompleto()
    {
        if (!File.Exists(CaminhoBackupFbk))
            return;

        // Não apaga: renomeia, para ninguém usar por engano um backup incompleto.
        var incompleto = NomeLivre(CaminhoBackupFbk + ".incompleto");
        try
        {
            File.Move(CaminhoBackupFbk, incompleto);
            _registro.Aviso($"Backup incompleto renomeado para: {incompleto}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _registro.Aviso($"Não foi possível renomear o backup incompleto {CaminhoBackupFbk}: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------ troca do banco (R13) e restore (R12)

    /// <summary>Firebird parado: tira o banco do lugar (R11 revisada em D-025). Desfeito por DevolverBancoOriginal.</summary>
    private ResultadoEtapa GuardarBancoOriginal()
    {
        CaminhoBancoGuardado = NomeLivre(CaminhoBancoGuardado);
        File.Move(_cfg.CaminhoBanco, CaminhoBancoGuardado, overwrite: false);
        BancoOriginalForaDoLugar = true;
        _registro.Info($"Banco atual guardado em: {CaminhoBancoGuardado}");
        return ResultadoEtapa.Ok();
    }

    private ResultadoEtapa DevolverBancoOriginal()
    {
        if (!File.Exists(CaminhoBancoGuardado))
            return ResultadoEtapa.Falha($"o banco original não foi encontrado em {CaminhoBancoGuardado}");

        // O Firebird precisa estar parado para mexer nos arquivos do banco.
        var parar = _firebird.Parar();
        if (parar.Status == StatusEtapa.Falha)
            _registro.Aviso("Não foi possível parar o Firebird antes de devolver o banco; tentando mesmo assim.");

        if (File.Exists(_cfg.CaminhoBanco))
        {
            // Só chega aqui com o original a salvo em CaminhoBancoGuardado: o arquivo no lugar é o parcial.
            File.Delete(_cfg.CaminhoBanco);
            _registro.Info($"Banco parcial apagado: {_cfg.CaminhoBanco}");
        }

        File.Move(CaminhoBancoGuardado, _cfg.CaminhoBanco, overwrite: false);
        BancoOriginalForaDoLugar = false;
        _registro.Info($"Banco original devolvido para: {_cfg.CaminhoBanco}");
        return ResultadoEtapa.Ok("banco original devolvido ao lugar");
    }

    /// <summary>
    /// Para erro inesperado que escapa do fluxo (o motor não chega a desfazer): devolve o banco original
    /// se ele ainda estiver fora do lugar e o banco novo não tiver sido conferido. Não lança exceção.
    /// </summary>
    public void DevolverSeInterrompido()
    {
        if (!BancoOriginalForaDoLugar || _bancoNovoConferido)
            return;

        _registro.Aviso("O reparo foi interrompido com o banco fora do lugar. Devolvendo o banco original...");
        try
        {
            var r = DevolverBancoOriginal();
            if (r.Status == StatusEtapa.Falha)
                _registro.Erro("NÃO foi possível devolver o banco original: " + r.Mensagem);
        }
        catch (Exception ex)
        {
            _registro.Erro($"NÃO foi possível devolver o banco original ({CaminhoBancoGuardado}): {ex}");
        }
    }

    private bool _bancoNovoConferido;

    private ResultadoEtapa ConferirBancoNovo()
    {
        var r = ConferirBancoRestaurado();
        _bancoNovoConferido = r.Status != StatusEtapa.Falha;
        return r;
    }

    private ResultadoEtapa Restore()
    {
        var r = _executor.Executar("gbak", new[] { "-c", "-v", "-i", CaminhoBackupFbk, _cfg.CaminhoBanco },
            new OpcoesProcesso { UsarCredenciais = true });

        if (r.Sucesso && File.Exists(_cfg.CaminhoBanco) && new FileInfo(_cfg.CaminhoBanco).Length > 0)
            return ResultadoEtapa.Ok();

        return ResultadoEtapa.Falha($"o restore não deu certo (código {r.CodigoSaida})");
    }

    /// <summary>
    /// R5: valida o banco recém-restaurado. Um restore bom não deve ter erro nenhum; se tiver, é mais
    /// seguro devolver o original (o .fbk continua guardado para o suporte).
    /// </summary>
    private ResultadoEtapa ConferirBancoRestaurado()
    {
        var r = _executor.Executar("gfix", new[] { "-v", "-f", _cfg.CaminhoBanco },
            new OpcoesProcesso { UsarCredenciais = true });

        var erros = ErrosDeValidacao(r.Saida);
        if (r.Sucesso && erros == 0)
            return ResultadoEtapa.Ok("nenhum problema encontrado no banco novo");

        if (r.Sucesso)
            return ResultadoEtapa.Falha($"o banco reconstruído não passou na conferência ({erros} erro(s)). " +
                                        $"A cópia de segurança continua guardada em {CaminhoBackupFbk}");

        // Não é dano no banco: algum programa se conectou nele e o gfix não pôde validar. O restore
        // já terminou com sucesso; devolver o banco antigo por isso seria pior (D-017).
        if (NaoPodeValidarAgora(r.Saida))
            return ResultadoEtapa.Aviso("não foi possível conferir o banco novo porque outro programa estava conectado nele; " +
                                        "o restore terminou sem erros");

        return ResultadoEtapa.Falha($"o banco reconstruído não passou na conferência (código {r.CodigoSaida}). " +
                                    $"A cópia de segurança continua guardada em {CaminhoBackupFbk}");
    }

    // ------------------------------------------------------------------ programas do SysPDV

    private ResultadoEtapa ProgramaComJanela(string ferramenta, string objetivo)
    {
        if (!_cfg.NaoInterativo)
            _interacao.Avisar($"Agora vai abrir o programa {ferramenta} para {objetivo}.\n" +
                              "Siga as instruções na janela dele e feche-a quando terminar. O reparo continua em seguida.");

        var r = _executor.Executar(ferramenta, Array.Empty<string>());
        return r.Sucesso
            ? ResultadoEtapa.Ok()
            : ResultadoEtapa.Falha($"o programa {ferramenta} terminou com erro (código {r.CodigoSaida})");
    }

    private ResultadoEtapa ProgramaSemEsperar(string ferramenta)
    {
        var r = _executor.Executar(ferramenta, Array.Empty<string>(), new OpcoesProcesso { EsperarTerminar = false });
        if (r.NaoIniciou)
            return ResultadoEtapa.Aviso($"não foi possível abrir o {ferramenta}; abra manualmente");
        if (r.AindaRodando || r.CodigoSaida == 0)
            return ResultadoEtapa.Ok();
        return ResultadoEtapa.Aviso($"o {ferramenta} fechou com erro (código {r.CodigoSaida}); abra manualmente");
    }

    // ------------------------------------------------------------------ utilitários

    private static string NomeLivre(string caminho) => Arquivos.NomeLivre(caminho);

    private static bool MesmoArquivo(string a, string b) =>
        !string.IsNullOrEmpty(b)
        && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private static long Mb(long bytes) => bytes / (1024 * 1024);
}
