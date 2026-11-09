using System.Text;

namespace Reparo.Core;

/// <summary>Registro (log) do reparo. Toda linha passa pelo mascarador de senha.</summary>
public interface IRegistro
{
    /// <summary>Caminho do arquivo de log (vazio se o registro não grava em arquivo).</summary>
    string CaminhoArquivo { get; }

    void Info(string mensagem);
    void Aviso(string mensagem);
    void Erro(string mensagem);

    /// <summary>Linha da saída de uma ferramenta externa (gfix, gbak...).</summary>
    void SaidaFerramenta(string ferramenta, string linha);
}

/// <summary>Não foi possível criar o arquivo de log. A mensagem é para o operador.</summary>
public sealed class LogIndisponivelException : Exception
{
    public LogIndisponivelException(string mensagem, Exception interna) : base(mensagem, interna) { }
}

/// <summary>
/// Grava o log em &lt;pasta&gt;\reparo_AAAAMMDD_HHMMSS.log (UTF-8). Nunca sobrescreve um log existente.
/// Cada linha é gravada na hora (sem buffer), para o log sobreviver a uma queda do app.
/// </summary>
public sealed class RegistroLog : IRegistro, IDisposable
{
    private readonly MascaradorSenha _mascarador;
    private readonly Func<DateTime> _agora;
    private readonly StreamWriter _escritor;
    private readonly object _trava = new();

    public string CaminhoArquivo { get; }

    public RegistroLog(string pastaLog, MascaradorSenha mascarador, Func<DateTime>? agora = null)
    {
        _mascarador = mascarador;
        _agora = agora ?? (() => DateTime.Now);

        try
        {
            Directory.CreateDirectory(pastaLog);
            (CaminhoArquivo, var fluxo) = CriarArquivoNovo(pastaLog, _agora());
            _escritor = new StreamWriter(fluxo, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true))
            {
                AutoFlush = true,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or NotSupportedException)
        {
            throw new LogIndisponivelException(
                $"Não foi possível criar o arquivo de log na pasta {pastaLog}. Verifique se a pasta existe e se há permissão.",
                ex);
        }
    }

    public void Info(string mensagem) => Escrever("INFO ", mensagem);
    public void Aviso(string mensagem) => Escrever("AVISO", mensagem);
    public void Erro(string mensagem) => Escrever("ERRO ", mensagem);
    public void SaidaFerramenta(string ferramenta, string linha) => Escrever("SAIDA", $"{ferramenta}> {linha}");

    private void Escrever(string nivel, string mensagem)
    {
        var texto = _mascarador.Mascarar(mensagem);
        var linhas = texto.Replace("\r\n", "\n").Split('\n');

        lock (_trava)
        {
            var momento = _agora().ToString("yyyy-MM-dd HH:mm:ss.fff");
            foreach (var linha in linhas)
                _escritor.WriteLine($"{momento} [{nivel}] {linha}");
        }
    }

    private static (string caminho, FileStream fluxo) CriarArquivoNovo(string pasta, DateTime momento)
    {
        var baseNome = $"reparo_{momento:yyyyMMdd_HHmmss}";
        for (var tentativa = 1; tentativa <= 100; tentativa++)
        {
            var nome = tentativa == 1 ? $"{baseNome}.log" : $"{baseNome}_{tentativa}.log";
            var caminho = Path.Combine(pasta, nome);
            try
            {
                // CreateNew falha se o arquivo já existir: nunca sobrescreve.
                return (caminho, new FileStream(caminho, FileMode.CreateNew, FileAccess.Write, FileShare.Read));
            }
            catch (IOException) when (File.Exists(caminho))
            {
                // Já existe um log com esse nome; tenta o próximo sufixo.
            }
        }

        throw new IOException("Muitos arquivos de log com o mesmo horário.");
    }

    public void Dispose()
    {
        lock (_trava)
            _escritor.Dispose();
    }
}
