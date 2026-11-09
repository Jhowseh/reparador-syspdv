using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Reparo.Core;

/// <summary>Resultado de uma chamada a programa externo. A saída já vem com a senha mascarada.</summary>
public sealed record ResultadoProcesso(
    int CodigoSaida,
    string Saida,
    TimeSpan Duracao,
    bool TempoEsgotado = false,
    bool AindaRodando = false,
    bool NaoIniciou = false)
{
    /// <summary>Terminou sozinho, dentro do tempo, com código 0.</summary>
    public bool Sucesso => !TempoEsgotado && !NaoIniciou && !AindaRodando && CodigoSaida == 0;
}

public sealed class OpcoesProcesso
{
    /// <summary>Passa usuário e senha do Firebird por ISC_USER/ISC_PASSWORD (nunca na linha de comando).</summary>
    public bool UsarCredenciais { get; init; }

    /// <summary>Tempo máximo de espera. Nulo = espera o quanto for preciso.</summary>
    public TimeSpan? TempoLimite { get; init; }

    /// <summary>
    /// Falso = abre o programa e não espera ele fechar (dbMonitor, syspdv_server). Mesmo assim observa
    /// por <see cref="TempoObservacao"/> para saber se ele fechou logo com erro.
    /// </summary>
    public bool EsperarTerminar { get; init; } = true;

    public TimeSpan TempoObservacao { get; init; } = TimeSpan.FromSeconds(10);
}

/// <summary>Único ponto do app que executa programas externos (contrato do harness, AGENTS.md seção 4).</summary>
public interface IExecutorProcessos
{
    /// <summary>Caminho do programa que seria executado, ou nulo se não foi encontrado.</summary>
    string? Localizar(string ferramenta);

    ResultadoProcesso Executar(string ferramenta, IReadOnlyList<string> argumentos, OpcoesProcesso? opcoes = null);
}

public sealed class ExecutorProcessos : IExecutorProcessos
{
    private static readonly char[] CaracteresQuePedemAspas = { ' ', '\t', '&', '|', '<', '>', '^', '(', ')', ',', ';', '=', '%', '!' };

    /// <summary>Programas do próprio Windows: procurados só na pasta do sistema.</summary>
    private static readonly HashSet<string> FerramentasDoWindows = new(StringComparer.OrdinalIgnoreCase) { "net", "taskkill", "tskill" };

    private readonly Configuracao _configuracao;
    private readonly IRegistro _registro;
    private readonly MascaradorSenha _mascarador;
    private readonly Func<string, string?> _lerVariavel;

