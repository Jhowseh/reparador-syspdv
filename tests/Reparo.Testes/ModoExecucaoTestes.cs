using Reparo.Core;

namespace Reparo.Testes;

public class ModoExecucaoTestes
{
    [Theory]
    [InlineData("1", true)]
    [InlineData(" 1 ", true)]
    [InlineData("0", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Variavel_define_modo(string? valor, bool esperado)
    {
        Assert.Equal(esperado, ModoExecucao.EhNaoInterativo(Array.Empty<string>(), valor));
    }

    [Fact]
    public void Argumento_ativa_modo_nao_interativo()
    {
        Assert.True(ModoExecucao.EhNaoInterativo(new[] { "--nao-interativo" }, null));
        Assert.True(ModoExecucao.EhNaoInterativo(new[] { "--NAO-INTERATIVO" }, null));
        Assert.False(ModoExecucao.EhNaoInterativo(new[] { "--outro" }, null));
    }
}
