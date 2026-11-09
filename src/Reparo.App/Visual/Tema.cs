using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Reparo.App.Visual;

/// <summary>Cores da tela. Segue o tema claro/escuro do Windows (D-018).</summary>
internal sealed record Tema(
    bool Escuro,
    Color Fundo, Color Superficie, Color Borda, Color Texto, Color TextoSuave,
    Color Destaque, Color DestaqueHover, Color DestaqueTexto, Color Campo,
    Color Sucesso, Color SucessoFundo, Color Aviso, Color AvisoFundo, Color Falha, Color FalhaFundo)
{
    public static Tema Claro { get; } = new(
        Escuro: false,
        Fundo: Hex("F5F6F8"), Superficie: Hex("FFFFFF"), Borda: Hex("E2E5EA"),
        Texto: Hex("16181D"), TextoSuave: Hex("626A76"),
        Destaque: Hex("2563EB"), DestaqueHover: Hex("1D4ED8"), DestaqueTexto: Color.White, Campo: Hex("FFFFFF"),
        Sucesso: Hex("16A34A"), SucessoFundo: Hex("EDFBF2"),
        Aviso: Hex("D97706"), AvisoFundo: Hex("FFF7E8"),
        Falha: Hex("DC2626"), FalhaFundo: Hex("FDEEEE"));

    public static Tema EscuroPadrao { get; } = new(
        Escuro: true,
        Fundo: Hex("111317"), Superficie: Hex("1A1D23"), Borda: Hex("2B3038"),
        Texto: Hex("ECEEF2"), TextoSuave: Hex("98A0AC"),
        Destaque: Hex("3B82F6"), DestaqueHover: Hex("5B95F8"), DestaqueTexto: Color.White, Campo: Hex("14171C"),
        Sucesso: Hex("22C55E"), SucessoFundo: Hex("13261B"),
        Aviso: Hex("F59E0B"), AvisoFundo: Hex("2A2312"),
        Falha: Hex("EF4444"), FalhaFundo: Hex("2C1618"));

    /// <summary>Lê "Modo dos aplicativos" do Windows (Configurações &gt; Personalização &gt; Cores).</summary>
    public static Tema DoWindows()
    {
        try
        {
            using var chave = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return chave?.GetValue("AppsUseLightTheme") is int claro && claro == 0 ? EscuroPadrao : Claro;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return Claro;
        }
    }

    /// <summary>Logo da PBtec deste tema (colorido no claro, branco/vermelho no escuro). Nulo se não achar.</summary>
    public Image? Logo() => Logos.Carregar(Escuro ? "logo-escuro.png" : "logo-claro.png");

    private static Color Hex(string rgb) => Color.FromArgb(
        Convert.ToInt32(rgb[..2], 16), Convert.ToInt32(rgb[2..4], 16), Convert.ToInt32(rgb[4..], 16));

    /// <summary>Barra de título escura no Windows 10 (20H1+) e 11. Sem efeito nas versões antigas.</summary>
    public void AplicarNaBarraDeTitulo(IntPtr janela)
    {
        if (!Escuro)
            return;
        var ligado = 1;
        if (DwmSetWindowAttribute(janela, 20, ref ligado, sizeof(int)) != 0)
            DwmSetWindowAttribute(janela, 19, ref ligado, sizeof(int)); // Windows 10 antes do 20H1
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int atributo, ref int valor, int tamanho);
}

/// <summary>Fontes da tela. Ícones vêm da fonte do próprio Windows (sem arquivos extras).</summary>
internal static class Fontes
{
    public static readonly Font Normal = new("Segoe UI", 10f);
    public static readonly Font Pequena = new("Segoe UI", 9f);
    public static readonly Font Rotulo = new("Segoe UI Semibold", 9.5f);
    public static readonly Font Subtitulo = new("Segoe UI Semibold", 11.5f);
    public static readonly Font Titulo = new("Segoe UI Semibold", 17f);
    public static readonly Font Botao = new("Segoe UI Semibold", 10.5f);

    private static readonly string? FamiliaIcones = AcharFamiliaIcones();

    public static Font? Icones(float tamanho) => FamiliaIcones is null ? null : new Font(FamiliaIcones, tamanho);

