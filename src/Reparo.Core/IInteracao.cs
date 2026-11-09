namespace Reparo.Core;

/// <summary>
/// Tudo o que a lógica precisa do operador. O Core só fala com esta interface;
/// a tela (WinForms) e o modo não interativo (harness) fornecem implementações.
/// As chamadas podem vir de uma thread que não é a da tela.
/// </summary>
public interface IInteracao
{
    /// <summary>Pergunta sim/não. No modo não interativo responde sim automaticamente.</summary>
    bool Confirmar(string mensagem);

    /// <summary>Mostra um aviso que o operador precisa ler (não bloqueia no modo não interativo).</summary>
    void Avisar(string mensagem);

    /// <summary>Uma etapa começou: etapa atual de um total.</summary>
    void MostrarProgresso(int etapaAtual, int totalEtapas, string descricao);

    /// <summary>Uma etapa terminou (Ok, Aviso ou Falha).</summary>
    void EtapaTerminou(int etapaAtual, StatusEtapa status);

    /// <summary>Resultado final do reparo, em linguagem simples.</summary>
    void MostrarResultado(bool sucesso, string mensagem);
}
