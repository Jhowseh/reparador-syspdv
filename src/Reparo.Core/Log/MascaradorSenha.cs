using System.Text.RegularExpressions;

namespace Reparo.Core;

/// <summary>
/// Esconde senhas em qualquer texto que vá para log ou tela.
/// Troca (1) cada senha conhecida e (2) o valor depois de -pa/-pas/-password e ISC_PASSWORD=.
/// </summary>
public sealed class MascaradorSenha
{
    public const string Mascara = "********";

    // gfix/gbak aceitam abreviações da opção de senha a partir de "-pa" (o "-p" do gbak é outra coisa).
    private static readonly Regex OpcaoSenha = new(
        @"(?<opcao>(?<![\w-])-pa\w*)(?<espaco>\s+)(?<valor>""[^""]*""|\S+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex VariavelSenha = new(
        @"(?<nome>ISC_PASSWORD\s*=\s*)(?<valor>\S+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly string[] _segredos;

    public MascaradorSenha(params string?[] segredos)
    {
        // As mais longas primeiro, para uma senha que contém outra ser trocada inteira.
        _segredos = segredos
            .Where(s => !string.IsNullOrEmpty(s))
            .Select(s => s!)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(s => s.Length)
            .ToArray();
    }

    public string Mascarar(string? texto)
    {
        if (string.IsNullOrEmpty(texto))
            return texto ?? string.Empty;

        var resultado = texto;
        foreach (var segredo in _segredos)
            resultado = resultado.Replace(segredo, Mascara, StringComparison.Ordinal);

        resultado = OpcaoSenha.Replace(resultado, m => m.Groups["opcao"].Value + m.Groups["espaco"].Value + Mascara);
        resultado = VariavelSenha.Replace(resultado, m => m.Groups["nome"].Value + Mascara);
        return resultado;
    }
}
