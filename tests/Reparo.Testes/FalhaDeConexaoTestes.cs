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
