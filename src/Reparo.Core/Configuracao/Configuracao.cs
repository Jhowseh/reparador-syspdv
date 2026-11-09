namespace Reparo.Core;

/// <summary>
/// Configuração do reparo já resolvida (padrões + reparo.json + variáveis REPARO_*).
/// Criada por <see cref="CarregadorConfiguracao"/>. A senha nunca aparece em ToString/Descrever.
/// </summary>
public sealed class Configuracao
{
    /// <summary>Nomes das ferramentas externas que o app pode chamar (contrato do harness).</summary>
    public static readonly IReadOnlyList<string> NomesFerramentas = new[]
    {
        "gfix", "gbak", "net", "tskill", "taskkill",
        "VERIFICAINDICE", "atualizasyspdv_server", "dbMonitor", "syspdv_server",
    };

    public required string PastaBase { get; init; }

    /// <summary>Caminho completo do banco (.fdb).</summary>
    public required string CaminhoBanco { get; init; }

    /// <summary>Pasta onde o banco antigo é guardado antes do restore (dentro da pasta base).</summary>
    public required string PastaBackupBanco { get; init; }

    /// <summary>Se definida, toda ferramenta é executada como &lt;pasta&gt;\&lt;nome&gt;.cmd (modo teste).</summary>
    public string? PastaFerramentas { get; init; }

    /// <summary>Caminho (ou nome) de cada ferramenta em produção. Chave sem diferenciar maiúsculas.</summary>
    public required IReadOnlyDictionary<string, string> Ferramentas { get; init; }

    public required string Usuario { get; init; }

    /// <summary>Senha do Firebird. Pode faltar: a tela pede ao operador (no modo não interativo é erro).</summary>
    /// <remarks>O modo não interativo só é aceito com <see cref="ModoTeste"/> (D-013).</remarks>
    public string? Senha { get; set; }

    public required string ServicoFirebird { get; init; }

    /// <summary>Processo do servidor Firebird, usado só se o serviço não parar no tempo limite.</summary>
    public required string ProcessoFirebird { get; init; }

    public required string PastaLog { get; init; }

    public required int TempoLimitePararServicoSegundos { get; init; }

    public required bool NaoInterativo { get; init; }

    /// <summary>
    /// Verdadeiro = antes do reparo, coloca os programas embutidos (gfix, gbak, fbclient, firebird.msg,
    /// VerificaIndice, dbMonitor) na pasta do SysPDV (D-015, D-016). Nunca no modo teste.
    /// </summary>
    public bool CopiarProgramas { get; init; }

    /// <summary>Verdadeiro quando as ferramentas vêm de REPARO_FERRAMENTAS_DIR (harness).</summary>
    public bool ModoTeste => PastaFerramentas is not null;

    public bool TemSenha => !string.IsNullOrEmpty(Senha);

    /// <summary>
    /// Confere a configuração. Devolve a lista de problemas em linguagem simples (vazia = ok).
    /// </summary>
    public IReadOnlyList<string> Validar()
    {
        var problemas = new List<string>();

        // R2 (D-013): sem ninguém na tela, o VERIFICAINDICE e a atualização (que abrem janela e
        // esperam alguém fechar) travariam o reparo para sempre. Sem janela, só com o harness.
        if (NaoInterativo && !ModoTeste)
            problemas.Add("O modo sem janela (--nao-interativo) só é usado nos testes do programa. " +
                          "Para reparar o banco, abra o ReparoSysPDV.exe com dois cliques.");

        if (string.IsNullOrWhiteSpace(Usuario))
            problemas.Add("O usuário do banco de dados não foi informado.");

        if (!TemSenha && NaoInterativo)
            problemas.Add("A senha do banco de dados não foi informada (REPARO_DB_SENHA ou \"senha\" no reparo.json).");

        if (string.IsNullOrWhiteSpace(ServicoFirebird))
            problemas.Add("O nome do serviço do Firebird não foi informado.");

        if (string.IsNullOrWhiteSpace(ProcessoFirebird))
            problemas.Add("O nome do processo do Firebird não foi informado.");

        if (TempoLimitePararServicoSegundos <= 0)
            problemas.Add("O tempo limite para parar o serviço precisa ser maior que zero.");

        if (PastaPerigosa(PastaBase))
            problemas.Add($"A pasta {PastaBase} não pode ser usada: o banco precisa estar na pasta do SysPDV " +
                          "(não na raiz do disco nem em pastas do Windows).");

        if (!EstaDentro(PastaBackupBanco, PastaBase) || Igual(PastaBackupBanco, PastaBase))
            problemas.Add("A pasta de backup do banco precisa ficar dentro da pasta do SysPDV.");

        if (Igual(CaminhoBanco, PastaBase) || !Path.GetFileName(CaminhoBanco).Contains('.'))
            problemas.Add("O caminho do banco de dados parece inválido.");

        if (CaminhoBanco.IndexOfAny(new[] { '*', '?' }) >= 0)
            problemas.Add("O caminho do banco de dados não pode ter * ou ?.");

        return problemas;
    }

