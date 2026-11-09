using System.Security.Cryptography;

namespace Reparo.Core;

/// <summary>Um arquivo que vem dentro do ReparoSysPDV.exe.</summary>
public sealed record ArquivoEmbutido(string Nome, byte[] Conteudo)
{
    public string Hash { get; } = Convert.ToHexString(SHA256.HashData(Conteudo));
}

/// <summary>
/// Programas que o reparo leva dentro do .exe e coloca na pasta do SysPDV (D-015, D-016):
/// gfix, gbak, fbclient.dll e firebird.msg do Firebird 2.5.1 (pasta terceiros\firebird\ do repositório) e
/// VerificaIndice e dbMonitor do SysPDV (pasta terceiros\syspdv\). Sempre os mesmos, para qualquer loja.
/// </summary>
public static class ProgramasDoReparo
{
    public const string VersaoFirebird = "2.5.1.26351";

    /// <summary>A ordem importa: o fbclient.dll vai primeiro; se ele estiver preso, nada mais é trocado.</summary>
    public static readonly IReadOnlyList<string> Nomes = new[]
    {
        "fbclient.dll", "gbak.exe", "gfix.exe", "firebird.msg", "VerificaIndice.exe", "dbMonitor.exe",
    };

    private const string Prefixo = "Reparo.Core.Programas.";

    /// <summary>Lê os arquivos embutidos. Se algum faltar (pacote mal gerado), ele simplesmente não vem na lista.</summary>
    public static IReadOnlyList<ArquivoEmbutido> Embutidos()
    {
        var assembly = typeof(ProgramasDoReparo).Assembly;
        var arquivos = new List<ArquivoEmbutido>();
        foreach (var nome in Nomes)
        {
            using var recurso = assembly.GetManifestResourceStream(Prefixo + nome);
            if (recurso is null)
                continue;

            using var memoria = new MemoryStream();
            recurso.CopyTo(memoria);
            arquivos.Add(new ArquivoEmbutido(nome, memoria.ToArray()));
        }
        return arquivos;
    }
}
