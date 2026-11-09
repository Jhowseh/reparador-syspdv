using System.Text.Json;

namespace Reparo.Core;

/// <summary>Erro ao ler a configuração. A mensagem é para o operador e nunca contém a senha.</summary>
public sealed class ConfiguracaoInvalidaException : Exception
{
    public ConfiguracaoInvalidaException(string mensagem) : base(mensagem) { }
}

/// <summary>
/// Monta a <see cref="Configuracao"/>. Prioridade: variáveis REPARO_* &gt; reparo.json &gt; padrões.
/// </summary>
public static class CarregadorConfiguracao
{
    public const string NomeArquivo = "reparo.json";

    // Padrões de produção (podem ser trocados no reparo.json ou nas variáveis).
    public const string PadraoPastaBase = @"C:\SYSPDV";
    public const string PadraoBanco = "syspdv_srv.fdb";
    public const string PadraoPastaBackupBanco = "bkpBd";
    public const string PadraoUsuario = "SYSDBA";
    public const string PadraoServico = "FirebirdServerDefaultInstance";
    public const string PadraoProcesso = "fbserver.exe";
    public const string PadraoSubpastaLog = "logs-reparo";
    public const int PadraoTempoLimitePararServico = 60;

    /// <summary>Modelo do reparo.json. Todos os campos são opcionais.</summary>
    public sealed class ArquivoConfiguracao
    {
        public string? PastaBase { get; set; }
        public string? Banco { get; set; }
        public string? PastaBackupBanco { get; set; }
        public string? PastaFerramentas { get; set; }
        public Dictionary<string, string>? Ferramentas { get; set; }
        public string? Usuario { get; set; }
        public string? Senha { get; set; }
        public string? ServicoFirebird { get; set; }
        public string? ProcessoFirebird { get; set; }
        public string? PastaLog { get; set; }
        public int? TempoLimitePararServicoSegundos { get; set; }

        /// <summary>Falso = não coloca os programas embutidos na pasta do SysPDV (D-015, D-016).</summary>
        public bool? CopiarProgramas { get; set; }
    }

    /// <summary>
    /// Carrega a configuração do processo atual: reparo.json ao lado do .exe (opcional) + ambiente
    /// + Firebird detectado no Windows.
    /// </summary>
    public static Configuracao CarregarDoAmbiente(IEnumerable<string> argumentos, string? bancoEscolhido = null)
    {
        var caminhoArquivo = Path.Combine(AppContext.BaseDirectory, NomeArquivo);
        return Carregar(caminhoArquivo, Environment.GetEnvironmentVariable, argumentos,
            bancoEscolhido, new DetectorFirebirdWindows());
    }

