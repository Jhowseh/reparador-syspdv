namespace Reparo.Core;

public static class ModoExecucao
{
    public const string ArgumentoNaoInterativo = "--nao-interativo";
    public const string VariavelNaoInterativo = "REPARO_NAO_INTERATIVO";

    /// <summary>
    /// Modo não interativo quando a variável REPARO_NAO_INTERATIVO vale "1"
    /// ou quando o argumento --nao-interativo é passado.
    /// </summary>
    public static bool EhNaoInterativo(IEnumerable<string> argumentos, string? valorVariavel)
    {
        if (string.Equals(valorVariavel?.Trim(), "1", StringComparison.Ordinal))
            return true;

        return argumentos.Any(a => string.Equals(a, ArgumentoNaoInterativo, StringComparison.OrdinalIgnoreCase));
    }
}
