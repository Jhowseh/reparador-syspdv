using Microsoft.Win32;

namespace Reparo.Core;

/// <summary>O que foi descoberto sobre o Firebird instalado. Campos nulos = não encontrado.</summary>
public sealed record InstalacaoFirebird(string? PastaBin, string? Processo, string? Servico)
{
    public static readonly InstalacaoFirebird NaoEncontrada = new(null, null, null);
}

/// <summary>Descobre onde o Firebird está instalado, para o operador não precisar configurar nada.</summary>
public interface IDetectorFirebird
{
    InstalacaoFirebird Detectar(string servicoPreferido);
}

/// <summary>
/// Lê o registro do Windows (somente leitura): o caminho do executável do serviço do Firebird e,
/// se não achar, a pasta da instalação padrão registrada pelo instalador do Firebird.
/// </summary>
public sealed class DetectorFirebirdWindows : IDetectorFirebird
{
    private const string ChaveServicos = @"SYSTEM\CurrentControlSet\Services";
    private const string ChaveInstancias = @"SOFTWARE\Firebird Project\Firebird Server\Instances";

    public InstalacaoFirebird Detectar(string servicoPreferido)
    {
        try
        {
            var servico = ServicoExiste(servicoPreferido) ? servicoPreferido : ProcurarServico();
            var exe = servico is null ? null : ExecutavelDoServico(servico);

            var pastaBin = exe is not null ? Path.GetDirectoryName(exe) : PastaBinDaInstalacao();
            if (pastaBin is not null && !File.Exists(Path.Combine(pastaBin, "gbak.exe")))
                pastaBin = PastaBinDaInstalacao();

            return new InstalacaoFirebird(pastaBin, exe is null ? null : Path.GetFileName(exe), servico);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return InstalacaoFirebird.NaoEncontrada;
        }
    }

    private static bool ServicoExiste(string nome)
    {
        using var chave = Registry.LocalMachine.OpenSubKey($@"{ChaveServicos}\{nome}");
        return chave is not null;
    }

    /// <summary>Serviço do servidor Firebird (nunca o Guardian).</summary>
    private static string? ProcurarServico()
    {
        using var servicos = Registry.LocalMachine.OpenSubKey(ChaveServicos);
        return servicos?.GetSubKeyNames()
            .Where(n => n.StartsWith("Firebird", StringComparison.OrdinalIgnoreCase)
                        && n.Contains("Server", StringComparison.OrdinalIgnoreCase)
                        && !n.Contains("Guardian", StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static string? ExecutavelDoServico(string servico)
    {
        using var chave = Registry.LocalMachine.OpenSubKey($@"{ChaveServicos}\{servico}");
        var caminho = ExtrairExecutavel(chave?.GetValue("ImagePath") as string);
        return caminho is not null && File.Exists(caminho) ? caminho : null;
    }

    private static string? PastaBinDaInstalacao()
    {
        foreach (var visao in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var raiz = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, visao);
            using var chave = raiz.OpenSubKey(ChaveInstancias);
            if (chave?.GetValue("DefaultInstance") is string pasta && !string.IsNullOrWhiteSpace(pasta))
            {
                var bin = Path.Combine(pasta.Trim(), "bin");
                if (File.Exists(Path.Combine(bin, "gbak.exe")))
                    return bin;
            }
        }

        return null;
    }

    /// <summary>
    /// Tira o caminho do .exe da linha do serviço, ex.:
    /// <c>"C:\Program Files\Firebird\Firebird_2_5\bin\fbserver.exe" -s DefaultInstance</c>.
    /// </summary>
    internal static string? ExtrairExecutavel(string? linhaServico)
    {
        if (string.IsNullOrWhiteSpace(linhaServico))
            return null;

        var linha = Environment.ExpandEnvironmentVariables(linhaServico.Trim());
        if (linha.StartsWith('"'))
        {
            var fim = linha.IndexOf('"', 1);
            return fim > 1 ? linha[1..fim] : null;
        }

        var posExe = linha.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return posExe > 0 ? linha[..(posExe + 4)] : null;
    }
}
