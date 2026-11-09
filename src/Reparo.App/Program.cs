using Reparo.Core;

namespace Reparo.App;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        var naoInterativo = ModoExecucao.EhNaoInterativo(
            args, Environment.GetEnvironmentVariable(ModoExecucao.VariavelNaoInterativo));

        return naoInterativo ? ExecutarSemTela(args) : ExecutarComTela(args);
    }

    private static int ExecutarSemTela(string[] args)
    {
        Configuracao configuracao;
        try
        {
            configuracao = CarregadorConfiguracao.CarregarDoAmbiente(args);
        }
        catch (ConfiguracaoInvalidaException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return (int)CodigoSaida.ConfiguracaoInvalida;
        }

        var mascarador = new MascaradorSenha(configuracao.Senha);
        var sessao = SessaoReparo.Executar(
            configuracao,
            new InteracaoAutomatica(mascarador),
            mascarador,
            informarErro: msg => Console.Error.WriteLine(mascarador.Mascarar(msg)),
            logCriado: caminho => Console.WriteLine($"Log do reparo: {caminho}"));

        Console.WriteLine($"Resultado: {sessao.Codigo} (código {(int)sessao.Codigo})");
        return (int)sessao.Codigo;
    }

    private static int ExecutarComTela(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // Só para sugerir o banco padrão na tela; a configuração de verdade é montada para o banco escolhido.
        string? bancoSugerido;
        try
        {
            var padrao = CarregadorConfiguracao.CarregarDoAmbiente(args);
            bancoSugerido = File.Exists(padrao.CaminhoBanco) ? padrao.CaminhoBanco : null;
        }
        catch (ConfiguracaoInvalidaException ex)
        {
            MessageBox.Show(ex.Message + "\n\nChame o suporte.", JanelaPrincipal.Titulo,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return (int)CodigoSaida.ConfiguracaoInvalida;
        }

        using var janela = new JanelaPrincipal(bancoSugerido,
            banco => CarregadorConfiguracao.CarregarDoAmbiente(args, banco));
        Application.Run(janela);
        return (int)janela.Codigo;
    }
}