    static ExecutorProcessos()
    {
        // Para ler a saída do gfix/gbak na página de código do console (OEM, ex.: 850).
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public ExecutorProcessos(Configuracao configuracao, IRegistro registro, MascaradorSenha mascarador,
        Func<string, string?>? lerVariavel = null)
    {
        _configuracao = configuracao;
        _registro = registro;
        _mascarador = mascarador;
        _lerVariavel = lerVariavel ?? Environment.GetEnvironmentVariable;
    }

    public string? Localizar(string ferramenta)
    {
        if (_configuracao.PastaFerramentas is { } pastaTeste)
        {
            // Modo teste: SEMPRE <pasta>\<nome>.cmd. Nunca cai para a ferramenta real.
            var caminhoTeste = Path.Combine(pastaTeste, ferramenta + ".cmd");
            return File.Exists(caminhoTeste) ? caminhoTeste : null;
        }

        var configurado = _configuracao.Ferramentas.TryGetValue(ferramenta, out var c) ? c : ferramenta + ".exe";
        if (Path.IsPathRooted(configurado))
            return File.Exists(configurado) ? configurado : null;

        // Ferramentas do Windows: só da pasta do sistema (um "net.exe" deixado na pasta do SysPDV
        // nunca é executado como administrador).
        if (FerramentasDoWindows.Contains(ferramenta))
        {
            var doSistema = Path.Combine(Environment.SystemDirectory, configurado);
            return File.Exists(doSistema) ? doSistema : null;
        }

        var naPastaBase = Path.Combine(_configuracao.PastaBase, configurado);
        if (File.Exists(naPastaBase))
            return naPastaBase;

        foreach (var pasta in (_lerVariavel("PATH") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidato = Path.Combine(pasta.Trim().Trim('"'), configurado);
                if (File.Exists(candidato))
                    return candidato;
            }
            catch (ArgumentException)
            {
                // Entrada inválida no PATH: ignora.
            }
        }

        return null;
    }

    public ResultadoProcesso Executar(string ferramenta, IReadOnlyList<string> argumentos, OpcoesProcesso? opcoes = null)
    {
        opcoes ??= new OpcoesProcesso();
        var textoComando = $"{ferramenta} {string.Join(" ", argumentos.Select(Aspas))}".TrimEnd();
        _registro.Info(_mascarador.Mascarar($"Executando: {textoComando}"));

        var caminho = Localizar(ferramenta);
        if (caminho is null)
        {
            var msg = $"Programa \"{ferramenta}\" não encontrado.";
            _registro.Erro(msg);
            return new ResultadoProcesso(-1, msg, TimeSpan.Zero, NaoIniciou: true);
        }

        var inicio = Stopwatch.StartNew();
        var saida = new StringBuilder();
        var travaSaida = new object();

        using var processo = new Process { StartInfo = MontarInicio(caminho, argumentos, opcoes) };
        if (opcoes.EsperarTerminar)
        {
            DataReceivedEventHandler ao = (_, e) =>
            {
                if (e.Data is null) return;
                lock (travaSaida) saida.AppendLine(e.Data);
                // Mascara aqui também (além do RegistroLog): nenhuma linha sai do executor com a senha.
                _registro.SaidaFerramenta(ferramenta, _mascarador.Mascarar(e.Data));
            };
            processo.OutputDataReceived += ao;
            processo.ErrorDataReceived += ao;
        }

        try
        {
            processo.Start();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            var msg = _mascarador.Mascarar($"Não foi possível iniciar \"{ferramenta}\": {ex.Message}");
            _registro.Erro(msg);
            return new ResultadoProcesso(-1, msg, inicio.Elapsed, NaoIniciou: true);
        }

        if (!opcoes.EsperarTerminar)
            return ObservarSemEsperar(ferramenta, processo, opcoes, inicio);

        processo.StandardInput.Close(); // nenhuma ferramenta pode ficar esperando teclado
        processo.BeginOutputReadLine();
        processo.BeginErrorReadLine();

        var limiteMs = opcoes.TempoLimite is { } t ? (int)Math.Min(t.TotalMilliseconds, int.MaxValue) : Timeout.Infinite;
        if (!processo.WaitForExit(limiteMs))
        {
            TentarEncerrar(processo);
            _registro.Erro($"{ferramenta} não terminou em {opcoes.TempoLimite!.Value.TotalSeconds:0} s e foi interrompido.");
            return new ResultadoProcesso(-1, Texto(saida, travaSaida), inicio.Elapsed, TempoEsgotado: true);
        }

        processo.WaitForExit(); // garante que toda a saída foi lida
        var codigo = processo.ExitCode;
        _registro.Info($"{ferramenta} terminou com código {codigo} em {inicio.Elapsed.TotalSeconds:0.0} s.");
        return new ResultadoProcesso(codigo, Texto(saida, travaSaida), inicio.Elapsed);
    }

    private ResultadoProcesso ObservarSemEsperar(string ferramenta, Process processo, OpcoesProcesso opcoes, Stopwatch inicio)
    {
        if (processo.WaitForExit((int)opcoes.TempoObservacao.TotalMilliseconds))
        {
            var codigo = processo.ExitCode;
            _registro.Info($"{ferramenta} foi aberto e já fechou, com código {codigo}.");
            return new ResultadoProcesso(codigo, string.Empty, inicio.Elapsed);
        }

        _registro.Info($"{ferramenta} foi aberto e continua rodando.");
        return new ResultadoProcesso(0, string.Empty, inicio.Elapsed, AindaRodando: true);
    }

    private ProcessStartInfo MontarInicio(string caminho, IReadOnlyList<string> argumentos, OpcoesProcesso opcoes)
    {
        var inicio = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Directory.Exists(_configuracao.PastaBase) ? _configuracao.PastaBase : Environment.CurrentDirectory,
        };

        var extensao = Path.GetExtension(caminho);
        if (extensao.Equals(".cmd", StringComparison.OrdinalIgnoreCase) || extensao.Equals(".bat", StringComparison.OrdinalIgnoreCase))
        {
            // /s: o cmd tira só a primeira e a última aspas da linha.
            inicio.FileName = _lerVariavel("ComSpec") ?? "cmd.exe";
            inicio.Arguments = "/d /s /c \"\"" + caminho + "\" " + string.Join(" ", argumentos.Select(AspasCmd)) + "\"";
        }
        else
        {
            inicio.FileName = caminho;
            foreach (var argumento in argumentos)
                inicio.ArgumentList.Add(argumento);
        }

        if (opcoes.EsperarTerminar)
        {
            var oem = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
            inicio.RedirectStandardInput = true;
            inicio.RedirectStandardOutput = true;
            inicio.RedirectStandardError = true;
            inicio.StandardOutputEncoding = oem;
            inicio.StandardErrorEncoding = oem;
        }

        // Credenciais só para quem precisa; nunca herdadas por outros programas.
        inicio.Environment.Remove("ISC_USER");
        inicio.Environment.Remove("ISC_PASSWORD");
        if (opcoes.UsarCredenciais)
        {
            inicio.Environment["ISC_USER"] = _configuracao.Usuario;
            inicio.Environment["ISC_PASSWORD"] = _configuracao.Senha ?? string.Empty;
        }

        return inicio;
    }

    private string Texto(StringBuilder saida, object trava)
    {
        lock (trava) return _mascarador.Mascarar(saida.ToString());
    }

    private static void TentarEncerrar(Process processo)
    {
        try
        {
            processo.Kill(entireProcessTree: true);
            processo.WaitForExit(5000);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Já terminou.
        }
    }

    /// <summary>Aspas para mostrar no log.</summary>
    private static string Aspas(string argumento) =>
        argumento.Length == 0 || argumento.IndexOfAny(CaracteresQuePedemAspas) >= 0 ? $"\"{argumento}\"" : argumento;

    /// <summary>Aspas para a linha do cmd.exe (usado só com .cmd/.bat, isto é, no modo teste).</summary>
    internal static string AspasCmd(string argumento)
    {
        if (argumento.Contains('"'))
            throw new ArgumentException("Argumento com aspas não é suportado ao chamar .cmd.", nameof(argumento));
        return Aspas(argumento);
    }
}
