using Reparo.Core;

namespace Reparo.App;

/// <summary>
/// Liga o Core à janela. O reparo roda em segundo plano; tudo aqui passa para a thread da tela.
/// Confirmar e Avisar esperam o operador responder (o reparo fica parado até lá).
/// </summary>
internal sealed class InteracaoJanela : IInteracao
{
    private readonly JanelaPrincipal _janela;
    private readonly MascaradorSenha _mascarador;

    public InteracaoJanela(JanelaPrincipal janela, MascaradorSenha mascarador)
    {
        _janela = janela;
        _mascarador = mascarador;
    }

    public bool Confirmar(string mensagem) =>
        _janela.Invoke(() => MessageBox.Show(_janela, _mascarador.Mascarar(mensagem), JanelaPrincipal.Titulo,
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes);

    public void Avisar(string mensagem) =>
        _janela.Invoke(() => MessageBox.Show(_janela, _mascarador.Mascarar(mensagem), JanelaPrincipal.Titulo,
            MessageBoxButtons.OK, MessageBoxIcon.Information));

    public void MostrarProgresso(int etapaAtual, int totalEtapas, string descricao) =>
        _janela.BeginInvoke(() => _janela.EtapaComecou(etapaAtual, totalEtapas, descricao));

    public void EtapaTerminou(int etapaAtual, StatusEtapa status) =>
        _janela.BeginInvoke(() => _janela.EtapaTerminou(etapaAtual, status));

    public void MostrarResultado(bool sucesso, string mensagem) =>
        _janela.Invoke(() => _janela.MostrarResultado(sucesso, _mascarador.Mascarar(mensagem)));
}
