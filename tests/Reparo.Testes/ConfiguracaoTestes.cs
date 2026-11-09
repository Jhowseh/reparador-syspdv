using Reparo.Core;

namespace Reparo.Testes;

public sealed class ConfiguracaoTestes : IDisposable
{
    private readonly string _pastaTemp = Path.Combine(Path.GetTempPath(), "reparo-testes-" + Guid.NewGuid().ToString("N"));

    public ConfiguracaoTestes() => Directory.CreateDirectory(_pastaTemp);

    public void Dispose() => Directory.Delete(_pastaTemp, recursive: true);

    private string Arquivo(string json)
    {
        var caminho = Path.Combine(_pastaTemp, "reparo.json");
        File.WriteAllText(caminho, json);
        return caminho;
    }

    private static Func<string, string?> Ambiente(Dictionary<string, string?> variaveis) =>
        nome => variaveis.TryGetValue(nome, out var v) ? v : null;

    private static readonly Func<string, string?> SemVariaveis = _ => null;

    [Fact]
    public void Sem_arquivo_e_sem_variaveis_usa_padroes_de_producao()
    {
        var c = CarregadorConfiguracao.Carregar(null, SemVariaveis, Array.Empty<string>());

        Assert.Equal(@"C:\SYSPDV", c.PastaBase);
        Assert.Equal(@"C:\SYSPDV\syspdv_srv.fdb", c.CaminhoBanco);
        Assert.Equal(@"C:\SYSPDV\bkpBd", c.PastaBackupBanco);
        Assert.Equal(@"C:\SYSPDV\logs-reparo", c.PastaLog);
        Assert.Equal("SYSDBA", c.Usuario);
        Assert.Null(c.Senha);
        Assert.Equal("FirebirdServerDefaultInstance", c.ServicoFirebird);
        Assert.Equal("fbserver.exe", c.ProcessoFirebird);
        Assert.Null(c.PastaFerramentas);
        Assert.False(c.ModoTeste);
        Assert.False(c.NaoInterativo);
        Assert.True(c.CopiarProgramas);
        Assert.Equal(@"C:\SYSPDV\gfix.exe", c.Ferramentas["gfix"], ignoreCase: true);
    }

    [Fact]
    public void Variaveis_tem_prioridade_sobre_o_arquivo()
    {
        var arquivo = Arquivo("""
            { "pastaBase": "D:\\ARQUIVO", "usuario": "USUARIO_ARQUIVO", "senha": "senha_arquivo",
              "servicoFirebird": "ServicoArquivo", "pastaLog": "D:\\logs-arquivo" }
            """);
        var c = CarregadorConfiguracao.Carregar(arquivo, Ambiente(new()
        {
            ["REPARO_BASE_DIR"] = @"E:\VAR",
            ["REPARO_DB_USUARIO"] = "USUARIO_VAR",
            ["REPARO_DB_SENHA"] = "senha_var",
            ["REPARO_SERVICO_FIREBIRD"] = "ServicoVar",
            ["REPARO_LOG_DIR"] = @"E:\logs-var",
            ["REPARO_FERRAMENTAS_DIR"] = @"E:\bin",
        }), Array.Empty<string>());

        Assert.Equal(@"E:\VAR", c.PastaBase);
        Assert.Equal(@"E:\VAR\syspdv_srv.fdb", c.CaminhoBanco);
        Assert.Equal("USUARIO_VAR", c.Usuario);
        Assert.Equal("senha_var", c.Senha);
        Assert.Equal("ServicoVar", c.ServicoFirebird);
        Assert.Equal(@"E:\logs-var", c.PastaLog);
        Assert.Equal(@"E:\bin", c.PastaFerramentas);
        Assert.True(c.ModoTeste);
    }

    [Fact]
    public void Arquivo_e_usado_quando_nao_ha_variavel()
    {
        var arquivo = Arquivo("""
            // comentários são aceitos
            { "PastaBase": "D:\\PDV", "banco": "outro.fdb", "senha": "abc", "processoFirebird": "firebird.exe",
              "tempoLimitePararServicoSegundos": 15,
              "ferramentas": { "GBAK": "D:\\fb\\gbak.exe" }, }
            """);
        var c = CarregadorConfiguracao.Carregar(arquivo, SemVariaveis, Array.Empty<string>());

        Assert.Equal(@"D:\PDV", c.PastaBase);
        Assert.Equal(@"D:\PDV\outro.fdb", c.CaminhoBanco);
        Assert.Equal(@"D:\PDV\logs-reparo", c.PastaLog);
        Assert.Equal("abc", c.Senha);
        Assert.Equal("firebird.exe", c.ProcessoFirebird);
        Assert.Equal(15, c.TempoLimitePararServicoSegundos);
        Assert.Equal(@"D:\fb\gbak.exe", c.Ferramentas["gbak"]);
        Assert.Equal(@"D:\PDV\gfix.exe", c.Ferramentas["gfix"]);
    }

