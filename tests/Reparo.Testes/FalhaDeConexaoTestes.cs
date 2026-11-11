using Reparo.Core;

namespace Reparo.Testes;

/// <summary>Separa "não conseguiu abrir o banco" (para) de "o banco tem danos" (segue com aviso). D-014.</summary>
public sealed class FalhaDeConexaoTestes
{
    [Theory]
    [InlineData("unknown ISC error 335545106")]
    [InlineData("gbak: ERROR:unknown ISC error 335545106\ngbak:Exiting before completion due to errors")]
    [InlineData("Error occurred during login, please check server firebird.log for details")]
    [InlineData("Statement failed, SQLSTATE = 08001\nunavailable database")]
    [InlineData("Unable to complete network request to host \"localhost\".")]
    [InlineData("I/O error during \"CreateFile (open)\" operation for file \"C:\\SYSPDV\\syspdv_srv.fdb\"")]
    [InlineData("unsupported on-disk structure for file C:\\SYSPDV\\syspdv_srv.fdb; found 12.0, support 11.2")]
    public void Reconhece_falha_de_conexao(string saida) =>
        Assert.True(EtapasReparo.FalhaDeConexao(saida));

    [Theory]
    [InlineData("Summary of validation errors\n\n\tNumber of record level errors\t: 3")]
    [InlineData("checksum error on database page 1234")]
    [InlineData("database file appears corrupt ()")]
    [InlineData("I/O error during \"ReadFile\" operation for file")]
    [InlineData("")]
    public void Danos_no_banco_nao_sao_falha_de_conexao(string saida) =>
        Assert.False(EtapasReparo.FalhaDeConexao(saida));
}

/// <summary>O gfix 2.5 sai com código 0 mesmo achando erros: o resumo é que conta (caso real de 11/11/2026).</summary>
public sealed class ResumoDoGfixTestes
{
    private const string ResumoReal =
        "Summary of validation errors\n\n" +
        "\tNumber of record level errors\t: 2\n" +
        "\tNumber of data page errors\t: 1\n" +
        "\tNumber of index page errors\t: 2\n" +
        "\tNumber of database page errors\t: 3\n";

    [Fact]
    public void Soma_os_erros_do_resumo() => Assert.Equal(8, EtapasReparo.ErrosDeValidacao(ResumoReal));

    [Theory]
    [InlineData("")]
    [InlineData("gfix: nada a relatar")]
    [InlineData("Summary of validation errors\n\n\tNumber of record level errors\t: 0")]
    public void Sem_erros_no_resumo_e_zero(string saida) => Assert.Equal(0, EtapasReparo.ErrosDeValidacao(saida));

    [Theory]
    [InlineData("bad parameters on attach or create database")]
    [InlineData("bad parameters on attach or create database\n-secondary server attachments cannot validate databases")]
    public void Reconhece_gfix_barrado_por_outra_conexao(string saida) =>
        Assert.True(EtapasReparo.NaoPodeValidarAgora(saida));

    [Fact]
    public void Erros_de_validacao_nao_sao_outra_conexao() =>
        Assert.False(EtapasReparo.NaoPodeValidarAgora(ResumoReal));
}

/// <summary>Conexão que cai ou fica sem resposta no meio do gfix (caso real de 11/11/2026, D-026).</summary>
public sealed class PerdaDeConexaoTestes
{
    [Theory]
    [InlineData("Error reading data from the connection.\n-O tempo limite de espera foi atingido. ")]
    [InlineData("Error writing data to the connection.")]
    [InlineData("connection shutdown")]
    public void Reconhece_conexao_perdida(string saida) => Assert.True(EtapasReparo.PerdeuConexao(saida));

    [Theory]
    [InlineData("Summary of validation errors\n\n\tNumber of record level errors\t: 2")]
    [InlineData("bad parameters on attach or create database")]
    [InlineData("")]
    public void Outras_respostas_nao_sao_conexao_perdida(string saida) => Assert.False(EtapasReparo.PerdeuConexao(saida));
}
