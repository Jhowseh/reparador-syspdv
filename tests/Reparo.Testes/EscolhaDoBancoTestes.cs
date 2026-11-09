using Reparo.Core;

namespace Reparo.Testes;

/// <summary>Configuração sem reparo.json: banco escolhido na tela + Firebird detectado.</summary>
public sealed class EscolhaDoBancoTestes : IDisposable
{
    private readonly PastaTemporaria _temp = new();
    private readonly string _binFirebird;

    private sealed class DetectorFalso : IDetectorFirebird
    {
        public InstalacaoFirebird Resposta { get; set; } = InstalacaoFirebird.NaoEncontrada;
        public string? Pedido { get; private set; }
        public int Chamadas { get; private set; }

        public InstalacaoFirebird Detectar(string servicoPreferido)
        {
            Pedido = servicoPreferido;
            Chamadas++;
            return Resposta;
        }
    }

    public EscolhaDoBancoTestes()
    {
        _binFirebird = Path.Combine(_temp.Caminho, @"Firebird_2_5\bin");
        _temp.Arquivo(@"Firebird_2_5\bin\gfix.exe", "");
        _temp.Arquivo(@"Firebird_2_5\bin\gbak.exe", "");
    }

    public void Dispose() => _temp.Dispose();

    private static readonly Func<string, string?> SemVariaveis = _ => null;

    [Fact]
    public void Banco_escolhido_define_a_pasta_base_backup_e_log()
    {
        var banco = Path.Combine(_temp.Caminho, @"LOJA\SYSPDV\syspdv_srv.fdb");

        var c = CarregadorConfiguracao.Carregar(null, SemVariaveis, Array.Empty<string>(), banco);

        Assert.Equal(banco, c.CaminhoBanco);
        Assert.Equal(Path.Combine(_temp.Caminho, @"LOJA\SYSPDV"), c.PastaBase);
        Assert.Equal(Path.Combine(_temp.Caminho, @"LOJA\SYSPDV\bkpBd"), c.PastaBackupBanco);
        Assert.Equal(Path.Combine(_temp.Caminho, @"LOJA\SYSPDV\logs-reparo"), c.PastaLog);
    }

    [Fact]
    public void Ferramentas_embutidas_vencem_a_deteccao_e_rodam_da_pasta_do_SysPDV()
    {
        var banco = Path.Combine(_temp.Caminho, @"LOJA\SYSPDV\syspdv_srv.fdb");
        var detector = new DetectorFalso { Resposta = new InstalacaoFirebird(_binFirebird, "firebird.exe", "FirebirdServerOutra") };

        var c = CarregadorConfiguracao.Carregar(null, SemVariaveis, Array.Empty<string>(), banco, detector);

        Assert.True(c.CopiarProgramas);
        Assert.Equal(Path.Combine(_temp.Caminho, @"LOJA\SYSPDV\gfix.exe"), c.Ferramentas["gfix"]);
        Assert.Equal(Path.Combine(_temp.Caminho, @"LOJA\SYSPDV\gbak.exe"), c.Ferramentas["gbak"]);
        Assert.Equal(Path.Combine(_temp.Caminho, @"LOJA\SYSPDV\VerificaIndice.exe"), c.Ferramentas["VERIFICAINDICE"]);
        Assert.Equal(Path.Combine(_temp.Caminho, @"LOJA\SYSPDV\dbMonitor.exe"), c.Ferramentas["dbMonitor"]);
        Assert.Equal("FirebirdServerOutra", c.ServicoFirebird); // serviço e processo continuam detectados
        Assert.Equal("firebird.exe", c.ProcessoFirebird);
    }

    [Fact]
    public void Firebird_detectado_preenche_gfix_gbak_servico_e_processo()
    {
        var arquivo = _temp.Arquivo("reparo.json", """{ "copiarProgramas": false }""");
        var detector = new DetectorFalso { Resposta = new InstalacaoFirebird(_binFirebird, "firebird.exe", "FirebirdServerOutra") };

        var c = CarregadorConfiguracao.Carregar(arquivo, SemVariaveis, Array.Empty<string>(), detector: detector);

        Assert.False(c.CopiarProgramas);
        Assert.Equal("VERIFICAINDICE.exe", c.Ferramentas["VERIFICAINDICE"]);

        Assert.Equal(Path.Combine(_binFirebird, "gfix.exe"), c.Ferramentas["gfix"]);
        Assert.Equal(Path.Combine(_binFirebird, "gbak.exe"), c.Ferramentas["gbak"]);
        Assert.Equal("FirebirdServerOutra", c.ServicoFirebird);
        Assert.Equal("firebird.exe", c.ProcessoFirebird);
        Assert.Equal("FirebirdServerDefaultInstance", detector.Pedido);
        Assert.Contains(c.Descrever(), l => l.Contains(Path.Combine(_binFirebird, "gbak.exe")));
    }

