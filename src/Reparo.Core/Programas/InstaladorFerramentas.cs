using System.Diagnostics;

namespace Reparo.Core;

/// <summary>
/// Coloca os programas embutidos (Firebird e SysPDV) na pasta do SysPDV (D-015, D-016).
/// Arquivo igual: não mexe. Arquivo diferente: o antigo é guardado (nunca apagado) e o novo entra.
/// O novo é gravado e conferido ANTES de tirar o antigo do lugar.
/// </summary>
public sealed class InstaladorFerramentas
{
    private readonly string _pastaDestino;
    private readonly string _pastaGuardar;
    private readonly IRegistro _registro;

    /// <param name="pastaGuardar">Para onde vão os arquivos substituídos (criada só se precisar).</param>
    public InstaladorFerramentas(string pastaDestino, string pastaGuardar, IRegistro registro)
    {
        _pastaDestino = pastaDestino;
        _pastaGuardar = pastaGuardar;
        _registro = registro;
    }

    public ResultadoEtapa Instalar(IReadOnlyList<ArquivoEmbutido> arquivos)
    {
        var faltando = ProgramasDoReparo.Nomes
            .Where(n => !arquivos.Any(a => a.Nome.Equals(n, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (faltando.Count > 0)
            return ResultadoEtapa.Falha($"o ReparoSysPDV.exe está incompleto (faltam {string.Join(", ", faltando)} dentro dele). " +
                                        "Gere o pacote de novo");

        var copiados = new List<string>();
        var substituidos = new List<string>();
        var iguais = 0;

        foreach (var arquivo in arquivos)
        {
            var destino = Path.Combine(_pastaDestino, arquivo.Nome);
            string? temporario = null;
            try
            {
                if (File.Exists(destino) && Arquivos.Hash(destino) == arquivo.Hash)
                {
                    iguais++;
                    _registro.Info($"{arquivo.Nome} já está na pasta do SysPDV, igual ao do reparador.");
                    continue;
                }

                temporario = Arquivos.NomeLivre(destino + ".reparo-novo");
                File.WriteAllBytes(temporario, arquivo.Conteudo);
                if (Arquivos.Hash(temporario) != arquivo.Hash)
                {
                    File.Delete(temporario);
                    return ResultadoEtapa.Falha($"a cópia de {arquivo.Nome} saiu diferente do original. O arquivo da pasta não foi trocado");
                }

                string? guardado = null;
                if (File.Exists(destino))
                {
                    Directory.CreateDirectory(_pastaGuardar);
                    guardado = Arquivos.NomeLivre(Path.Combine(_pastaGuardar, arquivo.Nome));
                    var versaoAntiga = Versao(destino);
                    File.Move(destino, guardado, overwrite: false);
                    _registro.Info($"{arquivo.Nome} que estava na pasta ({versaoAntiga}) foi guardado em: {guardado}");
                }

                try
                {
                    File.Move(temporario, destino, overwrite: false);
                    temporario = null;
                }
                catch (Exception) when (guardado is not null)
                {
                    File.Move(guardado, destino, overwrite: false); // devolve o antigo
                    throw;
                }

                _registro.Info($"{arquivo.Nome} do reparador colocado em: {destino}");
                (guardado is null ? copiados : substituidos).Add(arquivo.Nome);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                TentarApagar(temporario);
                _registro.Erro($"Não foi possível colocar {arquivo.Nome} em {_pastaDestino}: {ex.Message}");
                return ResultadoEtapa.Falha($"não foi possível colocar o {arquivo.Nome} na pasta do SysPDV. " +
                                            "Se o sistema SysPDV estiver aberto neste computador, feche-o e tente de novo");
            }
        }

        if (substituidos.Count > 0)
            return ResultadoEtapa.Aviso($"{string.Join(", ", substituidos)} eram de outra versão e foram trocados pelos do " +
                                        $"reparador; os anteriores estão em {_pastaGuardar}");

        return ResultadoEtapa.Ok(copiados.Count == 0
            ? "os arquivos já estavam certos"
            : $"copiado(s): {string.Join(", ", copiados)}");
    }

    private static string Versao(string caminho)
    {
        try
        {
            var versao = FileVersionInfo.GetVersionInfo(caminho).FileVersion;
            return string.IsNullOrWhiteSpace(versao) ? "sem versão" : "versão " + versao.Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "versão desconhecida";
        }
    }

    private void TentarApagar(string? temporario)
    {
        if (temporario is null || !File.Exists(temporario))
            return;
        try
        {
            File.Delete(temporario); // é a cópia que este reparo acabou de criar
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _registro.Aviso($"Não foi possível apagar a cópia temporária {temporario}: {ex.Message}");
        }
    }
}
