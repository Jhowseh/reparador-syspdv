namespace Reparo.Core;

public enum StatusEtapa { Ok, Aviso, Falha }

public sealed record ResultadoEtapa(StatusEtapa Status, string? Mensagem = null)
{
    public static ResultadoEtapa Ok(string? mensagem = null) => new(StatusEtapa.Ok, mensagem);
    public static ResultadoEtapa Aviso(string mensagem) => new(StatusEtapa.Aviso, mensagem);
    public static ResultadoEtapa Falha(string mensagem) => new(StatusEtapa.Falha, mensagem);
}

/// <summary>Um passo do reparo.</summary>
public sealed class Etapa
{
    /// <summary>Texto curto para o operador (ex.: "Fazer a cópia de segurança").</summary>
    public required string Descricao { get; init; }

    public required Func<ResultadoEtapa> Executar { get; init; }

    /// <summary>Crítica: se falhar, o reparo para e o que foi feito é desfeito. Não crítica: vira aviso.</summary>
    public bool Critica { get; init; } = true;

    /// <summary>Como voltar atrás o que esta etapa fez (só é chamado se ela tiver dado certo).</summary>
    public Func<ResultadoEtapa>? Desfazer { get; init; }

    /// <summary>
    /// Depois que esta etapa dá certo, não se desfaz mais nada (ex.: o banco restaurado foi conferido:
    /// ele é válido e uma falha depois disso não deve trazer de volta o banco antigo).
    /// </summary>
    public bool PontoSemVolta { get; init; }
}

public sealed record ResultadoFluxo(
    bool Concluido,
    string? EtapaQueFalhou,
    string? MensagemFalha,
    bool PassouPontoSemVolta,
    bool Desfeito,
    bool FalhaAoDesfazer,
    IReadOnlyList<string> Avisos);

/// <summary>Executa as etapas em ordem. Falha crítica: para e roda os "desfazer" na ordem inversa.</summary>
public sealed class MotorFluxo
{
    private readonly IRegistro _registro;
    private readonly IInteracao _interacao;

    public MotorFluxo(IRegistro registro, IInteracao interacao)
    {
        _registro = registro;
        _interacao = interacao;
    }

    public ResultadoFluxo Executar(IReadOnlyList<Etapa> etapas)
    {
        var desfazer = new Stack<Etapa>();
        var avisos = new List<string>();
        var passouPontoSemVolta = false;

        for (var i = 0; i < etapas.Count; i++)
        {
            var etapa = etapas[i];
            _interacao.MostrarProgresso(i + 1, etapas.Count, etapa.Descricao);
            _registro.Info($"--- Etapa {i + 1} de {etapas.Count}: {etapa.Descricao}");

            var resultado = ExecutarProtegido(etapa.Executar, etapa.Descricao);

            if (resultado.Status == StatusEtapa.Falha && !etapa.Critica)
                resultado = ResultadoEtapa.Aviso(resultado.Mensagem ?? $"\"{etapa.Descricao}\" não deu certo.");

            _interacao.EtapaTerminou(i + 1, resultado.Status);

            switch (resultado.Status)
            {
                case StatusEtapa.Ok:
                    _registro.Info($"Etapa concluída: {etapa.Descricao}" + Sufixo(resultado.Mensagem));
                    break;

                case StatusEtapa.Aviso:
                    _registro.Aviso($"Etapa concluída com aviso: {etapa.Descricao}" + Sufixo(resultado.Mensagem));
                    avisos.Add($"{etapa.Descricao}: {resultado.Mensagem}");
                    break;

                default:
                    _registro.Erro($"Etapa FALHOU: {etapa.Descricao}" + Sufixo(resultado.Mensagem));
                    var (desfeito, falhaAoDesfazer) = DesfazerTudo(desfazer);
                    return new ResultadoFluxo(false, etapa.Descricao, resultado.Mensagem, passouPontoSemVolta,
                        desfeito, falhaAoDesfazer, avisos);
            }

            if (etapa.Desfazer is not null)
                desfazer.Push(etapa);

            if (etapa.PontoSemVolta)
            {
                desfazer.Clear();
                passouPontoSemVolta = true;
                _registro.Info("Ponto sem volta: daqui em diante nada será desfeito.");
            }
        }

        return new ResultadoFluxo(true, null, null, passouPontoSemVolta, false, false, avisos);
    }

    private (bool desfeito, bool falhaAoDesfazer) DesfazerTudo(Stack<Etapa> pilha)
    {
        if (pilha.Count == 0)
            return (false, false);

        _registro.Aviso("Desfazendo o que foi feito...");
        var falhou = false;
        while (pilha.Count > 0)
        {
            var etapa = pilha.Pop();
            _registro.Info($"Desfazendo: {etapa.Descricao}");
            var r = ExecutarProtegido(etapa.Desfazer!, "desfazer " + etapa.Descricao);
            if (r.Status == StatusEtapa.Falha)
            {
                falhou = true;
                _registro.Erro($"NÃO foi possível desfazer: {etapa.Descricao}" + Sufixo(r.Mensagem));
            }
            else
            {
                _registro.Info($"Desfeito: {etapa.Descricao}" + Sufixo(r.Mensagem));
            }
        }

        return (true, falhou);
    }

    private ResultadoEtapa ExecutarProtegido(Func<ResultadoEtapa> acao, string descricao)
    {
        try
        {
            return acao();
        }
        catch (Exception ex)
        {
            _registro.Erro($"Erro inesperado em \"{descricao}\": {ex}");
            return ResultadoEtapa.Falha($"Erro inesperado: {ex.Message}");
        }
    }

    private static string Sufixo(string? mensagem) => string.IsNullOrWhiteSpace(mensagem) ? "" : $" — {mensagem}";
}
