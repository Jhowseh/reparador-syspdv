using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Reparo.App.Visual;

/// <summary>
/// Animação curta na thread da tela: chama <c>quadro</c> com o progresso (0 a 1, desacelerando no fim)
/// e <c>fim</c> ao terminar. Começar outra ou parar conclui a anterior na hora.
/// </summary>
internal sealed class Animacao : IDisposable
{
    private readonly System.Windows.Forms.Timer _relogio = new() { Interval = 15 };
    private readonly Stopwatch _tempo = new();
    private int _duracaoMs;
    private Action<float>? _quadro;
    private Action? _fim;

    public Animacao() => _relogio.Tick += (_, _) => Avancar();

    /// <summary>Falso quando o Windows está com as animações desligadas (Desempenho / Acessibilidade).</summary>
    public static bool Ligadas => SystemInformation.UIEffectsEnabled;

    public void Iniciar(int duracaoMs, Action<float> quadro, Action? fim = null)
    {
        Parar();
        _duracaoMs = Math.Max(1, duracaoMs);
        _quadro = quadro;
        _fim = fim;
        _tempo.Restart();
        quadro(0);
        _relogio.Start();
    }

    /// <summary>Pula para o fim da animação em andamento (se houver).</summary>
    public void Parar()
    {
        if (_quadro is null)
            return;
        _relogio.Stop();
        var (quadro, fim) = (_quadro, _fim);
        _quadro = null;
        _fim = null;
        quadro(1);
        fim?.Invoke();
    }

    private void Avancar()
    {
        if (_quadro is null)
            return;
        var t = Math.Min(1f, (float)_tempo.Elapsed.TotalMilliseconds / _duracaoMs);
        if (t >= 1)
        {
            Parar();
            return;
        }
        _quadro(1 - MathF.Pow(1 - t, 3)); // desacelera no fim
    }

    public void Dispose()
    {
        _relogio.Stop();
        _relogio.Dispose();
    }
}

/// <summary>Base dos controles desenhados à mão: sem piscar e com o fundo do controle de trás.</summary>
internal abstract class ControleDesenhado : Control
{
    protected ControleDesenhado()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
    }

    protected Color FundoDeTras => Parent?.BackColor ?? BackColor;
}

/// <summary>Caixa com cantos arredondados e borda fina. Opcional: faixa colorida à esquerda.</summary>
internal sealed class Cartao : Panel
{
    public Cartao()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw, true);
        Padding = new Padding(18);
    }

    public Color CorFundo { get; set; } = Color.White;
    public Color CorBorda { get; set; } = Color.Gainsboro;
    public Color? CorFaixa { get; set; }
    public float Raio { get; set; } = 10;

    /// <summary>Altura = conteúdo (com texto quebrado na largura disponível) + espaçamento interno.</summary>
    public override Size GetPreferredSize(Size proposedSize)
    {
        var largura = proposedSize.Width is > 0 and < int.MaxValue / 2 ? proposedSize.Width : Width;
        var interna = Math.Max(1, largura - Padding.Horizontal);
        var altura = Padding.Vertical;
        foreach (Control filho in Controls)
        {
            if (filho.Visible)
                altura += filho.GetPreferredSize(new Size(interna, 0)).Height + filho.Margin.Vertical;
        }
        return new Size(largura, altura);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        using (var fundoDeTras = new SolidBrush(Parent?.BackColor ?? BackColor))
            g.FillRectangle(fundoDeTras, ClientRectangle);

        Desenho.Suave(g);
        var escala = DeviceDpi / 96f;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using var caminho = Desenho.Arredondado(r, Raio * escala);
        using (var fundo = new SolidBrush(CorFundo))
            g.FillPath(fundo, caminho);

        if (CorFaixa is { } faixa)
        {
            var estado = g.Save();
            g.SetClip(caminho);
            using var pincel = new SolidBrush(faixa);
            g.FillRectangle(pincel, 0, 0, 4 * escala, Height);
            g.Restore(estado);
        }

        using var borda = new Pen(CorBorda, 1);
        g.DrawPath(borda, caminho);
    }
}