    private static string? AcharFamiliaIcones()
    {
        using var instaladas = new InstalledFontCollection();
        var nomes = instaladas.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" }.FirstOrDefault(nomes.Contains);
    }
}

/// <summary>Códigos dos ícones da fonte Segoe MDL2 Assets / Segoe Fluent Icons.</summary>
internal static class Icones
{
    public const string Reparo = "";
    public const string Pasta = "";
    public const string Info = "";
    public const string Cadeado = "";
    public const string Banco = "";
}

/// <summary>Logos embutidos no .exe (Recursos\*.png). Carregados uma vez só.</summary>
internal static class Logos
{
    private static readonly Dictionary<string, Image?> Cache = new();

    public static Image? Carregar(string nome)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(nome, out var pronto))
                return pronto;
            using var fluxo = typeof(Logos).Assembly.GetManifestResourceStream("Reparo.App." + nome);
            Image? imagem = null;
            if (fluxo is not null)
            {
                using var lida = Image.FromStream(fluxo);
                imagem = new Bitmap(lida); // cópia: não depende do fluxo depois de fechado
            }
            Cache[nome] = imagem;
            return imagem;
        }
    }
}

internal static class Desenho
{
    public static GraphicsPath Arredondado(RectangleF r, float raio)
    {
        var d = Math.Min(raio * 2, Math.Min(r.Width, r.Height));
        var caminho = new GraphicsPath();
        if (d <= 0)
        {
            caminho.AddRectangle(r);
            return caminho;
        }
        caminho.AddArc(r.X, r.Y, d, d, 180, 90);
        caminho.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        caminho.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        caminho.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        caminho.CloseFigure();
        return caminho;
    }

    public static void Suave(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
    }

    /// <summary>Escala de DPI do controle (1 = 96 dpi).</summary>
    public static float Escala(Control c) => c.DeviceDpi / 96f;

    /// <summary>Círculo de status: ✓ verde, ! amarelo, ✕ vermelho ou anel azul (em andamento).</summary>
    public static void IconeStatus(Graphics g, RectangleF r, EstadoLinha estado, Tema tema, Color fundo)
    {
        var cor = estado switch
        {
            EstadoLinha.Ok => tema.Sucesso,
            EstadoLinha.Aviso => tema.Aviso,
            EstadoLinha.Falha => tema.Falha,
            EstadoLinha.Pendente => tema.Borda,
            _ => tema.Destaque,
        };

        if (estado is EstadoLinha.Andamento or EstadoLinha.Pendente)
        {
            using var anel = new Pen(cor, r.Width * 0.12f);
            var dentro = RectangleF.Inflate(r, -r.Width * 0.06f, -r.Width * 0.06f);
            using (var limpa = new SolidBrush(fundo))
                g.FillEllipse(limpa, dentro);
            g.DrawEllipse(anel, dentro);
            if (estado == EstadoLinha.Andamento)
            {
                using var ponto = new SolidBrush(cor);
                g.FillEllipse(ponto, RectangleF.Inflate(r, -r.Width * 0.3f, -r.Width * 0.3f));
            }
            return;
        }

        using (var pincel = new SolidBrush(cor))
            g.FillEllipse(pincel, r);

        using var traco = new Pen(Color.White, r.Width * 0.12f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        float X(float f) => r.X + r.Width * f;
        float Y(float f) => r.Y + r.Height * f;
        switch (estado)
        {
            case EstadoLinha.Ok:
                g.DrawLines(traco, new[] { new PointF(X(0.29f), Y(0.52f)), new PointF(X(0.44f), Y(0.66f)), new PointF(X(0.71f), Y(0.37f)) });
                break;
            case EstadoLinha.Aviso:
                g.DrawLine(traco, X(0.5f), Y(0.28f), X(0.5f), Y(0.56f));
                using (var ponto = new SolidBrush(Color.White))
                    g.FillEllipse(ponto, X(0.43f), Y(0.66f), r.Width * 0.14f, r.Height * 0.14f);
                break;
            case EstadoLinha.Falha:
                g.DrawLine(traco, X(0.34f), Y(0.34f), X(0.66f), Y(0.66f));
                g.DrawLine(traco, X(0.66f), Y(0.34f), X(0.34f), Y(0.66f));
                break;
        }
    }
}

internal enum EstadoLinha { Pendente, Andamento, Ok, Aviso, Falha }
