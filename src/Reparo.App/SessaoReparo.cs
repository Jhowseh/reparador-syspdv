using Reparo.Core;

namespace Reparo.App;

internal sealed record ResultadoSessao(CodigoSaida Codigo, string? CaminhoLog);

/// <summary>
/// Uma execução do reparo: cria o log, garante uma execução por vez e protege contra erro inesperado.
/// Usada pelo modo com tela e pelo modo não interativo. Tudo roda na thread que chamar (a trava é
/// liberada na mesma thread que a pegou).
/// </summary>
internal static class SessaoReparo
{
    private const string NomeTrava = @"Global\ReparoSysPDV";

    public static ResultadoSessao Executar(Configuracao configuracao, IInteracao interacao, MascaradorSenha mascarador,
        Action<string> informarErro, Action<string>? logCriado = null)
    {
        RegistroLog registro;
        try
        {
            registro = new RegistroLog(configuracao.PastaLog, mascarador);
        }
        catch (LogIndisponivelException ex)
        {
            informarErro(mascarador.Mascarar(ex.Message));
            return new ResultadoSessao(CodigoSaida.ConfiguracaoInvalida, null);
        }

        using (registro)
        {
            logCriado?.Invoke(registro.CaminhoArquivo);

            // Dois reparos ao mesmo tempo no mesmo computador seriam desastrosos.
            using var trava = new Mutex(false, NomeTrava);
            var temTrava = false;
            try
            {
                try { temTrava = trava.WaitOne(0); }
                catch (AbandonedMutexException) { temTrava = true; }

                if (!temTrava)
                {
                    const string msg = "Já existe um reparo em andamento neste computador. Aguarde ele terminar.";
                    registro.Erro(msg);
                    informarErro(msg);
                    return new ResultadoSessao(CodigoSaida.CanceladoPeloOperador, registro.CaminhoArquivo);
                }

                var executor = new ExecutorProcessos(configuracao, registro, mascarador);
                var reparador = new Reparador(configuracao, interacao, registro, executor, new AmbienteWindows());
                return new ResultadoSessao(reparador.Executar(), registro.CaminhoArquivo);
            }
            catch (Exception ex)
            {
                // Detalhes só no log (mascarado); para o operador, só o tipo do erro (D-005).
                registro.Erro("Erro inesperado: " + ex);
                informarErro($"Aconteceu um erro inesperado ({ex.GetType().Name}). " +
                             $"Chame o suporte e informe o arquivo de log:\n{registro.CaminhoArquivo}");
                return new ResultadoSessao(CodigoSaida.ErroInesperado, registro.CaminhoArquivo);
            }
            finally
            {
                if (temTrava)
                    trava.ReleaseMutex();
            }
        }
    }
}
