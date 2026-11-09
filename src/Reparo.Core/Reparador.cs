namespace Reparo.Core;

/// <summary>Ponto de entrada da lógica do reparo. Não depende de tela.</summary>
public sealed class Reparador
{
    private readonly Configuracao _configuracao;
    private readonly IInteracao _interacao;
    private readonly IRegistro _registro;
    private readonly IExecutorProcessos _executor;
    private readonly IAmbienteSistema _ambiente;
    private readonly Func<DateTime> _agora;
    private readonly IReadOnlyList<ArquivoEmbutido>? _programas;

    /// <param name="programas">Programas a colocar na pasta do SysPDV; nulo = os embutidos no .exe.</param>
    public Reparador(Configuracao configuracao, IInteracao interacao, IRegistro registro,
        IExecutorProcessos executor, IAmbienteSistema ambiente, Func<DateTime>? agora = null,
        IReadOnlyList<ArquivoEmbutido>? programas = null)
    {
        _configuracao = configuracao;
        _interacao = interacao;
        _registro = registro;
        _executor = executor;
        _ambiente = ambiente;
        _agora = agora ?? (() => DateTime.Now);
        _programas = programas;
    }

    /// <summary>Mensagem final para o operador (preenchida ao terminar).</summary>
    public string MensagemFinal { get; private set; } = string.Empty;

    public CodigoSaida Executar()
    {
        _registro.Info("=== Reparo do banco SysPDV ===");
        _registro.Info($"Computador: {Environment.MachineName} | Usuário do Windows: {Environment.UserName}");
        _registro.Info($"Versão do app: {typeof(Reparador).Assembly.GetName().Version}");
        foreach (var linha in _configuracao.Descrever())
            _registro.Info("Configuração - " + linha);

        var resultado = ExecutarReparo();
        _registro.Info($"=== Fim: {resultado} (código {(int)resultado}) ===");
        return resultado;
    }

    private CodigoSaida ExecutarReparo()
    {
        var problemas = _configuracao.Validar();
        if (problemas.Count > 0)
        {
            foreach (var problema in problemas)
                _registro.Erro("Configuração: " + problema);

            return Terminar(CodigoSaida.ConfiguracaoInvalida,
                "A configuração tem problemas e o reparo não foi iniciado:" + Environment.NewLine
                + string.Join(Environment.NewLine, problemas.Select(p => " - " + p)));
        }

        // R23: avisar que os caixas serão desconectados.
        var confirmou = _interacao.Confirmar(
            "O reparo vai desligar o banco de dados por alguns minutos.\n" +
            "Todos os caixas serão desconectados. Feche o sistema em todos os caixas antes de continuar.\n\n" +
            "Deseja começar o reparo agora?");
        if (!confirmou)
            return Terminar(CodigoSaida.CanceladoPeloOperador, "O reparo foi cancelado. Nada foi alterado.");

        _registro.Info("Operador confirmou o início do reparo.");

        var firebird = new ControleFirebird(_configuracao, _executor, _registro);
        EtapasReparo etapas;
        ResultadoFluxo fluxo;
        var firebirdOk = false;
        try
        {
            var programas = _configuracao.CopiarProgramas
                ? _programas ?? ProgramasDoReparo.Embutidos()
                : Array.Empty<ArquivoEmbutido>();
            etapas = new EtapasReparo(_configuracao, _executor, _registro, _interacao, _ambiente, firebird, _agora(),
                programas);
            fluxo = new MotorFluxo(_registro, _interacao).Executar(etapas.Montar());
        }
        finally
        {
            // R15: termine como terminar (inclusive com erro inesperado), o Firebird fica ligado.
            firebirdOk = GarantirFirebirdLigado(firebird);
        }

        return Concluir(fluxo, etapas, firebirdOk);
    }

    private bool GarantirFirebirdLigado(ControleFirebird firebird)
    {
        _registro.Info("--- Garantindo que o Firebird fique ligado");
        try
        {
            if (firebird.Ligar().Status != StatusEtapa.Falha)
                return true;
        }
        catch (Exception ex)
        {
            _registro.Erro("Erro ao ligar o Firebird: " + ex);
        }

        _registro.Erro("O Firebird não ficou ligado no final.");
        return false;
    }

    private CodigoSaida Concluir(ResultadoFluxo fluxo, EtapasReparo etapas, bool firebirdOk)
    {
        var avisoFirebird = firebirdOk
            ? ""
            : "\n\nATENÇÃO: o serviço do Firebird não ligou. Os caixas não vão conseguir usar o sistema. Chame o suporte.";

        if (fluxo.Concluido)
        {
            var texto = "Reparo concluído com sucesso.\n" +
                        $"O banco antigo foi guardado em: {etapas.CaminhoBancoGuardado}\n" +
                        $"Cópia de segurança: {etapas.CaminhoBackupFbk}";
            if (fluxo.Avisos.Count > 0)
                texto += $"\n\nAvisos ({fluxo.Avisos.Count}):\n" + string.Join("\n", fluxo.Avisos.Select(a => " - " + a));

            return Terminar(firebirdOk ? CodigoSaida.Sucesso : CodigoSaida.FalhaNoReparo, texto + avisoFirebird);
        }

        var motivo = $"A etapa \"{fluxo.EtapaQueFalhou}\" falhou: {fluxo.MensagemFalha}.";

        if (fluxo.PassouPontoSemVolta)
        {
            // Restore deu certo e o banco novo foi conferido: ele fica, e o antigo fica guardado de propósito.
            return Terminar(CodigoSaida.FalhaNoReparo,
                "O banco foi reconstruído, mas o reparo não terminou.\n" + motivo + "\n" +
                $"O banco antigo está guardado em: {etapas.CaminhoBancoGuardado}\n" +
                "Chame o suporte antes de usar o sistema." + avisoFirebird);
        }

        if (fluxo.FalhaAoDesfazer || etapas.BancoOriginalForaDoLugar)
        {
            return Terminar(CodigoSaida.FalhaAoDesfazer,
                "ATENÇÃO: o reparo falhou e NÃO foi possível devolver o banco original ao lugar.\n" + motivo + "\n" +
                $"O banco original está guardado em: {etapas.CaminhoBancoGuardado}\n" +
                "NÃO use o sistema. Chame o suporte e informe o arquivo de log." + avisoFirebird);
        }

        var situacao = fluxo.Desfeito
            ? "O banco original foi devolvido ao lugar. Nada foi perdido."
            : "O banco de dados não foi alterado.";
        return Terminar(CodigoSaida.FalhaNoReparo,
            "O reparo não foi concluído.\n" + motivo + "\n" + situacao + avisoFirebird);
    }

    private CodigoSaida Terminar(CodigoSaida codigo, string mensagem)
    {
        MensagemFinal = mensagem;
        if (codigo == CodigoSaida.Sucesso)
            _registro.Info(mensagem);
        else
            _registro.Erro(mensagem);

        _interacao.MostrarResultado(codigo == CodigoSaida.Sucesso, mensagem);
        return codigo;
    }
}