    /// <param name="caminhoArquivo">Caminho do reparo.json; se não existir, só padrões e variáveis.</param>
    /// <param name="lerVariavel">Lê uma variável de ambiente (injetável para testes).</param>
    /// <param name="bancoEscolhido">
    /// Banco escolhido pelo operador na tela. Se informado, a pasta base passa a ser a pasta dele.
    /// </param>
    /// <param name="detector">Descobre o Firebird instalado (não é usado no modo teste).</param>
    public static Configuracao Carregar(
        string? caminhoArquivo, Func<string, string?> lerVariavel, IEnumerable<string> argumentos,
        string? bancoEscolhido = null, IDetectorFirebird? detector = null)
    {
        var arquivo = LerArquivo(caminhoArquivo);

        string? Var(string nome)
        {
            var valor = lerVariavel(nome);
            return string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
        }

        static string? Txt(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

        string pastaBase;
        string caminhoBanco;
        if (Txt(bancoEscolhido) is { } escolhido)
        {
            caminhoBanco = CaminhoCompleto(escolhido);
            pastaBase = Path.GetDirectoryName(caminhoBanco) ?? caminhoBanco;
        }
        else
        {
            pastaBase = CaminhoCompleto(Var("REPARO_BASE_DIR") ?? Txt(arquivo.PastaBase) ?? PadraoPastaBase);
            var banco = Txt(arquivo.Banco) ?? PadraoBanco;
            caminhoBanco = CaminhoCompleto(Path.IsPathRooted(banco) ? banco : Path.Combine(pastaBase, banco));
        }

        var backup = Txt(arquivo.PastaBackupBanco) ?? PadraoPastaBackupBanco;
        var pastaBackup = CaminhoCompleto(Path.IsPathRooted(backup) ? backup : Path.Combine(pastaBase, backup));

        var pastaFerramentas = Var("REPARO_FERRAMENTAS_DIR") ?? Txt(arquivo.PastaFerramentas);
        if (pastaFerramentas is not null)
            pastaFerramentas = CaminhoCompleto(pastaFerramentas);

        var pastaLog = CaminhoCompleto(
            Var("REPARO_LOG_DIR") ?? Txt(arquivo.PastaLog) ?? Path.Combine(pastaBase, PadraoSubpastaLog));

        // A senha não passa por Trim: espaços podem fazer parte dela.
        var senhaVar = lerVariavel("REPARO_DB_SENHA");
        var senha = !string.IsNullOrEmpty(senhaVar) ? senhaVar
                  : !string.IsNullOrEmpty(arquivo.Senha) ? arquivo.Senha
                  : null;

        var servicoConfigurado = Var("REPARO_SERVICO_FIREBIRD") ?? Txt(arquivo.ServicoFirebird);

        // Fora do modo teste, descobre o Firebird instalado (só o que não foi configurado à mão).
        var detectado = pastaFerramentas is null && detector is not null
            ? detector.Detectar(servicoConfigurado ?? PadraoServico)
            : InstalacaoFirebird.NaoEncontrada;

        // D-015/D-016: fora do harness, os programas embutidos vão para a pasta do SysPDV e são usados de lá.
        var copiarProgramas = pastaFerramentas is null && (arquivo.CopiarProgramas ?? true);

        return new Configuracao
        {
            PastaBase = pastaBase,
            CaminhoBanco = caminhoBanco,
            PastaBackupBanco = pastaBackup,
            PastaFerramentas = pastaFerramentas,
            Ferramentas = MontarFerramentas(arquivo.Ferramentas, detectado.PastaBin, copiarProgramas ? pastaBase : null),
            CopiarProgramas = copiarProgramas,
            Usuario = Var("REPARO_DB_USUARIO") ?? Txt(arquivo.Usuario) ?? PadraoUsuario,
            Senha = senha,
            ServicoFirebird = servicoConfigurado ?? detectado.Servico ?? PadraoServico,
            ProcessoFirebird = Txt(arquivo.ProcessoFirebird) ?? detectado.Processo ?? PadraoProcesso,
            PastaLog = pastaLog,
            TempoLimitePararServicoSegundos = arquivo.TempoLimitePararServicoSegundos ?? PadraoTempoLimitePararServico,
            NaoInterativo = ModoExecucao.EhNaoInterativo(argumentos, lerVariavel(ModoExecucao.VariavelNaoInterativo)),
        };
    }

    private static ArquivoConfiguracao LerArquivo(string? caminho)
    {
        if (caminho is null || !File.Exists(caminho))
            return new ArquivoConfiguracao();

        try
        {
            var opcoes = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            };
            return JsonSerializer.Deserialize<ArquivoConfiguracao>(File.ReadAllText(caminho), opcoes)
                   ?? new ArquivoConfiguracao();
        }
        catch (JsonException ex)
        {
            // Não repete o conteúdo do arquivo (pode ter a senha); só a posição do erro.
            throw new ConfiguracaoInvalidaException(
                $"O arquivo de configuração {NomeArquivo} está com erro de digitação (linha {ex.LineNumber + 1}).");
        }
        catch (IOException)
        {
            throw new ConfiguracaoInvalidaException($"Não foi possível ler o arquivo de configuração {NomeArquivo}.");
        }
        catch (UnauthorizedAccessException)
        {
            throw new ConfiguracaoInvalidaException($"Sem permissão para ler o arquivo de configuração {NomeArquivo}.");
        }
    }

    /// <param name="pastaProgramasCopiados">
    /// Pasta do SysPDV quando os programas embutidos são copiados para lá (vence a detecção; o
    /// reparo.json ainda vence tudo).
    /// </param>
    private static IReadOnlyDictionary<string, string> MontarFerramentas(
        Dictionary<string, string>? doArquivo, string? pastaBinFirebird, string? pastaProgramasCopiados)
    {
        var ferramentas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var nome in Configuracao.NomesFerramentas)
            ferramentas[nome] = nome + ".exe"; // procurado na pasta base e no PATH

        // gfix/gbak da instalação do Firebird detectada.
        if (pastaBinFirebird is not null)
        {
            foreach (var nome in new[] { "gfix", "gbak" })
            {
                var caminho = Path.Combine(pastaBinFirebird, nome + ".exe");
                if (File.Exists(caminho))
                    ferramentas[nome] = caminho;
            }
        }

        // Programas embutidos, rodando da pasta do SysPDV (usam o fbclient.dll que fica ao lado).
        if (pastaProgramasCopiados is not null)
        {
            ferramentas["gfix"] = Path.Combine(pastaProgramasCopiados, "gfix.exe");
            ferramentas["gbak"] = Path.Combine(pastaProgramasCopiados, "gbak.exe");
            ferramentas["VERIFICAINDICE"] = Path.Combine(pastaProgramasCopiados, "VerificaIndice.exe");
            ferramentas["dbMonitor"] = Path.Combine(pastaProgramasCopiados, "dbMonitor.exe");
        }

        if (doArquivo is not null)
        {
            foreach (var (nome, caminho) in doArquivo)
            {
                if (!string.IsNullOrWhiteSpace(caminho))
                    ferramentas[nome] = caminho.Trim();
            }
        }

        return ferramentas;
    }

    private static string CaminhoCompleto(string caminho)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(caminho));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ConfiguracaoInvalidaException($"Caminho inválido na configuração: {caminho}");
        }
    }
}
