using Reparo.Core;

namespace Reparo.Testes;

/// <summary>Executa scripts .cmd de teste (inofensivos) criados numa pasta temporária.</summary>
public sealed class ExecutorProcessosTestes : IDisposable
{
    private const string Senha = "Exec-S3nha!";
    private readonly PastaTemporaria _temp = new();
    private readonly string _base;
    private readonly string _bin;
    private readonly RegistroFalso _registro = new();

    public ExecutorProcessosTestes()
    {
        _base = Path.Combine(_temp.Caminho, "SYSPDV");
        _bin = Path.Combine(_temp.Caminho, "bin");
        Directory.CreateDirectory(_base);
        Directory.CreateDirectory(_bin);
    }

    public void Dispose() => _temp.Dispose();

    private void Script(string nome, string corpo) =>
        File.WriteAllText(Path.Combine(_bin, nome + ".cmd"), "@echo off\r\n" + corpo + "\r\n");

    private ExecutorProcessos Executor(Configuracao? cfg = null, Func<string, string?>? lerVariavel = null)
    {
        cfg ??= ConfigTeste.Criar(_base, Senha);
        return new ExecutorProcessos(cfg, _registro, new MascaradorSenha(cfg.Senha), lerVariavel);
    }

    [Fact]
    public void Modo_teste_usa_o_cmd_da_pasta_de_ferramentas()
    {
        Script("gfix", "echo argumentos=[%*]\r\necho pasta=[%CD%]\r\nexit /b 7");

        var r = Executor().Executar("gfix", new[] { "-v", "-f", @"C:\Pasta Com Espaco\banco.fdb" });

        Assert.Equal(7, r.CodigoSaida);
        Assert.False(r.Sucesso);
        Assert.Contains(@"argumentos=[-v -f ""C:\Pasta Com Espaco\banco.fdb""]", r.Saida);
        Assert.Contains($"pasta=[{_base}]", r.Saida);
        Assert.Contains(_registro.Linhas, l => l.StartsWith("SAIDA gfix> argumentos="));
        Assert.Contains(_registro.Linhas, l => l.Contains("terminou com código 7"));
    }

    [Fact]
    public void Credenciais_vao_por_variavel_e_a_saida_e_mascarada()
    {
        Script("gbak", "echo usuario=[%ISC_USER%] senha=[%ISC_PASSWORD%] args=[%*]");

        var r = Executor().Executar("gbak", new[] { "-b", "a.fdb", "b.fbk" }, new OpcoesProcesso { UsarCredenciais = true });

        Assert.True(r.Sucesso);
        Assert.Contains("usuario=[SYSDBA]", r.Saida);
        Assert.Contains("senha=[********]", r.Saida);
        Assert.Contains("args=[-b a.fdb b.fbk]", r.Saida);
        Assert.DoesNotContain(Senha, r.Saida);
        Assert.DoesNotContain(Senha, _registro.Tudo);
    }

    [Fact]
    public void Sem_credenciais_a_senha_nao_e_passada()
    {
        Script("net", "echo senha=[%ISC_PASSWORD%]");

        var r = Executor().Executar("net", new[] { "start", "Servico" });

        Assert.Contains("senha=[]", r.Saida);
    }

    [Fact]
    public void Ferramenta_ausente_nao_inicia_e_nao_cai_para_a_real()
    {
        var executor = Executor();

        Assert.Null(executor.Localizar("gbak"));
        var r = executor.Executar("gbak", new[] { "-b" });
        Assert.True(r.NaoIniciou);
        Assert.False(r.Sucesso);
    }

    [Fact]
    public void Tempo_esgotado_interrompe_o_programa()
    {
        Script("net", "ping -n 30 127.0.0.1 > nul");

        var r = Executor().Executar("net", new[] { "stop", "x" }, new OpcoesProcesso { TempoLimite = TimeSpan.FromSeconds(1) });

        Assert.True(r.TempoEsgotado);
        Assert.False(r.Sucesso);
        Assert.True(r.Duracao < TimeSpan.FromSeconds(20));
    }

    [Fact]
    public void Sem_esperar_informa_se_fechou_logo_com_erro()
    {
        Script("syspdv_server", "exit /b 3");

        var r = Executor().Executar("syspdv_server", Array.Empty<string>(),
            new OpcoesProcesso { EsperarTerminar = false, TempoObservacao = TimeSpan.FromSeconds(10) });

        Assert.Equal(3, r.CodigoSaida);
        Assert.False(r.AindaRodando);
    }

    [Fact]
    public void Sem_esperar_aceita_programa_que_continua_aberto()
    {
        Script("dbMonitor", "ping -n 5 127.0.0.1 > nul");

        var r = Executor().Executar("dbMonitor", Array.Empty<string>(),
            new OpcoesProcesso { EsperarTerminar = false, TempoObservacao = TimeSpan.FromMilliseconds(300) });

        Assert.True(r.AindaRodando);
    }

    [Fact]
    public void Producao_procura_caminho_configurado_pasta_base_e_PATH()
    {
        var cfg = ConfigTeste.Criar(_base, Senha, modoTeste: false);
        var naBase = _temp.Arquivo(@"SYSPDV\VERIFICAINDICE.exe", "");
        var noPath = _temp.Arquivo(@"outros\gbak.exe", "");
        var executor = Executor(cfg, nome => nome == "PATH" ? Path.Combine(_temp.Caminho, "outros") : null);

        Assert.Equal(naBase, executor.Localizar("VERIFICAINDICE"));
        Assert.Equal(noPath, executor.Localizar("gbak"));
        Assert.Null(executor.Localizar("gfix"));
    }

    [Fact]
    public void Net_e_taskkill_vem_sempre_da_pasta_do_sistema()
    {
        var cfg = ConfigTeste.Criar(_base, Senha, modoTeste: false);
        _temp.Arquivo(@"SYSPDV\net.exe", "falso, deixado na pasta do SysPDV");
        var executor = Executor(cfg, nome => nome == "PATH" ? _base : null);

        // Só localiza (não executa).
        Assert.Equal(Path.Combine(Environment.SystemDirectory, "net.exe"), executor.Localizar("net"), ignoreCase: true);
        Assert.Equal(Path.Combine(Environment.SystemDirectory, "taskkill.exe"), executor.Localizar("taskkill"), ignoreCase: true);
    }

    [Theory]
    [InlineData("simples", "simples")]
    [InlineData("com espaco", "\"com espaco\"")]
    [InlineData("a&b", "\"a&b\"")]
    [InlineData("", "\"\"")]
    public void Aspas_para_o_cmd(string entrada, string esperado)
    {
        Assert.Equal(esperado, ExecutorProcessos.AspasCmd(entrada));
    }

    [Fact]
    public void Aspas_dentro_do_argumento_sao_recusadas_no_cmd()
    {
        Assert.Throws<ArgumentException>(() => ExecutorProcessos.AspasCmd("a\"b"));
    }
}
