using Reparo.Core;

namespace Reparo.Testes;

public class MascaradorSenhaTestes
{
    [Fact]
    public void Troca_a_senha_conhecida_em_qualquer_lugar()
    {
        var m = new MascaradorSenha("Xy#9");
        Assert.Equal("antes ******** meio ********depois", m.Mascarar("antes Xy#9 meio Xy#9depois"));
    }

    [Theory]
    [InlineData("gfix -v -f -user SYSDBA -password masterkey banco.fdb", "gfix -v -f -user SYSDBA -password ******** banco.fdb")]
    [InlineData("gbak -b -pas outra banco.fdb", "gbak -b -pas ******** banco.fdb")]
    [InlineData("gbak -b -PA \"com espaco\" x.fdb", "gbak -b -PA ******** x.fdb")]
    [InlineData("set ISC_PASSWORD=abc123", "set ISC_PASSWORD=********")]
    public void Troca_valor_de_opcao_de_senha_mesmo_desconhecido(string entrada, string esperado)
    {
        Assert.Equal(esperado, new MascaradorSenha().Mascarar(entrada));
    }

    [Theory]
    [InlineData("gbak -b -p 4096 -v x.fdb")] // -p (tamanho de página) não é senha
    [InlineData("C:\\pasta-padrao\\arquivo.txt")]
    [InlineData("-user SYSDBA")]
    public void Nao_mexe_no_que_nao_e_senha(string entrada)
    {
        Assert.Equal(entrada, new MascaradorSenha().Mascarar(entrada));
    }

    [Fact]
    public void Senha_que_contem_outra_e_trocada_inteira()
    {
        var m = new MascaradorSenha("abc", "abcdef");
        Assert.Equal("[********]", m.Mascarar("[abcdef]"));
    }

    [Fact]
    public void Senha_vazia_ou_nula_e_ignorada()
    {
        var m = new MascaradorSenha(null, "");
        Assert.Equal("texto normal", m.Mascarar("texto normal"));
        Assert.Equal(string.Empty, m.Mascarar(null));
    }
}