    [Fact]
    public void Configuracao_manual_vence_a_deteccao()
    {
        var arquivo = _temp.Arquivo("reparo.json", """
            { "processoFirebird": "fbserver.exe", "copiarProgramas": false,
              "ferramentas": { "gbak": "D:\\meu\\gbak.exe" } }
            """);
        var detector = new DetectorFalso { Resposta = new InstalacaoFirebird(_binFirebird, "firebird.exe", "Detectado") };
        var variaveis = new Dictionary<string, string?> { ["REPARO_SERVICO_FIREBIRD"] = "DaVariavel" };

        var c = CarregadorConfiguracao.Carregar(arquivo, n => variaveis.GetValueOrDefault(n), Array.Empty<string>(),
            detector: detector);

        Assert.Equal(@"D:\meu\gbak.exe", c.Ferramentas["gbak"]);
        Assert.Equal(Path.Combine(_binFirebird, "gfix.exe"), c.Ferramentas["gfix"]);
        Assert.Equal("DaVariavel", c.ServicoFirebird);
        Assert.Equal("fbserver.exe", c.ProcessoFirebird);
        Assert.Equal("DaVariavel", detector.Pedido);
    }

    [Fact]
    public void Modo_teste_nao_usa_a_deteccao()
    {
        var detector = new DetectorFalso { Resposta = new InstalacaoFirebird(_binFirebird, "firebird.exe", "Detectado") };
        var variaveis = new Dictionary<string, string?> { ["REPARO_FERRAMENTAS_DIR"] = @"C:\harness\bin" };

        var c = CarregadorConfiguracao.Carregar(null, n => variaveis.GetValueOrDefault(n), Array.Empty<string>(),
            detector: detector);

        Assert.Equal(0, detector.Chamadas);
        Assert.Equal("FirebirdServerDefaultInstance", c.ServicoFirebird);
        Assert.Equal("gfix.exe", c.Ferramentas["gfix"]);
        Assert.False(c.CopiarProgramas); // o harness nunca recebe arquivos de verdade
    }

    [Fact]
    public void Nada_detectado_usa_os_padroes()
    {
        var c = CarregadorConfiguracao.Carregar(null, SemVariaveis, Array.Empty<string>(), detector: new DetectorFalso());

        Assert.Equal("FirebirdServerDefaultInstance", c.ServicoFirebird);
        Assert.Equal("fbserver.exe", c.ProcessoFirebird);
        Assert.Equal(@"C:\SYSPDV\gbak.exe", c.Ferramentas["gbak"], ignoreCase: true);
    }

    [Theory]
    [InlineData(@"""C:\Program Files\Firebird\Firebird_2_5\bin\fbserver.exe"" -s DefaultInstance", @"C:\Program Files\Firebird\Firebird_2_5\bin\fbserver.exe")]
    [InlineData(@"C:\Firebird\bin\fbserver.exe -s DefaultInstance", @"C:\Firebird\bin\fbserver.exe")]
    [InlineData(@"C:\FB\bin\firebird.EXE", @"C:\FB\bin\firebird.EXE")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Extrai_o_executavel_da_linha_do_servico(string? linha, string? esperado)
    {
        Assert.Equal(esperado, DetectorFirebirdWindows.ExtrairExecutavel(linha));
    }

    [Fact]
    public void Pastas_perigosas_sao_recusadas()
    {
        Assert.True(Configuracao.PastaPerigosa(@"C:\"));
        Assert.True(Configuracao.PastaPerigosa(@"D:\"));
        Assert.True(Configuracao.PastaPerigosa(Environment.GetFolderPath(Environment.SpecialFolder.Windows)));
        Assert.True(Configuracao.PastaPerigosa(Environment.SystemDirectory));
        Assert.True(Configuracao.PastaPerigosa(Environment.GetFolderPath(Environment.SpecialFolder.Desktop)));
        Assert.True(Configuracao.PastaPerigosa(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
        Assert.False(Configuracao.PastaPerigosa(@"C:\SYSPDV"));
        Assert.False(Configuracao.PastaPerigosa(Path.Combine(_temp.Caminho, "SYSPDV")));
    }

    [Fact]
    public void Banco_na_raiz_do_disco_e_invalido()
    {
        var c = CarregadorConfiguracao.Carregar(null, _ => null, new[] { "--nao-interativo" }, @"C:\syspdv_srv.fdb");
        c.Senha = "x";

        Assert.Contains(c.Validar(), p => p.Contains("pasta do SysPDV"));
    }
}
