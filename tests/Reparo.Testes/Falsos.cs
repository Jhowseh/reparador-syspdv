using Reparo.Core;

namespace Reparo.Testes;

internal sealed class InteracaoFalsa : IInteracao
{
    public bool Resposta { get; set; } = true;
    public List<string> Avisos { get; } = new();
    public List<string> Perguntas { get; } = new();
    public List<string> Progresso { get; } = new();

    public bool Confirmar(string mensagem)
    {
        Perguntas.Add(mensagem);
        return Resposta;
    }

    public void Avisar(string mensagem) => Avisos.Add(mensagem);
    /// <summary>Se definido, lança erro ao começar esta etapa (erro fora das etapas, ex.: tela caiu).</summary>
    public int? ExplodirNaEtapa { get; set; }

    public void MostrarProgresso(int etapaAtual, int totalEtapas, string descricao)
    {
        if (etapaAtual == ExplodirNaEtapa)
            throw new InvalidOperationException("falha simulada na interação");
        Progresso.Add($"{etapaAtual}/{totalEtapas} {descricao}");
    }
    public List<(int Etapa, StatusEtapa Status)> Terminadas { get; } = new();
    public void EtapaTerminou(int etapaAtual, StatusEtapa status) => Terminadas.Add((etapaAtual, status));
    public bool? Sucesso { get; private set; }

    public void MostrarResultado(bool sucesso, string mensagem)
    {
        Sucesso = sucesso;
        Avisos.Add(mensagem); // os testes leem a mensagem final em Avisos.Last()
    }
}

internal sealed class RegistroFalso : IRegistro
{
    public List<string> Linhas { get; } = new();
    public string CaminhoArquivo { get; set; } = string.Empty;
    public void Info(string mensagem) => Linhas.Add("INFO " + mensagem);
    public void Aviso(string mensagem) => Linhas.Add("AVISO " + mensagem);
    public void Erro(string mensagem) => Linhas.Add("ERRO " + mensagem);
    public void SaidaFerramenta(string ferramenta, string linha) => Linhas.Add($"SAIDA {ferramenta}> {linha}");
    public string Tudo => string.Join("\n", Linhas);
}

internal sealed class AmbienteFalso : IAmbienteSistema
{
    public bool Administrador { get; set; } = true;
    public long? EspacoLivre { get; set; } = long.MaxValue;
    public bool RuntimeVc80 { get; set; } = true;
    public bool EhAdministrador() => Administrador;
    public long? EspacoLivreBytes(string pasta) => EspacoLivre;
    public bool TemRuntimeVisualC2005x86(string pastaPrograma) => RuntimeVc80;
}

/// <summary>Uma chamada feita ao simulador.</summary>
internal sealed record Chamada(string Ferramenta, string[] Argumentos, OpcoesProcesso Opcoes, int Codigo)
{
    public string Modo => Ferramenta switch
    {
        "net" => Argumentos[0],
        "gbak" when Argumentos.Contains("-b") => "backup",
        "gbak" when Argumentos.Contains("-c") => "restore",
        "gfix" when Argumentos.Contains("-m") => "mend",
        "gfix" => "validar",
        _ => "executar",
    };

    public override string ToString() => $"{Ferramenta} {Modo} ({Codigo})";
}

/// <summary>
/// Simula gfix, gbak, net e os programas do SysPDV em memória e em arquivos de uma pasta temporária,
/// imitando o simulador do harness (banco = arquivo de texto).
/// </summary>
internal sealed class SimuladorFerramentas : IExecutorProcessos
{
    public bool ServicoLigado { get; set; } = true;
    public List<Chamada> Chamadas { get; } = new();

