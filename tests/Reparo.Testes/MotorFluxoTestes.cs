using Reparo.Core;

namespace Reparo.Testes;

public class MotorFluxoTestes
{
    private readonly List<string> _ordem = new();
    private readonly RegistroFalso _registro = new();

    private Etapa E(string nome, StatusEtapa status = StatusEtapa.Ok, bool critica = true, bool comDesfazer = false,
        bool pontoSemVolta = false, bool desfazerFalha = false) => new()
    {
        Descricao = nome,
        Critica = critica,
        PontoSemVolta = pontoSemVolta,
        Executar = () => { _ordem.Add(nome); return new ResultadoEtapa(status, status == StatusEtapa.Ok ? null : "motivo"); },
        Desfazer = comDesfazer
            ? () => { _ordem.Add("desfazer " + nome); return desfazerFalha ? ResultadoEtapa.Falha("x") : ResultadoEtapa.Ok(); }
            : null,
    };

    private ResultadoFluxo Rodar(params Etapa[] etapas) => new MotorFluxo(_registro, new InteracaoFalsa()).Executar(etapas);

    [Fact]
    public void Executa_todas_em_ordem()
    {
        var r = Rodar(E("a"), E("b"), E("c"));

        Assert.True(r.Concluido);
        Assert.Equal(new[] { "a", "b", "c" }, _ordem);
    }

    [Fact]
    public void Falha_critica_para_e_desfaz_na_ordem_inversa()
    {
        var r = Rodar(E("a", comDesfazer: true), E("b"), E("c", comDesfazer: true), E("d", StatusEtapa.Falha), E("e"));

        Assert.False(r.Concluido);
        Assert.Equal("d", r.EtapaQueFalhou);
        Assert.True(r.Desfeito);
        Assert.Equal(new[] { "a", "b", "c", "d", "desfazer c", "desfazer a" }, _ordem);
    }

    [Fact]
    public void Falha_nao_critica_vira_aviso_e_continua()
    {
        var r = Rodar(E("a", StatusEtapa.Falha, critica: false), E("b"));

        Assert.True(r.Concluido);
        Assert.Single(r.Avisos);
        Assert.Equal(new[] { "a", "b" }, _ordem);
    }

    [Fact]
    public void Ponto_sem_volta_impede_desfazer_o_que_veio_antes()
    {
        var r = Rodar(E("guardar", comDesfazer: true), E("restore", pontoSemVolta: true), E("indices", StatusEtapa.Falha));

        Assert.False(r.Concluido);
        Assert.True(r.PassouPontoSemVolta);
        Assert.False(r.Desfeito);
        Assert.DoesNotContain("desfazer guardar", _ordem);
    }

    [Fact]
    public void Etapa_que_falhou_nao_e_desfeita()
    {
        Rodar(E("a", comDesfazer: true), E("b", StatusEtapa.Falha, comDesfazer: true));
        Assert.Equal(new[] { "a", "b", "desfazer a" }, _ordem);
    }

    [Fact]
    public void Excecao_vira_falha_e_dispara_o_desfazer()
    {
        var explode = new Etapa { Descricao = "explode", Executar = () => throw new IOException("disco") };
        var r = Rodar(E("a", comDesfazer: true), explode);

        Assert.False(r.Concluido);
        Assert.Contains("disco", r.MensagemFalha);
        Assert.Contains("desfazer a", _ordem);
    }

    [Fact]
    public void Falha_ao_desfazer_e_informada_e_os_outros_ainda_sao_tentados()
    {
        var r = Rodar(E("a", comDesfazer: true), E("b", comDesfazer: true, desfazerFalha: true), E("c", StatusEtapa.Falha));

        Assert.True(r.FalhaAoDesfazer);
        Assert.Equal(new[] { "a", "b", "c", "desfazer b", "desfazer a" }, _ordem);
    }
}
