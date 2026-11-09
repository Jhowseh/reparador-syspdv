using System.Text;
using Reparo.Core;

namespace Reparo.Testes;

/// <summary>gfix/gbak/fbclient/firebird.msg embutidos e colocados na pasta do SysPDV (D-015).</summary>
public sealed class InstaladorFerramentasTestes : IDisposable
{
    private readonly PastaTemporaria _temp = new();
    private readonly string _pasta;
    private readonly string _guardar;
    private readonly RegistroFalso _registro = new();

    public InstaladorFerramentasTestes()
    {
        _pasta = Path.Combine(_temp.Caminho, "SYSPDV");
        Directory.CreateDirectory(_pasta);
        _guardar = Path.Combine(_pasta, "bkpBd", "ferramentas-anteriores_20261105_100000");
    }

    public void Dispose() => _temp.Dispose();

    private static IReadOnlyList<ArquivoEmbutido> Falsos() => ProgramasDoReparo.Nomes
        .Select(n => new ArquivoEmbutido(n, Encoding.ASCII.GetBytes("NOVO:" + n)))
        .ToList();

    private ResultadoEtapa Instalar(IReadOnlyList<ArquivoEmbutido>? arquivos = null) =>
        new InstaladorFerramentas(_pasta, _guardar, _registro).Instalar(arquivos ?? Falsos());

    private string Ler(string nome) => File.ReadAllText(Path.Combine(_pasta, nome));

    [Fact]
    public void Embutidos_sao_exatamente_os_arquivos_entregues()
    {
        var esperados = new Dictionary<string, string>
        {
            // Firebird 2.5.1.26351 (pasta firebird\)
            ["fbclient.dll"] = "96303A1B2133F1B6FA90240D3ACC0A2BE291473CF5CC1F72FD89F5B65CCA9286",
            ["gbak.exe"] = "82AC13C9158B2149115D931A7545ADC53032364621B242754CF3D2ED105F9FB2",
            ["gfix.exe"] = "C1DCF8BA788171512975EF38778FFB925B1B33924E0660BF4AD58C45AD8D1FB4",
            ["firebird.msg"] = "190E1ADA19F7E051F96A09F674244831FFD0E071A3E83ED296AEDE062AF74C93",
            // SysPDV (pasta syspdv\)
            ["VerificaIndice.exe"] = "EB96BE5C5091F99C0E36330C75AF937A6CA2DD841F3D6C6C085B235C33405837",
            ["dbMonitor.exe"] = "EF456B09B821F3CF8B8463896933C7A374F090EB9365E68F431084A6F09243B3",
        };

        var embutidos = ProgramasDoReparo.Embutidos();

        Assert.Equal(ProgramasDoReparo.Nomes, embutidos.Select(a => a.Nome));
        Assert.All(embutidos, a => Assert.Equal(esperados[a.Nome], a.Hash));
    }

    [Fact]
    public void Pasta_sem_os_arquivos_recebe_os_quatro()
    {
        var r = Instalar();

        Assert.Equal(StatusEtapa.Ok, r.Status);
        Assert.All(ProgramasDoReparo.Nomes, n => Assert.Equal("NOVO:" + n, Ler(n)));
        Assert.False(Directory.Exists(_guardar));
        Assert.Empty(Directory.GetFiles(_pasta, "*.reparo-novo*"));
    }

    [Fact]
    public void Arquivos_iguais_nao_sao_mexidos()
    {
        Instalar();
        var antes = File.GetLastWriteTimeUtc(Path.Combine(_pasta, "fbclient.dll"));

        var r = Instalar();

        Assert.Equal(StatusEtapa.Ok, r.Status);
        Assert.Contains("já estavam certos", r.Mensagem);
        Assert.Equal(antes, File.GetLastWriteTimeUtc(Path.Combine(_pasta, "fbclient.dll")));
        Assert.False(Directory.Exists(_guardar));
    }

    [Fact]
    public void Arquivo_de_outra_versao_e_guardado_e_trocado()
    {
        _temp.Arquivo(@"SYSPDV\fbclient.dll", "VELHO");

        var r = Instalar();

        Assert.Equal(StatusEtapa.Aviso, r.Status);
        Assert.Contains("fbclient.dll", r.Mensagem);
        Assert.Equal("NOVO:fbclient.dll", Ler("fbclient.dll"));
        Assert.Equal("VELHO", File.ReadAllText(Path.Combine(_guardar, "fbclient.dll")));
    }

    [Fact]
    public void VerificaIndice_com_nome_em_maiusculas_tambem_e_trocado()
    {
        _temp.Arquivo(@"SYSPDV\VERIFICAINDICE.EXE", "VELHO");

        Instalar();

        Assert.Equal("NOVO:VerificaIndice.exe", Ler("VerificaIndice.exe"));
        Assert.Single(Directory.GetFiles(_pasta, "verificaindice*", new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive }));
        Assert.Equal("VELHO", File.ReadAllText(Path.Combine(_guardar, "VerificaIndice.exe")));
    }

    [Fact]
    public void Arquivo_ja_guardado_antes_nao_e_sobrescrito()
    {
        _temp.Arquivo(@"SYSPDV\gbak.exe", "VELHO");
        var anterior = _temp.Arquivo(@"SYSPDV\bkpBd\ferramentas-anteriores_20261105_100000\gbak.exe", "MAIS VELHO");

        Instalar();

        Assert.Equal("MAIS VELHO", File.ReadAllText(anterior));
        Assert.Equal("VELHO", File.ReadAllText(Path.Combine(_guardar, "gbak_2.exe")));
    }

    [Fact]
    public void Arquivo_preso_para_tudo_sem_trocar_nada()
    {
        var preso = _temp.Arquivo(@"SYSPDV\fbclient.dll", "EM USO");

        ResultadoEtapa r;
        using (new FileStream(preso, FileMode.Open, FileAccess.Read, FileShare.None))
            r = Instalar();

        Assert.Equal(StatusEtapa.Falha, r.Status);
        Assert.Contains("feche-o", r.Mensagem);
        Assert.Equal("EM USO", File.ReadAllText(preso));
        Assert.False(File.Exists(Path.Combine(_pasta, "gfix.exe"))); // fbclient vai primeiro: nada mais foi trocado
        Assert.Empty(Directory.GetFiles(_pasta, "*.reparo-novo*"));
    }

    [Fact]
    public void Pacote_incompleto_nao_copia_nada()
    {
        var r = Instalar(Falsos().Where(a => a.Nome != "gbak.exe").ToList());

        Assert.Equal(StatusEtapa.Falha, r.Status);
        Assert.Contains("gbak.exe", r.Mensagem);
        Assert.Empty(Directory.GetFiles(_pasta));
    }
}
