using System.Security.Cryptography;

namespace Reparo.Core;

internal static class Arquivos
{
    /// <summary>Se o arquivo já existe, acrescenta _2, _3... (nunca sobrescreve).</summary>
    public static string NomeLivre(string caminho)
    {
        if (!File.Exists(caminho))
            return caminho;

        var pasta = Path.GetDirectoryName(caminho)!;
        var nome = Path.GetFileNameWithoutExtension(caminho);
        var extensao = Path.GetExtension(caminho);
        for (var i = 2; ; i++)
        {
            var candidato = Path.Combine(pasta, $"{nome}_{i}{extensao}");
            if (!File.Exists(candidato))
                return candidato;
        }
    }

    public static string Hash(string caminho)
    {
        using var arquivo = File.OpenRead(caminho);
        return Convert.ToHexString(SHA256.HashData(arquivo));
    }
}