    /// <summary>"ferramenta:modo" que devem falhar (ex.: "gbak:backup", "VERIFICAINDICE:executar", "net:stop").</summary>
    public HashSet<string> Falhas { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> Ausentes { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Simula o Firebird recusando usuário/senha em gfix e gbak.</summary>
    public bool SenhaErrada { get; set; }

    /// <summary>Se definido, gfix e gbak não conseguem conectar e respondem com esta mensagem.</summary>
    public string? ErroConexao { get; set; }

    /// <summary>Simula o gfix -v encontrando erros no banco recém-restaurado.</summary>
    public bool BancoRestauradoComErros { get; set; }

    /// <summary>Simula outro programa conectado no banco novo: o gfix -v se recusa a validar.</summary>
    public bool RestauradoComOutraConexao { get; set; }

    public string? Localizar(string ferramenta) => Ausentes.Contains(ferramenta) ? null : "simulado:" + ferramenta;

    public ResultadoProcesso Executar(string ferramenta, IReadOnlyList<string> argumentos, OpcoesProcesso? opcoes = null)
    {
        opcoes ??= new OpcoesProcesso();
        var args = argumentos.ToArray();
        var provisoria = new Chamada(ferramenta, args, opcoes, 0);
        var (codigo, saida) = Simular(provisoria);
        Chamadas.Add(provisoria with { Codigo = codigo });
        if (Ausentes.Contains(ferramenta))
            return new ResultadoProcesso(-1, "não encontrado", TimeSpan.Zero, NaoIniciou: true);
        return new ResultadoProcesso(codigo, saida, TimeSpan.Zero);
    }

    private bool DeveFalhar(Chamada c) => Falhas.Contains($"{c.Ferramenta}:{c.Modo}");

    private (int, string) Simular(Chamada c)
    {
        switch (c.Ferramenta)
        {
            case "net":
                if (DeveFalhar(c)) return (2, "System error 1051");
                if (c.Modo == "stop")
                {
                    if (!ServicoLigado) return (2, "The service is not started.\nMore help is available by typing NET HELPMSG 3521.");
                    ServicoLigado = false;
                    return (0, "stopped");
                }
                if (ServicoLigado) return (2, "The requested service has already been started.\nNET HELPMSG 2182.");
                ServicoLigado = true;
                return (0, "started");

            case "taskkill":
                ServicoLigado = false;
                return (0, "");

            case "gfix" or "gbak" when SenhaErrada:
                return (1, "Your user name and password are not defined. Ask your database administrator to set up a Firebird login.");

            case "gfix" or "gbak" when ErroConexao is not null:
                return (1, ErroConexao);

            case "gfix":
            {
                if (!ServicoLigado) return (1, "unavailable database");
                // "gfix:validar"/"gfix:mend" simulam o banco ORIGINAL corrompido; o restaurado só
                // falha com BancoRestauradoComErros (como o harness: banco parcial dá erro).
                var banco = c.Argumentos[^1];
                if (File.Exists(banco) && File.ReadAllText(banco).StartsWith("RESTAURADO:"))
                {
                    if (RestauradoComOutraConexao)
                        return (1, "bad parameters on attach or create database\n-secondary server attachments cannot validate databases");
                    return BancoRestauradoComErros ? (1, "validation errors") : (0, "");
                }
                return DeveFalhar(c) ? (1, "validation errors") : (0, "");
            }

            case "gbak":
            {
                if (!ServicoLigado) return (1, "unavailable database");
                var origem = c.Argumentos[^2];
                var destino = c.Argumentos[^1];
                if (c.Modo == "backup")
                {
                    if (DeveFalhar(c)) { File.WriteAllText(destino, "FBK-INCOMPLETO"); return (1, "write error"); }
                    File.WriteAllText(destino, "FBK:" + File.ReadAllText(origem));
                    return (0, "");
                }
                if (File.Exists(destino)) return (1, "database already exists");
                if (DeveFalhar(c)) { File.WriteAllText(destino, "PARCIAL"); return (1, "restore error"); }
                File.WriteAllText(destino, "RESTAURADO:" + File.ReadAllText(origem)[4..]);
                return (0, "");
            }

            default:
                return DeveFalhar(c) ? (1, "erro") : (0, "");
        }
    }
}

internal static class ConfigTeste
{
    public static Configuracao Criar(string pastaBase, string? senha = "S3nha-Teste!", bool modoTeste = true,
        string? pastaLog = null, bool naoInterativo = true, bool copiarProgramas = false)
    {
        // A cópia dos programas embutidos (D-015/D-016) só existe fora do modo teste; aqui fica
        // desligada por padrão para os testes de produção continuarem olhando só o que testam.
        var json = Path.Combine(pastaBase, "..", $"reparo-teste-{Guid.NewGuid():N}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(json))!);
        File.WriteAllText(json, $$"""{ "copiarProgramas": {{(copiarProgramas ? "true" : "false")}} }""");

        var variaveis = new Dictionary<string, string?>
        {
            ["REPARO_BASE_DIR"] = pastaBase,
            ["REPARO_DB_SENHA"] = senha,
            ["REPARO_NAO_INTERATIVO"] = naoInterativo ? "1" : null,
            ["REPARO_LOG_DIR"] = pastaLog ?? Path.Combine(pastaBase, "logs-reparo"),
            ["REPARO_FERRAMENTAS_DIR"] = modoTeste ? Path.Combine(pastaBase, "..", "bin") : null,
        };
        return CarregadorConfiguracao.Carregar(json, n => variaveis.TryGetValue(n, out var v) ? v : null,
            Array.Empty<string>());
    }
}

/// <summary>Pasta temporária apagada no fim do teste.</summary>
internal sealed class PastaTemporaria : IDisposable
{
    public string Caminho { get; } = Path.Combine(Path.GetTempPath(), "reparo-teste-" + Guid.NewGuid().ToString("N"));

    public PastaTemporaria() => Directory.CreateDirectory(Caminho);

    public string Arquivo(string relativo, string conteudo)
    {
        var caminho = Path.Combine(Caminho, relativo);
        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        File.WriteAllText(caminho, conteudo);
        return caminho;
    }

    public void Dispose()
    {
        try { Directory.Delete(Caminho, recursive: true); } catch (IOException) { }
    }
}
