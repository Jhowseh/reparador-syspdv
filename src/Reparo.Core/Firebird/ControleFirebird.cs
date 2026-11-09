namespace Reparo.Core;

/// <summary>Liga e para o serviço do Firebird sempre por "net start/stop" (o harness simula esse comando).</summary>
public sealed class ControleFirebird
{
    // Números das mensagens do "net" (iguais em qualquer idioma do Windows).
    private const string MsgServicoNaoIniciado = "3521";  // net stop em serviço parado
    private const string MsgServicoJaIniciado = "2182";   // net start em serviço rodando
    private const int TaskkillProcessoNaoEncontrado = 128;

    private readonly Configuracao _configuracao;
    private readonly IExecutorProcessos _executor;
    private readonly IRegistro _registro;

    public ControleFirebird(Configuracao configuracao, IExecutorProcessos executor, IRegistro registro)
    {
        _configuracao = configuracao;
        _executor = executor;
        _registro = registro;
    }

    private OpcoesProcesso ComTempoLimite => new()
    {
        TempoLimite = TimeSpan.FromSeconds(_configuracao.TempoLimitePararServicoSegundos),
    };

    public ResultadoEtapa Parar()
    {
        var r = _executor.Executar("net", new[] { "stop", _configuracao.ServicoFirebird }, ComTempoLimite);
        if (r.Sucesso)
            return ResultadoEtapa.Ok();

        if (r.CodigoSaida == 2 && r.Saida.Contains(MsgServicoNaoIniciado, StringComparison.Ordinal))
            return ResultadoEtapa.Ok("o Firebird já estava parado");

        // R25: só mata o processo se o serviço não parou normalmente.
        _registro.Aviso("O Firebird não parou normalmente. Tentando encerrar o processo.");
        var k = _executor.Executar("taskkill", new[] { "/F", "/IM", _configuracao.ProcessoFirebird }, ComTempoLimite);
        if (k.Sucesso || k.CodigoSaida == TaskkillProcessoNaoEncontrado)
            return ResultadoEtapa.Aviso("o Firebird não parou normalmente e foi encerrado à força");

        return ResultadoEtapa.Falha("não foi possível parar o Firebird");
    }

    public ResultadoEtapa Ligar()
    {
        var r = _executor.Executar("net", new[] { "start", _configuracao.ServicoFirebird }, ComTempoLimite);
        if (r.Sucesso)
            return ResultadoEtapa.Ok();

        if (r.CodigoSaida == 2 && r.Saida.Contains(MsgServicoJaIniciado, StringComparison.Ordinal))
            return ResultadoEtapa.Ok("o Firebird já estava ligado");

        return ResultadoEtapa.Falha("não foi possível ligar o Firebird");
    }

    public ResultadoEtapa Reiniciar()
    {
        var parar = Parar();
        if (parar.Status == StatusEtapa.Falha)
            return parar;

        var ligar = Ligar();
        if (ligar.Status == StatusEtapa.Falha)
            return ligar;

        return parar.Status == StatusEtapa.Aviso ? parar : ResultadoEtapa.Ok();
    }
}