    /// <summary>Linhas para o log descrevendo a configuração, com a senha mascarada.</summary>
    public IEnumerable<string> Descrever()
    {
        yield return $"Pasta do SysPDV: {PastaBase}";
        yield return $"Banco de dados: {CaminhoBanco}";
        yield return $"Pasta de backup do banco: {PastaBackupBanco}";
        yield return $"Pasta de logs: {PastaLog}";
        yield return $"Usuário do banco: {Usuario}";
        yield return $"Senha do banco: {(TemSenha ? "******** (informada)" : "(não informada)")}";
        yield return $"Serviço do Firebird: {ServicoFirebird}";
        yield return $"Processo do Firebird: {ProcessoFirebird}";
        yield return $"Tempo limite para parar o serviço: {TempoLimitePararServicoSegundos} s";
        if (ModoTeste)
        {
            yield return $"Modo teste: ferramentas em {PastaFerramentas}";
        }
        else
        {
            yield return "Modo normal: ferramentas reais";
            yield return CopiarProgramas
                ? $"Programas do reparo: os do reparador (Firebird {ProgramasDoReparo.VersaoFirebird}, VerificaIndice, dbMonitor), " +
                  "copiados para a pasta do SysPDV"
                : "Programas do reparo: os já instalados (cópia desligada no reparo.json)";
            yield return $"gfix: {Ferramentas["gfix"]}";
            yield return $"gbak: {Ferramentas["gbak"]}";
        }
        yield return $"Modo não interativo: {(NaoInterativo ? "sim" : "não")}";
    }

    public override string ToString() => string.Join(Environment.NewLine, Descrever());

    /// <summary>
    /// Pastas onde a limpeza (srv*, *log, *txt...) faria estrago: raiz de disco, pasta do Windows,
    /// Arquivos de Programas, pasta do usuário e Área de Trabalho/Documentos/Downloads.
    /// </summary>
    public static bool PastaPerigosa(string pasta)
    {
        var p = Path.TrimEndingDirectorySeparator(pasta);
        var raiz = Path.GetPathRoot(pasta);
        if (raiz is not null && Igual(p, raiz))
            return true;

        var especiais = new[]
        {
            Environment.SpecialFolder.Windows, Environment.SpecialFolder.System, Environment.SpecialFolder.SystemX86,
            Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86,
            Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.Desktop,
            Environment.SpecialFolder.CommonDesktopDirectory, Environment.SpecialFolder.MyDocuments,
            Environment.SpecialFolder.CommonDocuments,
        };
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrEmpty(windows) && EstaDentro(p, windows))
            return true; // qualquer pasta dentro de C:\Windows

        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        return especiais.Select(Environment.GetFolderPath).Append(downloads)
            .Where(e => !string.IsNullOrEmpty(e))
            .Any(e => Igual(p, e));
    }

    internal static bool EstaDentro(string caminho, string pasta)
    {
        var pastaComBarra = Path.TrimEndingDirectorySeparator(pasta) + Path.DirectorySeparatorChar;
        return caminho.StartsWith(pastaComBarra, StringComparison.OrdinalIgnoreCase) || Igual(caminho, pasta);
    }

    private static bool Igual(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b),
            StringComparison.OrdinalIgnoreCase);
}