/// <summary>Botão arredondado. Primário = azul cheio; secundário = contorno.</summary>
internal sealed class Botao : ControleDesenhado, IButtonControl
{
    private bool _sobre;
    private bool _apertado;
    private Tema _tema = Tema.Claro;

    public Botao()
    {
        SetStyle(ControlStyles.Selectable | ControlStyles.StandardClick, true);
        Cursor = Cursors.Hand;
        Font = Fontes.Botao;
        TabStop = true;
    }

    public bool Primario { get; set; }
    public string? Icone { get; set; }
    public DialogResult DialogResult { get; set; }

    public Tema Tema
    {
        get => _tema;
        set { _tema = value; Invalidate(); }
    }

    public void NotifyDefault(bool value) { }

    public void PerformClick()
    {
        if (Enabled)
            OnClick(EventArgs.Empty);
    }

    protected override void OnMouseEnter(EventArgs e) { _sobre = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _sobre = _apertado = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _apertado = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _apertado = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            PerformClick();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        using (var tras = new SolidBrush(FundoDeTras))
            g.FillRectangle(tras, ClientRectangle);
        Desenho.Suave(g);

        var escala = Desenho.Escala(this);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using var caminho = Desenho.Arredondado(r, 8 * escala);

        Color fundo, texto, borda;
        if (Primario)
        {
            fundo = !Enabled ? Misturar(_tema.Destaque, _tema.Fundo, 0.55f)
                  : _apertado || _sobre ? _tema.DestaqueHover : _tema.Destaque;
            texto = Enabled ? _tema.DestaqueTexto : Misturar(_tema.DestaqueTexto, fundo, 0.3f);
            borda = fundo;
        }
        else
        {
            fundo = _apertado ? Misturar(_tema.Borda, _tema.Superficie, 0.5f)
                  : _sobre && Enabled ? Misturar(_tema.Borda, _tema.Superficie, 0.7f)
                  : _tema.Superficie;
            texto = Enabled ? _tema.Texto : _tema.TextoSuave;
            borda = _tema.Borda;
        }

        using (var pincel = new SolidBrush(fundo))
            g.FillPath(pincel, caminho);
        using (var caneta = new Pen(borda))
            g.DrawPath(caneta, caminho);

        if (Focused && ShowFocusCues)
        {
            using var foco = new Pen(_tema.Destaque, 2 * escala);
            using var anel = Desenho.Arredondado(RectangleF.Inflate(r, -3 * escala, -3 * escala), 6 * escala);
            if (Primario)
                foco.Color = Color.FromArgb(160, _tema.DestaqueTexto);
            g.DrawPath(foco, anel);
        }

        var tamanhoTexto = TextRenderer.MeasureText(g, Text, Font);
        using var fonteIcone = Icone is null ? null : Fontes.Icones(Font.SizeInPoints);
        var larguraIcone = fonteIcone is null ? 0 : TextRenderer.MeasureText(g, Icone, fonteIcone).Width;
        var espaco = fonteIcone is null ? 0 : (int)(4 * escala);
        var x = (Width - (larguraIcone + espaco + tamanhoTexto.Width)) / 2;
        if (fonteIcone is not null)
        {
            TextRenderer.DrawText(g, Icone, fonteIcone, new Rectangle(x, 0, larguraIcone, Height), texto,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        TextRenderer.DrawText(g, Text, Font, new Rectangle(x + larguraIcone + espaco, 0, tamanhoTexto.Width, Height), texto,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    internal static Color Misturar(Color a, Color b, float pesoDeA) => Color.FromArgb(
        (int)(a.R * pesoDeA + b.R * (1 - pesoDeA)),
        (int)(a.G * pesoDeA + b.G * (1 - pesoDeA)),
        (int)(a.B * pesoDeA + b.B * (1 - pesoDeA)));
}

/// <summary>Campo de texto com borda arredondada (azul quando em foco, vermelha com erro) e ícone à esquerda.</summary>
internal sealed class CampoTexto : ControleDesenhado
{
    private Tema _tema = Tema.Claro;
    private bool _erro;

    public CampoTexto()
    {
        Caixa = new TextBox { BorderStyle = BorderStyle.None, Font = Fontes.Normal };
        Caixa.GotFocus += (_, _) => Invalidate();
        Caixa.LostFocus += (_, _) => Invalidate();
        Caixa.TextChanged += (_, _) => Erro = false;
        Controls.Add(Caixa);
        Height = 40;
        Cursor = Cursors.IBeam;
    }

    public TextBox Caixa { get; }
    public string? Icone { get; set; }

    public bool Erro
    {
        get => _erro;
        set { if (_erro != value) { _erro = value; Invalidate(); } }
    }

    public Tema Tema
    {
        get => _tema;
        set
        {
            _tema = value;
            Caixa.BackColor = value.Campo;
            Caixa.ForeColor = value.Texto;
            Invalidate();
        }
    }

    protected override void OnClick(EventArgs e) { Caixa.Focus(); base.OnClick(e); }
    protected override void OnEnabledChanged(EventArgs e) { Caixa.Enabled = Enabled; Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        var escala = Desenho.Escala(this);
        var esquerda = (int)((Icone is null ? 12 : 38) * escala);
        var direita = (int)(12 * escala);
        Caixa.SetBounds(esquerda, (Height - Caixa.Height) / 2, Math.Max(10, Width - esquerda - direita), Caixa.Height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        using (var tras = new SolidBrush(FundoDeTras))
            g.FillRectangle(tras, ClientRectangle);
        Desenho.Suave(g);

        var escala = Desenho.Escala(this);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using var caminho = Desenho.Arredondado(r, 8 * escala);
        using (var fundo = new SolidBrush(_tema.Campo))
            g.FillPath(fundo, caminho);

        var corBorda = _erro ? _tema.Falha : Caixa.Focused ? _tema.Destaque : _tema.Borda;
        using (var borda = new Pen(corBorda, Caixa.Focused || _erro ? 1.6f * escala : 1f))
            g.DrawPath(borda, caminho);

        if (Icone is not null && Fontes.Icones(11f) is { } fonte)
        {
            using (fonte)
                TextRenderer.DrawText(g, Icone, fonte, new Rectangle((int)(12 * escala), 0, (int)(20 * escala), Height),
                    _tema.TextoSuave, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
        }
    }
}

/// <summary>Barra de progresso fina e arredondada.</summary>
internal sealed class BarraProgresso : ControleDesenhado
{
    private int _valor;
    private int _maximo = 1;

    public Tema Tema { get; set; } = Tema.Claro;

    public int Maximo
    {
        get => _maximo;
        set { _maximo = Math.Max(1, value); Invalidate(); }
    }

    public int Valor
    {
        get => _valor;
        set { _valor = Math.Clamp(value, 0, _maximo); Invalidate(); }
    }

    public Color? CorFixa { get; set; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        using (var tras = new SolidBrush(FundoDeTras))
            g.FillRectangle(tras, ClientRectangle);
        Desenho.Suave(g);

        var r = new RectangleF(0, 0, Width - 1, Height - 1);
        using (var trilho = new SolidBrush(Tema.Borda))
        using (var caminho = Desenho.Arredondado(r, r.Height / 2))
            g.FillPath(trilho, caminho);

        var largura = r.Width * _valor / _maximo;
        if (largura < 1)
            return;
        using var cheio = new SolidBrush(CorFixa ?? Tema.Destaque);
        using var parte = Desenho.Arredondado(new RectangleF(0, 0, Math.Max(largura, r.Height), r.Height), r.Height / 2);
        g.FillPath(cheio, parte);
    }
}

/// <summary>Lista das etapas em forma de linha do tempo. Mostra sempre a etapa atual (roda o mouse para ver as anteriores).</summary>
internal sealed class ListaEtapas : ControleDesenhado
{
    private readonly List<(string Texto, EstadoLinha Estado)> _linhas = new();
    private int _rolagem;

    public Tema Tema { get; set; } = Tema.Claro;

    public int Quantidade => _linhas.Count;

    private int AlturaLinha => (int)(32 * Desenho.Escala(this));

    public void Limpar()
    {
        _linhas.Clear();
        _rolagem = 0;
        Invalidate();
    }

    public void Adicionar(string texto)
    {
        _linhas.Add((texto, EstadoLinha.Andamento));
        IrParaOFim();
    }

    public void Marcar(int indice, EstadoLinha estado)
    {
        if (indice < 0 || indice >= _linhas.Count)
            return;
        _linhas[indice] = (_linhas[indice].Texto, estado);
        Invalidate();
    }

    private int RolagemMaxima => Math.Max(0, _linhas.Count * AlturaLinha - Height);

    private void IrParaOFim()
    {
        _rolagem = RolagemMaxima;
        _noFim = true;
        Invalidate();
    }

    private bool _noFim = true;

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        // Se estava mostrando a etapa atual, continua mostrando (ex.: o quadro de resultado diminuiu a lista).
        _rolagem = _noFim ? RolagemMaxima : Math.Min(_rolagem, RolagemMaxima);
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        _rolagem = Math.Clamp(_rolagem - e.Delta / 120 * AlturaLinha, 0, RolagemMaxima);
        _noFim = _rolagem >= RolagemMaxima;
        Invalidate();
        base.OnMouseWheel(e);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        Focus(); // para a roda do mouse funcionar
        base.OnMouseEnter(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var fundo = FundoDeTras;
        using (var tras = new SolidBrush(fundo))
            g.FillRectangle(tras, ClientRectangle);
        Desenho.Suave(g);

        var escala = Desenho.Escala(this);
        var altura = AlturaLinha;
        var icone = 18 * escala;
        var centroX = 2 * escala + icone / 2;

        using var linhaDoTempo = new Pen(Tema.Borda, 2 * escala);
        for (var i = 0; i < _linhas.Count; i++)
        {
            var topo = i * altura - _rolagem;
            if (topo + altura < 0 || topo > Height)
                continue;

            var meio = topo + altura / 2f;
            if (i < _linhas.Count - 1)
                g.DrawLine(linhaDoTempo, centroX, meio + icone / 2, centroX, meio + altura - icone / 2);

            var (texto, estado) = _linhas[i];
            Desenho.IconeStatus(g, new RectangleF(centroX - icone / 2, meio - icone / 2, icone, icone), estado, Tema, fundo);

            var fonte = estado == EstadoLinha.Andamento ? Fontes.Rotulo : Fontes.Normal;
            var cor = estado == EstadoLinha.Andamento ? Tema.Texto
                    : estado == EstadoLinha.Falha ? Tema.Falha
                    : Tema.TextoSuave;
            var x = (int)(centroX + icone / 2 + 12 * escala);
            TextRenderer.DrawText(g, texto, fonte, new Rectangle(x, topo, Width - x, altura), cor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        // Esmaece o topo quando há etapas escondidas acima.
        if (_rolagem > 0)
        {
            var faixa = new Rectangle(0, 0, Width, (int)(28 * escala));
            using var degrade = new LinearGradientBrush(faixa, fundo, Color.FromArgb(0, fundo), LinearGradientMode.Vertical);
            g.FillRectangle(degrade, faixa);
        }
    }
}

/// <summary>Imagem (logo) ajustada ao tamanho do controle, sem distorcer e com boa qualidade.</summary>
internal sealed class ImagemLogo : ControleDesenhado
{
    public Image? Imagem { get; set; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        using (var tras = new SolidBrush(FundoDeTras))
            g.FillRectangle(tras, ClientRectangle);
        if (Imagem is null || Width <= 0 || Height <= 0)
            return;

        var proporcao = Math.Min((float)Width / Imagem.Width, (float)Height / Imagem.Height);
        var w = Imagem.Width * proporcao;
        var h = Imagem.Height * proporcao;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(Imagem, new RectangleF((Width - w) / 2, (Height - h) / 2, w, h));
    }
}

/// <summary>Abas no topo da janela ("Reparo", "Sobre"): texto com sublinhado azul na aba aberta.</summary>
internal sealed class BarraAbas : ControleDesenhado
{
    private readonly List<string> _abas = new();
    private readonly Animacao _animacao = new();
    private int _selecionada;
    private int _sobre = -1;
    private RectangleF? _sublinhado; // posição do sublinhado durante a animação (nulo = na aba aberta)

    public BarraAbas()
    {
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        Font = Fontes.Botao;
        AccessibleRole = AccessibleRole.PageTabList;
    }

    public Tema Tema { get; set; } = Tema.Claro;

    /// <summary>Aba aberta mudou: (índice novo, se deve animar).</summary>
    public event Action<int, bool>? SelecionadaMudou;

    public void Adicionar(string titulo)
    {
        _abas.Add(titulo);
        Invalidate();
    }

    public int Selecionada
    {
        get => _selecionada;
        set => Selecionar(value, animar: true);
    }

    /// <summary>Abre uma aba; com <paramref name="animar"/>, o sublinhado desliza até ela.</summary>
    public void Selecionar(int indice, bool animar)
    {
        indice = Math.Clamp(indice, 0, Math.Max(0, _abas.Count - 1));
        if (indice == _selecionada)
            return;

        var de = _sublinhado ?? Sublinhado(_selecionada);
        _selecionada = indice;
        if (animar && Animacao.Ligadas && IsHandleCreated)
        {
            _animacao.Iniciar(220, t =>
            {
                var para = Sublinhado(_selecionada);
                _sublinhado = new RectangleF(
                    de.X + (para.X - de.X) * t, para.Y, de.Width + (para.Width - de.Width) * t, para.Height);
                Invalidate();
            }, () => { _sublinhado = null; Invalidate(); });
        }
        else
        {
            _animacao.Parar();
        }

        Invalidate();
        SelecionadaMudou?.Invoke(indice, animar);
    }

    private RectangleF Sublinhado(int indice)
    {
        var grossura = 3 * Desenho.Escala(this);
        var r = Retangulos()[indice];
        return new RectangleF(r.X, Height - grossura, r.Width, grossura);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _animacao.Dispose();
        base.Dispose(disposing);
    }

    private List<Rectangle> Retangulos()
    {
        var escala = Desenho.Escala(this);
        var folga = (int)(12 * escala);
        var x = 0;
        var lista = new List<Rectangle>();
        foreach (var aba in _abas)
        {
            var largura = TextRenderer.MeasureText(aba, Font, Size.Empty, TextFormatFlags.NoPadding).Width + 2 * folga;
            lista.Add(new Rectangle(x, 0, largura, Height));
            x += largura + (int)(4 * escala);
        }
        return lista;
    }

    private int AbaEm(Point p) => Retangulos().FindIndex(r => r.Contains(p));

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var aba = AbaEm(e.Location);
        Cursor = aba >= 0 ? Cursors.Hand : Cursors.Default;
        if (aba != _sobre) { _sobre = aba; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { _sobre = -1; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        if (AbaEm(e.Location) is var aba and >= 0)
            Selecionar(aba, animar: true);
        base.OnMouseClick(e);
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Left) { Selecionada--; e.Handled = true; }
        if (e.KeyCode == Keys.Right) { Selecionada++; e.Handled = true; }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        using (var tras = new SolidBrush(FundoDeTras))
            g.FillRectangle(tras, ClientRectangle);
        Desenho.Suave(g);

        var escala = Desenho.Escala(this);
        using (var linha = new Pen(Tema.Borda, 1))
            g.DrawLine(linha, 0, Height - 1, Width, Height - 1);

        var retangulos = Retangulos();
        for (var i = 0; i < _abas.Count; i++)
        {
            var r = retangulos[i];
            var aberta = i == _selecionada;
            var cor = aberta || i == _sobre ? Tema.Texto : Tema.TextoSuave;
            TextRenderer.DrawText(g, _abas[i], Font, new Rectangle(r.X, 0, r.Width, Height - (int)(3 * escala)), cor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            if (aberta && Focused && ShowFocusCues)
            {
                using var foco = new Pen(Tema.Destaque, 1.5f * escala);
                using var anel = Desenho.Arredondado(new RectangleF(r.X + 1, 2 * escala, r.Width - 2, Height - 8 * escala), 6 * escala);
                g.DrawPath(foco, anel);
            }
        }

        if (_abas.Count > 0)
        {
            var s = _sublinhado ?? Sublinhado(_selecionada);
            using var pincel = new SolidBrush(Tema.Destaque);
            using var caminho = Desenho.Arredondado(s, s.Height / 2);
            g.FillPath(pincel, caminho);
        }
    }
}

/// <summary>
/// Troca de página animada: fica por cima das páginas por um instante, mostrando a página antiga
/// sumindo e a nova entrando de lado (imagens das duas tiradas antes da troca).
/// </summary>
internal sealed class TransicaoPagina : ControleDesenhado
{
    private Bitmap? _antes;
    private Bitmap? _depois;
    private int _direcao = 1;

    public TransicaoPagina()
    {
        TabStop = false;
        Visible = false;
    }

    /// <summary>0 = só a página antiga; 1 = só a nova.</summary>
    public float Progresso { get; set; }

    /// <param name="direcao">1 = a nova vem da direita (aba à direita); -1 = da esquerda.</param>
    public void Preparar(Bitmap antes, Bitmap depois, int direcao)
    {
        Limpar();
        _antes = antes;
        _depois = depois;
        _direcao = direcao;
        Progresso = 0;
    }

    public void Limpar()
    {
        _antes?.Dispose();
        _depois?.Dispose();
        _antes = _depois = null;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        using (var tras = new SolidBrush(FundoDeTras))
            g.FillRectangle(tras, ClientRectangle);

        // A antiga some no começo; a nova aparece logo depois, deslizando de lado
        // (quase sem as duas ao mesmo tempo, para os textos não se misturarem).
        var deslize = 28 * Desenho.Escala(this);
        var p = Math.Clamp(Progresso, 0, 1);
        var saida = Math.Clamp(p / 0.3f, 0, 1);
        var entrada = Math.Clamp((p - 0.22f) / 0.78f, 0, 1);
        if (_antes is not null)
            DesenharComTransparencia(g, _antes, -_direcao * deslize * 0.4f * saida, 1 - saida);
        if (_depois is not null)
            DesenharComTransparencia(g, _depois, _direcao * deslize * (1 - entrada), entrada);
    }

    private static void DesenharComTransparencia(Graphics g, Image imagem, float x, float opacidade)
    {
        if (opacidade <= 0.01f)
            return;
        using var atributos = new ImageAttributes();
        atributos.SetColorMatrix(new ColorMatrix { Matrix33 = opacidade });
        var destino = new Rectangle((int)Math.Round(x), 0, imagem.Width, imagem.Height);
        g.DrawImage(imagem, destino, 0, 0, imagem.Width, imagem.Height, GraphicsUnit.Pixel, atributos);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Limpar();
        base.Dispose(disposing);
    }
}

/// <summary>Quadrado arredondado colorido com um ícone (cabeçalho e resultado).</summary>
internal sealed class SeloIcone : ControleDesenhado
{
    public string Icone { get; set; } = Icones.Reparo;
    public Color CorFundo { get; set; } = Color.RoyalBlue;
    public Color CorIcone { get; set; } = Color.White;
    public EstadoLinha? Estado { get; set; }
    public Tema Tema { get; set; } = Tema.Claro;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        using (var tras = new SolidBrush(FundoDeTras))
            g.FillRectangle(tras, ClientRectangle);
        Desenho.Suave(g);

        var escala = Desenho.Escala(this);
        if (Estado is { } estado)
        {
            var lado = Math.Min(Width, Height) - 2;
            Desenho.IconeStatus(g, new RectangleF((Width - lado) / 2f, (Height - lado) / 2f, lado, lado), estado, Tema, FundoDeTras);
            return;
        }

        using (var caminho = Desenho.Arredondado(new RectangleF(0, 0, Width - 1, Height - 1), 10 * escala))
        using (var pincel = new SolidBrush(CorFundo))
            g.FillPath(pincel, caminho);

        if (Fontes.Icones(Height * 0.38f / escala) is { } fonte)
        {
            using (fonte)
                TextRenderer.DrawText(g, Icone, fonte, ClientRectangle, CorIcone,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
}
