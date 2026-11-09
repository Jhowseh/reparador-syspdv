using Reparo.Core;

namespace Reparo.App;

/// <summary>Modo não interativo: não pergunta nada, não pausa, não abre janelas; escreve na saída padrão.</summary>
internal sealed class InteracaoAutomatica : IInteracao
{
    private readonly MascaradorSenha _mascarador;

    public InteracaoAutomatica(MascaradorSenha mascarador)
    {
        _mascarador = mascarador;
    }

    public bool Confirmar(string mensagem)
    {
        Escrever($"[confirmado automaticamente] {mensagem.Replace('\n', ' ')}");
        return true;
    }

    public void Avisar(string mensagem) => Escrever($"[aviso] {mensagem}");

    public void MostrarProgresso(int etapaAtual, int totalEtapas, string descricao) =>
        Escrever($"[etapa {etapaAtual} de {totalEtapas}] {descricao}");

    public void EtapaTerminou(int etapaAtual, StatusEtapa status)
    {
        if (status != StatusEtapa.Ok)
            Escrever($"[etapa {etapaAtual}] {(status == StatusEtapa.Aviso ? "concluída com aviso" : "FALHOU")}");
    }

    public void MostrarResultado(bool sucesso, string mensagem) =>
        Escrever((sucesso ? "[SUCESSO] " : "[FALHA] ") + mensagem);

    private void Escrever(string texto) => Console.WriteLine(_mascarador.Mascarar(texto));
}
