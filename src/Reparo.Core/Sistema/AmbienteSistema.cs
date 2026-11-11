using System.Security.Principal;

namespace Reparo.Core;

/// <summary>Consultas ao Windows (separadas para os testes poderem simular).</summary>
public interface IAmbienteSistema
{
    bool EhAdministrador();

    /// <summary>Espaço livre (bytes) no disco da pasta, ou nulo se não der para saber.</summary>
    long? EspacoLivreBytes(string pasta);

    /// <summary>
    /// O runtime do Visual C++ 2005 (32 bits) que o gfix/gbak/fbclient 2.5 precisam está disponível?
    /// Na dúvida responde sim (o próprio gfix acusa se faltar).
    /// </summary>
    bool TemRuntimeVisualC2005x86(string pastaPrograma);

    /// <summary>
    /// Espera até não haver mais nenhum processo com esse nome (ex.: "fbserver.exe").
    /// Verdadeiro se ele fechou (ou nem estava rodando) dentro do limite.
    /// </summary>
    bool EsperarProcessoTerminar(string nomeProcesso, TimeSpan limite);
}

public sealed class AmbienteWindows : IAmbienteSistema
{
    public bool EhAdministrador()
    {
        using var identidade = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identidade).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public long? EspacoLivreBytes(string pasta)
    {
        try
        {
            var raiz = Path.GetPathRoot(Path.GetFullPath(pasta));
            if (string.IsNullOrEmpty(raiz) || raiz.StartsWith(@"\\", StringComparison.Ordinal))
                return null; // pasta de rede: não dá para conferir de forma confiável
            return new DriveInfo(raiz).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public bool TemRuntimeVisualC2005x86(string pastaPrograma)
    {
        // Ao lado do programa (como no kit zip do Firebird) ...
        if (File.Exists(Path.Combine(pastaPrograma, "msvcr80.dll"))
            && File.Exists(Path.Combine(pastaPrograma, "Microsoft.VC80.CRT.manifest")))
            return true;

        // ... ou instalado no Windows (o instalador do Firebird 2.5 faz isso).
        try
        {
            var winSxS = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "WinSxS");
            if (!Directory.Exists(winSxS))
                return true; // não dá para conferir
            return Directory.EnumerateDirectories(winSxS, "x86_microsoft.vc80.crt_1fc8b3b9a1e18e3b_*").Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    public bool EsperarProcessoTerminar(string nomeProcesso, TimeSpan limite)
    {
        // GetProcessesByName quer o nome sem ".exe".
        var nome = nomeProcesso.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? nomeProcesso[..^4] : nomeProcesso;
        var fim = DateTime.UtcNow + limite;
        while (true)
        {
            var processos = System.Diagnostics.Process.GetProcessesByName(nome);
            var rodando = processos.Length > 0;
            foreach (var p in processos)
                p.Dispose();

            if (!rodando)
                return true;
            if (DateTime.UtcNow >= fim)
                return false;
            Thread.Sleep(250);
        }
    }
}