    [Fact]
    public void Banco_com_caminho_completo_e_aceito()
    {
        var arquivo = Arquivo("""{ "banco": "F:\\dados\\syspdv_srv.fdb" }""");
        var c = CarregadorConfiguracao.Carregar(arquivo, SemVariaveis, Array.Empty<string>());
        Assert.Equal(@"F:\dados\syspdv_srv.fdb", c.CaminhoBanco);
    }

    [Fact]
    public void Variavel_vazia_e_ignorada()
    {
        var c = CarregadorConfiguracao.Carregar(null, Ambiente(new()
        {
            ["REPARO_BASE_DIR"] = "  ",
            ["REPARO_FERRAMENTAS_DIR"] = "",
        }), Array.Empty<string>());

        Assert.Equal(@"C:\SYSPDV", c.PastaBase);
        Assert.False(c.ModoTeste);
    }

    [Fact]
    public void Json_com_erro_gera_mensagem_sem_o_conteudo()
    {
        var arquivo = Arquivo("""{ "senha": "segredo123", """ + "\n" + """ "usuario": }""");
        var ex = Assert.Throws<ConfiguracaoInvalidaException>(
            () => CarregadorConfiguracao.Carregar(arquivo, SemVariaveis, Array.Empty<string>()));

        Assert.DoesNotContain("segredo123", ex.Message);
        Assert.Contains("reparo.json", ex.Message);
    }

    [Fact]
    public void Senha_nunca_aparece_na_descricao_nem_no_ToString()
    {
        var c = CarregadorConfiguracao.Carregar(null, Ambiente(new() { ["REPARO_DB_SENHA"] = "Xy#9-segredo" }),
            Array.Empty<string>());

        Assert.DoesNotContain("Xy#9-segredo", string.Join("\n", c.Descrever()));
        Assert.DoesNotContain("Xy#9-segredo", c.ToString());
        Assert.Contains("informada", c.ToString());
    }

    [Fact]
    public void Senha_preserva_espacos()
    {
        var c = CarregadorConfiguracao.Carregar(null, Ambiente(new() { ["REPARO_DB_SENHA"] = " a b " }),
            Array.Empty<string>());
        Assert.Equal(" a b ", c.Senha);
    }

    [Fact]
    public void Sem_senha_e_invalido_so_no_modo_nao_interativo()
    {
        var interativo = CarregadorConfiguracao.Carregar(null, SemVariaveis, Array.Empty<string>());
        Assert.Empty(interativo.Validar());

        var naoInterativo = CarregadorConfiguracao.Carregar(null, SemVariaveis, new[] { "--nao-interativo" });
        Assert.True(naoInterativo.NaoInterativo);
        Assert.Contains(naoInterativo.Validar(), p => p.Contains("senha"));
    }

    [Fact]
    public void Sem_janela_so_e_aceito_no_modo_teste()
    {
        var producao = CarregadorConfiguracao.Carregar(null, SemVariaveis, new[] { "--nao-interativo" });
        producao.Senha = "x";
        Assert.Contains(producao.Validar(), p => p.Contains("--nao-interativo"));

        var harness = CarregadorConfiguracao.Carregar(null,
            n => n == "REPARO_FERRAMENTAS_DIR" ? @"C:\harness\bin" : null, new[] { "--nao-interativo" });
        harness.Senha = "x";
        Assert.True(harness.ModoTeste);
        Assert.DoesNotContain(harness.Validar(), p => p.Contains("--nao-interativo"));

        var comTela = CarregadorConfiguracao.Carregar(null, SemVariaveis, Array.Empty<string>());
        Assert.DoesNotContain(comTela.Validar(), p => p.Contains("--nao-interativo"));
    }

    [Theory]
    [InlineData(@"D:\outra-pasta")]
    [InlineData(@"..\fora")]
    [InlineData(".")]
    public void Pasta_de_backup_fora_da_pasta_base_e_invalida(string pastaBackup)
    {
        var arquivo = Arquivo($$"""{ "pastaBackupBanco": "{{pastaBackup.Replace(@"\", @"\\")}}", "senha": "x" }""");
        var c = CarregadorConfiguracao.Carregar(arquivo, SemVariaveis, Array.Empty<string>());

        Assert.Contains(c.Validar(), p => p.Contains("backup"));
    }

    [Fact]
    public void Banco_com_curinga_e_invalido()
    {
        var arquivo = Arquivo("""{ "banco": "*.fdb", "senha": "x" }""");
        var c = CarregadorConfiguracao.Carregar(arquivo, SemVariaveis, Array.Empty<string>());
        Assert.Contains(c.Validar(), p => p.Contains('*'));
    }

    [Fact]
    public void Tempo_limite_zero_e_invalido()
    {
        var arquivo = Arquivo("""{ "tempoLimitePararServicoSegundos": 0, "senha": "x" }""");
        var c = CarregadorConfiguracao.Carregar(arquivo, SemVariaveis, Array.Empty<string>());
        Assert.Contains(c.Validar(), p => p.Contains("tempo limite"));
    }
}
