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
        new() { Descricao = "Ligar o Firebird", Executar = _firebird.Ligar },
        // Crítica só por causa da senha: erros de validação viram aviso; senha errada para tudo.
        new() { Descricao = "Verificar o banco de dados (1 de 3)", Executar = PrimeiraVerificacao },
        new() { Descricao = "Reiniciar o Firebird", Executar = _firebird.Reiniciar },
        new() { Descricao = "Consertar o banco de dados (2 de 3)", Executar = () => Gfix("-m", "-f", "-i"), Critica = false },
        new() { Descricao = "Reiniciar o Firebird", Executar = _firebird.Reiniciar },
        new() { Descricao = "Verificar o banco de dados (3 de 3)", Executar = () => Gfix("-v", "-f"), Critica = false },
        new() { Descricao = "Reiniciar o Firebird", Executar = _firebird.Reiniciar },
        new() { Descricao = "Fazer a cópia de segurança (backup)", Executar = Backup },
        new() { Descricao = "Parar o Firebird", Executar = _firebird.Parar },
        new() { Descricao = "Guardar o banco atual na pasta de backup", Executar = GuardarBancoOriginal, Desfazer = DevolverBancoOriginal },
        new() { Descricao = "Ligar o Firebird", Executar = _firebird.Ligar },
        new() { Descricao = "Reconstruir o banco a partir do backup (restore)", Executar = Restore },
        // O gfix -v só valida sem outras conexões abertas (Firebird 2.5): reinicia antes, como nas outras verificações.
        new() { Descricao = "Reiniciar o Firebird", Executar = _firebird.Reiniciar },
        // R5: só depois de conferido o banco novo vira definitivo; se falhar, o original volta ao lugar.
        new() { Descricao = "Conferir o banco reconstruído", Executar = ConferirBancoRestaurado, PontoSemVolta = true },
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
            return ResultadoEtapa.Falha($"o banco de dados não foi encontrado em {_cfg.CaminhoBanco}");

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

    private ResultadoEtapa Gfix(bool conferirSenha, params string[] opcoes)
    {
        var r = _executor.Executar("gfix", opcoes.Append(_cfg.CaminhoBanco).ToArray(),
            new OpcoesProcesso { UsarCredenciais = true });

        if (r.Sucesso)
            return ResultadoEtapa.Ok("nenhum problema encontrado");

        if (conferirSenha && SenhaRecusada(r.Saida))
            return ResultadoEtapa.Falha("a senha do banco de dados está incorreta (ou o usuário não existe). " +
                                        "Confira a senha e tente de novo");

        if (conferirSenha && r.NaoIniciou)
            return ResultadoEtapa.Falha($"não foi possível abrir o gfix ({PrimeiraLinha(r.Saida)}). Chame o suporte");

        // D-014: nem conseguiu abrir o banco. Seguir só reiniciaria o Firebird à toa até o backup falhar.
        if (conferirSenha && FalhaDeConexao(r.Saida))
            return ResultadoEtapa.Falha($"não foi possível conectar ao banco de dados (resposta do Firebird: \"{PrimeiraLinha(r.Saida)}\"). " +
                                        "Pode haver outra versão do Firebird neste computador. Chame o suporte");

        // Erros aqui são esperados (são o motivo do reparo). O backup é que decide se dá para seguir.
        return ResultadoEtapa.Aviso($"o gfix encontrou problemas (código {r.CodigoSaida}); o reparo vai continuar");
    }

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

    /// <summary>gfix -v recusado porque há outras conexões no banco (não diz nada sobre danos).</summary>
    internal static bool NaoPodeValidarAgora(string saida) =>
        saida.Contains("secondary server attachments cannot validate", StringComparison.OrdinalIgnoreCase);

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
        var r = _executor.Executar("gbak", new[] { "-b", "-v", "-i", "-g", "-l", _cfg.CaminhoBanco, CaminhoBackupFbk },
            new OpcoesProcesso { UsarCredenciais = true });

        var tamanho = File.Exists(CaminhoBackupFbk) ? new FileInfo(CaminhoBackupFbk).Length : 0;
        if (r.Sucesso && tamanho > 0)
        {
            _registro.Info($"Backup criado: {CaminhoBackupFbk} ({tamanho} bytes)");
            return ResultadoEtapa.Ok();
        }

        MarcarBackupIncompleto();
        var motivo = r.Sucesso ? "o arquivo de backup ficou vazio" : $"o gbak terminou com erro (código {r.CodigoSaida})";
        return ResultadoEtapa.Falha($"o backup não deu certo: {motivo}. O banco de dados não foi alterado");
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

    private ResultadoEtapa GuardarBancoOriginal()
    {
        // Confere de novo: só tira o banco do lugar com um backup bom (R11).
        if (!File.Exists(CaminhoBackupFbk) || new FileInfo(CaminhoBackupFbk).Length == 0)
            return ResultadoEtapa.Falha("o arquivo de backup não existe ou está vazio");

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

        if (r.Sucesso)
            return ResultadoEtapa.Ok("nenhum problema encontrado no banco novo");

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
