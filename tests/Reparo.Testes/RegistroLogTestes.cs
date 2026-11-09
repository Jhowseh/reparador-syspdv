using Reparo.Core;

namespace Reparo.Testes;

public sealed class RegistroLogTestes : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "reparo-log-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTime Momento = new(2026, 11, 3, 14, 5, 9);

    public void Dispose()
    {
        if (Directory.Exists(_pasta))
            Directory.Delete(_pasta, recursive: true);
    }

    [Fact]
    public void Cria_a_pasta_e_o_arquivo_com_data_e_hora()
    {
        using var log = new RegistroLog(_pasta, new MascaradorSenha(), () => Momento);
        log.Info("olá");

        Assert.Equal(Path.Combine(_pasta, "reparo_20261103_140509.log"), log.CaminhoArquivo);
        Assert.True(File.Exists(log.CaminhoArquivo));
    }

    [Fact]
    public void Grava_cada_linha_na_hora_com_nivel()
    {
        using var log = new RegistroLog(_pasta, new MascaradorSenha(), () => Momento);
        log.Info("primeira");
        log.Erro("segunda\nterceira");
        log.SaidaFerramenta("gbak", "linha do gbak");

        // Lê com o arquivo ainda aberto: prova que não ficou em buffer.
        var linhas = LerCompartilhado(log.CaminhoArquivo);
        Assert.Equal(new[]
        {
            "2026-11-03 14:05:09.000 [INFO ] primeira",
            "2026-11-03 14:05:09.000 [ERRO ] segunda",
            "2026-11-03 14:05:09.000 [ERRO ] terceira",
            "2026-11-03 14:05:09.000 [SAIDA] gbak> linha do gbak",
        }, linhas);
    }

    [Fact]
    public void Senha_nunca_chega_ao_arquivo()
    {
        const string senha = "P@ss-Secreta";
        using (var log = new RegistroLog(_pasta, new MascaradorSenha(senha), () => Momento))
        {
            log.Info($"gfix -user SYSDBA -password {senha} banco.fdb");
            log.Aviso($"senha={senha}");
            log.SaidaFerramenta("gbak", $"erro com {senha}");
            log.Erro(new InvalidOperationException($"falhou {senha}").ToString());
        }

        var conteudo = File.ReadAllText(Directory.GetFiles(_pasta).Single());
        Assert.DoesNotContain(senha, conteudo);
        Assert.Contains("********", conteudo);
    }

    [Fact]
    public void Nunca_sobrescreve_log_existente()
    {
        Directory.CreateDirectory(_pasta);
        var existente = Path.Combine(_pasta, "reparo_20261103_140509.log");
        File.WriteAllText(existente, "log antigo");

        using (var log = new RegistroLog(_pasta, new MascaradorSenha(), () => Momento))
        {
            log.Info("novo");
            Assert.Equal(Path.Combine(_pasta, "reparo_20261103_140509_2.log"), log.CaminhoArquivo);
        }

        Assert.Equal("log antigo", File.ReadAllText(existente));
    }

    [Fact]
    public void Pasta_invalida_gera_erro_para_o_operador()
    {
        var invalida = Path.Combine(_pasta, "a\0b");
        var ex = Assert.Throws<LogIndisponivelException>(() => new RegistroLog(invalida, new MascaradorSenha()));
        Assert.Contains("log", ex.Message);
    }

    private static string[] LerCompartilhado(string caminho)
    {
        using var fluxo = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var leitor = new StreamReader(fluxo);
        return leitor.ReadToEnd().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
    }
}
