using Reparo.App.Visual;
using Reparo.Core;

namespace Reparo.App;

/// <summary>
/// Tela única para o operador da loja (D-018): escolhe o banco, digita a senha e inicia. Durante o
/// reparo, a mesma janela mostra o progresso e, no fim, o resultado.
/// Só mostra e pergunta; toda a lógica está no Reparo.Core. O reparo roda em segundo plano.
/// </summary>
internal sealed class JanelaPrincipal : Form
{
    public const string Titulo = "Reparo do banco de dados - SysPDV";

    private readonly Func<string, Configuracao> _carregarConfiguracao;
    private readonly Tema _tema;

    // Abas
    private readonly BarraAbas _abas;
    private readonly TableLayoutPanel _paginaReparo;
    private readonly Control _paginaSobre;
    private readonly Panel _paginas;
    private readonly TransicaoPagina _transicao;
    private readonly Animacao _animacaoPagina = new();

    // Entrada
    private readonly TableLayoutPanel _painelEntrada;
    private readonly CampoTexto _banco;
    private readonly Botao _procurar;
    private readonly CampoTexto _senha;
    private readonly Label _erroEntrada;
    private readonly Botao _iniciar;

    // Reparo
    private readonly TableLayoutPanel _painelReparo;
    private readonly Label _bancoEmReparo;
    private readonly Label _contador;
    private readonly Label _etapaAtual;
    private readonly BarraProgresso _barra;
    private readonly ListaEtapas _etapas;

    // Resultado
    private readonly Cartao _resultado;
    private readonly SeloIcone _seloResultado;
    private readonly Label _tituloResultado;
    private readonly Label _textoResultado;
    private readonly TextBox _caminhoLog;
    private readonly Botao _fechar;

    private bool _emAndamento;
    private int _primeiraEtapaDaLista = 1;

    /// <summary>Código de saída do app ao fechar a janela.</summary>
    public CodigoSaida Codigo { get; private set; } = CodigoSaida.CanceladoPeloOperador;

    /// <param name="bancoSugerido">Banco que aparece já preenchido (se existir).</param>
    /// <param name="carregarConfiguracao">Monta a configuração para o banco escolhido.</param>
    /// <param name="tema">Cores; nulo = segue o tema do Windows.</param>
    public JanelaPrincipal(string? bancoSugerido, Func<string, Configuracao> carregarConfiguracao, Tema? tema = null)
    {
        _carregarConfiguracao = carregarConfiguracao;
        _tema = tema ?? Tema.DoWindows();

        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = Titulo;
        Icon = IconeDoPrograma();
        Font = Fontes.Normal;
        BackColor = _tema.Fundo;
        ForeColor = _tema.Texto;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(560, 640);
        Size = new Size(620, 720);
        Padding = new Padding(28, 24, 28, 24);

        var raiz = Tabela(1);
        raiz.Dock = DockStyle.Fill;
        raiz.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // cabeçalho
        raiz.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // abas
        raiz.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // página aberta

        raiz.Controls.Add(Cabecalho(), 0, 0);

        _abas = new BarraAbas { Tema = _tema, Dock = DockStyle.Top, Height = 38, Margin = new Padding(0, 0, 0, 18) };
        _abas.Adicionar("Reparo");
        _abas.Adicionar("Sobre");
        _abas.SelecionadaMudou += MostrarPagina;
        raiz.Controls.Add(_abas, 0, 1);

        // Página "Reparo": entrada ou progresso, resultado e rodapé.
        _paginaReparo = Tabela(1);
        _paginaReparo.Dock = DockStyle.Fill;
        _paginaReparo.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // entrada ou progresso
        _paginaReparo.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // resultado
        _paginaReparo.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // rodapé

        // ---------------------------------------------------------------- entrada
        _painelEntrada = Tabela(1);
        _painelEntrada.Dock = DockStyle.Fill;

        _painelEntrada.Controls.Add(Aviso(
            "Feche o sistema em todos os caixas antes de começar. O reparo leva alguns minutos e " +
            "faz uma cópia de segurança antes de mexer no banco."));

        var formulario = NovoCartao();
        var campos = Tabela(2, _tema.Superficie);
        campos.Dock = DockStyle.Top;
        campos.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        campos.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        campos.Controls.Add(Texto("Banco de dados", Fontes.Rotulo, _tema.TextoSuave, new Padding(0, 0, 0, 6)), 0, 0);
        campos.SetColumnSpan(campos.GetControlFromPosition(0, 0)!, 2);

        _banco = new CampoTexto { Tema = _tema, Icone = Icones.Banco, Dock = DockStyle.Fill, Margin = new Padding(0) };
        _banco.Caixa.Text = bancoSugerido ?? string.Empty;
        _banco.Caixa.PlaceholderText = @"C:\SYSPDV\syspdv_srv.fdb";
        campos.Controls.Add(_banco, 0, 1);

        _procurar = new Botao { Tema = _tema, Text = "Procurar", Icone = Icones.Pasta, Size = new Size(118, 40), Margin = new Padding(8, 0, 0, 0) };
        _procurar.Click += (_, _) => ProcurarBanco();
        campos.Controls.Add(_procurar, 1, 1);

        var rotuloSenha = Texto("Senha do banco", Fontes.Rotulo, _tema.TextoSuave, new Padding(0, 16, 0, 6));
        campos.Controls.Add(rotuloSenha, 0, 2);
        campos.SetColumnSpan(rotuloSenha, 2);

        _senha = new CampoTexto { Tema = _tema, Icone = Icones.Cadeado, Dock = DockStyle.Fill, Margin = new Padding(0) };
        _senha.Caixa.UseSystemPasswordChar = true;
        _senha.Caixa.PlaceholderText = "Senha do Firebird";
        campos.Controls.Add(_senha, 0, 3);
        campos.SetColumnSpan(_senha, 2);

        _erroEntrada = Texto("", Fontes.Pequena, _tema.Falha, new Padding(0, 10, 0, 0));
        _erroEntrada.Visible = false;
        campos.Controls.Add(_erroEntrada, 0, 4);
        campos.SetColumnSpan(_erroEntrada, 2);

        formulario.Controls.Add(campos);
        _painelEntrada.Controls.Add(formulario);

        _iniciar = new Botao
        {
            Tema = _tema, Primario = true, Text = "Iniciar reparo",
            Dock = DockStyle.Top, Height = 46, Margin = new Padding(0, 18, 0, 0),
        };
        _iniciar.Click += async (_, _) => await IniciarAsync();
        _painelEntrada.Controls.Add(_iniciar);

        // ---------------------------------------------------------------- progresso
        _painelReparo = Tabela(1);
        _painelReparo.Dock = DockStyle.Fill;
        _painelReparo.Visible = false;

        var cartaoProgresso = NovoCartao();
        cartaoProgresso.AutoSize = false;
        cartaoProgresso.Dock = DockStyle.Fill;
        var progresso = Tabela(1, _tema.Superficie);
        progresso.Dock = DockStyle.Fill;
        for (var i = 0; i < 4; i++)
            progresso.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        progresso.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _bancoEmReparo = Texto("", Fontes.Pequena, _tema.TextoSuave, new Padding(0, 0, 0, 12));
        _bancoEmReparo.AutoEllipsis = true;
        progresso.Controls.Add(_bancoEmReparo);
        _contador = Texto("Preparando...", Fontes.Pequena, _tema.TextoSuave, new Padding(0, 0, 0, 2));
        progresso.Controls.Add(_contador);
        _etapaAtual = Texto("", Fontes.Subtitulo, _tema.Texto, new Padding(0, 0, 0, 12));
        _etapaAtual.AutoEllipsis = true;
        progresso.Controls.Add(_etapaAtual);
        _barra = new BarraProgresso { Tema = _tema, Dock = DockStyle.Top, Height = 6, Margin = new Padding(0, 0, 0, 18) };
        progresso.Controls.Add(_barra);
        _etapas = new ListaEtapas { Tema = _tema, Dock = DockStyle.Fill, Margin = new Padding(0) };
        progresso.Controls.Add(_etapas);

        cartaoProgresso.Controls.Add(progresso);
        _painelReparo.Controls.Add(cartaoProgresso);

        var conteudo = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0), BackColor = _tema.Fundo };
        conteudo.Controls.Add(_painelReparo);
        conteudo.Controls.Add(_painelEntrada);
        _paginaReparo.Controls.Add(conteudo, 0, 0);

        // ---------------------------------------------------------------- resultado
        _resultado = NovoCartao();
        _resultado.Margin = new Padding(0, 14, 0, 0);
        _resultado.Visible = false;
        var corpoResultado = Tabela(2, _tema.Superficie);
        corpoResultado.Dock = DockStyle.Top;
        corpoResultado.Padding = new Padding(0, 0, 0, 6);
        corpoResultado.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        corpoResultado.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _seloResultado = new SeloIcone { Tema = _tema, Size = new Size(26, 26), Margin = new Padding(0, 1, 12, 0), Estado = EstadoLinha.Ok };
        corpoResultado.Controls.Add(_seloResultado, 0, 0);
        corpoResultado.SetRowSpan(_seloResultado, 3);
        _tituloResultado = Texto("", Fontes.Subtitulo, _tema.Texto, new Padding(0, 0, 0, 4));
        corpoResultado.Controls.Add(_tituloResultado, 1, 0);
        _textoResultado = Texto("", Fontes.Normal, _tema.TextoSuave, new Padding(0));
        corpoResultado.Controls.Add(_textoResultado, 1, 1);
        _caminhoLog = new TextBox
        {
            ReadOnly = true, BorderStyle = BorderStyle.None, Font = Fontes.Pequena, TabStop = false,
            BackColor = _tema.Superficie, ForeColor = _tema.TextoSuave, Dock = DockStyle.Top,
            Margin = new Padding(0, 10, 0, 0), Visible = false,
        };
        corpoResultado.Controls.Add(_caminhoLog, 1, 2);
        _resultado.Controls.Add(corpoResultado);
        _paginaReparo.Controls.Add(_resultado, 0, 1);

        // ---------------------------------------------------------------- rodapé
        _fechar = new Botao { Tema = _tema, Text = "Fechar", Size = new Size(120, 40), Anchor = AnchorStyles.Right, Margin = new Padding(0, 14, 0, 0), Visible = false };
        _fechar.Click += (_, _) => Close();
        _paginaReparo.Controls.Add(_fechar, 0, 2);

        // ---------------------------------------------------------------- páginas
        _paginaSobre = PaginaSobre();
        _paginaSobre.Visible = false;
        _paginas = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0), BackColor = _tema.Fundo };
        _transicao = new TransicaoPagina { Dock = DockStyle.Fill };
        _paginas.Controls.Add(_transicao); // por cima das páginas, só durante a troca
        _paginas.Controls.Add(_paginaSobre);
        _paginas.Controls.Add(_paginaReparo);
        raiz.Controls.Add(_paginas, 0, 2);

        Controls.Add(raiz);
        AcceptButton = _iniciar;
        Shown += (_, _) => (_banco.Caixa.Text.Length == 0 ? (Control)_procurar : _senha.Caixa).Focus();
    }

    // ------------------------------------------------------------------ montagem

    /// <summary>O mesmo ícone do .exe (app.ico), para a barra de título e a barra de tarefas.</summary>
    private static Icon? IconeDoPrograma()
    {
        try
        {
            return Environment.ProcessPath is { } exe ? Icon.ExtractAssociatedIcon(exe) : null;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            return null; // fica o ícone padrão do Windows
        }
    }

    private Control Cabecalho()
    {
        var cabecalho = Tabela(2);
        cabecalho.Dock = DockStyle.Top;
        cabecalho.Margin = new Padding(0, 0, 0, 14);
        cabecalho.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        cabecalho.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // Logo da PBtec no tema da tela; sem ele, fica o selo azul com a chave inglesa.
        Control selo = _tema.Logo() is { } logo
            ? new ImagemLogo { Imagem = logo, Size = new Size(46, 56), Margin = new Padding(0, 0, 14, 0) }
            : new SeloIcone { Tema = _tema, CorFundo = _tema.Destaque, Size = new Size(48, 48), Margin = new Padding(0, 0, 14, 0) };
        cabecalho.Controls.Add(selo, 0, 0);
        cabecalho.SetRowSpan(selo, 2);
        cabecalho.Controls.Add(Texto("Reparo do banco de dados", Fontes.Titulo, _tema.Texto, new Padding(0, 0, 0, 0)), 1, 0);
        cabecalho.Controls.Add(Texto("SysPDV · Firebird", Fontes.Normal, _tema.TextoSuave, new Padding(2, 0, 0, 0)), 1, 1);
        return cabecalho;
    }

    /// <summary>Aba "Sobre": quem fez o programa e o contato da PBtec.</summary>
    private Control PaginaSobre()
    {
        var pagina = Tabela(1);
        pagina.Dock = DockStyle.Fill;

        var cartao = NovoCartao();
        cartao.Padding = new Padding(24, 28, 24, 24);
        var corpo = Tabela(1, _tema.Superficie);
        corpo.Dock = DockStyle.Top;
        corpo.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        if (_tema.Logo() is { } logo)
        {
            corpo.Controls.Add(new ImagemLogo
            {
                Imagem = logo, Size = new Size(150, 170), Anchor = AnchorStyles.None,
                Margin = new Padding(0, 0, 0, 20), BackColor = _tema.Superficie,
            });
        }

        corpo.Controls.Add(Centralizado("PBtec - Soluções em Automação", Fontes.Subtitulo, _tema.Texto, new Padding(0, 0, 0, 4)));
        corpo.Controls.Add(Centralizado("(83) 3224-3080", Fontes.Normal, _tema.TextoSuave, new Padding(0)));

        var separador = new Panel { Height = 1, Dock = DockStyle.Top, BackColor = _tema.Borda, Margin = new Padding(40, 22, 40, 20) };
        corpo.Controls.Add(separador);

        corpo.Controls.Add(Centralizado("Desenvolvimento e Análise", Fontes.Rotulo, _tema.TextoSuave, new Padding(0, 0, 0, 8)));
        foreach (var nome in new[] { "José Henrique", "Erick Henrique", "Eduardo Vasconcelos" })
            corpo.Controls.Add(Centralizado(nome, Fontes.Normal, _tema.Texto, new Padding(0, 0, 0, 3)));

        var versao = typeof(JanelaPrincipal).Assembly.GetName().Version?.ToString(3);
        corpo.Controls.Add(Centralizado(
            "Reparo do banco de dados" + (versao is null ? "" : " · versão " + versao),
            Fontes.Pequena, _tema.TextoSuave, new Padding(0, 22, 0, 0)));

        cartao.Controls.Add(corpo);
        pagina.Controls.Add(cartao);
        return pagina;
    }

    private static Label Centralizado(string texto, Font fonte, Color cor, Padding margem)
    {
        var rotulo = Texto(texto, fonte, cor, margem);
        rotulo.TextAlign = ContentAlignment.MiddleCenter;
        return rotulo;
    }

    /// <summary>Troca a página aberta (0 = Reparo, 1 = Sobre), com uma transição curta se <paramref name="animar"/>.</summary>
    private void MostrarPagina(int indice, bool animar)
    {
        _animacaoPagina.Parar(); // termina a troca anterior, se ainda estiver acontecendo

        Control nova = indice == 0 ? _paginaReparo : _paginaSobre;
        Control antiga = indice == 0 ? _paginaSobre : _paginaReparo;
        var tamanho = _paginas.ClientSize;
        var comAnimacao = animar && Animacao.Ligadas && IsHandleCreated && Visible && antiga.Visible
                          && tamanho.Width > 0 && tamanho.Height > 0;

        var antes = comAnimacao ? Foto(antiga, tamanho) : null;
        antiga.Visible = false;
        nova.Visible = true;
        // Enter só inicia o reparo com a página do reparo à vista.
        AcceptButton = indice == 0 ? _iniciar : null;
        if (antes is null)
            return;

        _paginas.PerformLayout();
        _transicao.Preparar(antes, Foto(nova, tamanho), direcao: indice == 1 ? 1 : -1);
        _transicao.Visible = true;
        _transicao.BringToFront();
        _animacaoPagina.Iniciar(320,
            t => { _transicao.Progresso = t; _transicao.Invalidate(); _transicao.Update(); },
            () => { _transicao.Visible = false; _transicao.Limpar(); });
    }

    private static Bitmap Foto(Control pagina, Size tamanho)
    {
        var foto = new Bitmap(tamanho.Width, tamanho.Height);
        pagina.DrawToBitmap(foto, new Rectangle(Point.Empty, tamanho));
        return foto;
    }

    /// <summary>Abre uma aba sem animação (usado pela prévia da tela).</summary>
    internal void AbrirAba(int indice) => _abas.Selecionar(indice, animar: false);

    private Control Aviso(string texto)
    {
        var cartao = NovoCartao(_tema.AvisoFundo, Botao.Misturar(_tema.Aviso, _tema.AvisoFundo, 0.35f));
        cartao.Padding = new Padding(16, 12, 16, 12);
        cartao.Margin = new Padding(0, 0, 0, 14);

        var linha = Tabela(2, _tema.AvisoFundo);
        linha.Dock = DockStyle.Top;
        linha.Padding = new Padding(0, 0, 0, 4);
        linha.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        linha.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var icone = new Label
        {
            Text = Icones.Info, Font = Fontes.Icones(12f) ?? Fontes.Normal, ForeColor = _tema.Aviso,
            AutoSize = true, Margin = new Padding(0, 2, 10, 0), BackColor = _tema.AvisoFundo,
        };
        if (Fontes.Icones(12f) is null)
            icone.Text = "!";
        linha.Controls.Add(icone, 0, 0);
        linha.Controls.Add(Texto(texto, Fontes.Normal, _tema.Texto, new Padding(0)), 1, 0);
        cartao.Controls.Add(linha);
        return cartao;
    }

    private Cartao NovoCartao(Color? fundo = null, Color? borda = null) => new()
    {
        CorFundo = fundo ?? _tema.Superficie,
        CorBorda = borda ?? _tema.Borda,
        BackColor = _tema.Fundo,
        Padding = new Padding(20),
        Margin = new Padding(0),
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
    };

    private TableLayoutPanel Tabela(int colunas, Color? fundo = null) => new()
    {
        ColumnCount = colunas,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Margin = new Padding(0),
        Padding = new Padding(0),
        BackColor = fundo ?? _tema.Fundo,
    };

    private static Label Texto(string texto, Font fonte, Color cor, Padding margem) => new()
    {
        Text = texto,
        Font = fonte,
        ForeColor = cor,
        AutoSize = true,
        Dock = DockStyle.Fill,
        Margin = margem,
        UseMnemonic = false,
    };

    // ------------------------------------------------------------------ ações

    private void ProcurarBanco()
    {
        using var dialogo = new OpenFileDialog
        {
            Title = "Escolha o banco de dados do SysPDV",
            Filter = "Banco de dados Firebird (*.fdb)|*.fdb|Todos os arquivos (*.*)|*.*",
            CheckFileExists = true,
        };

        var atual = _banco.Caixa.Text.Trim();
        if (File.Exists(atual))
        {
            dialogo.InitialDirectory = Path.GetDirectoryName(atual);
            dialogo.FileName = Path.GetFileName(atual);
        }

        if (dialogo.ShowDialog(this) == DialogResult.OK)
        {
            _banco.Caixa.Text = dialogo.FileName;
            _senha.Caixa.Focus();
        }
    }

    private void ErroNaEntrada(string mensagem, CampoTexto? campo)
    {
        _erroEntrada.Text = mensagem;
        _erroEntrada.Visible = true;
        if (campo is not null)
        {
            campo.Erro = true;
            campo.Caixa.Focus();
        }
    }

    private async Task IniciarAsync()
    {
        if (_emAndamento)
            return;

        _erroEntrada.Visible = false;
        var banco = _banco.Caixa.Text.Trim().Trim('"');
        if (banco.Length == 0 || !File.Exists(banco))
        {
            ErroNaEntrada(@"Escolha o arquivo do banco de dados (normalmente C:\SYSPDV\syspdv_srv.fdb).", _banco);
            return;
        }

        if (_senha.Caixa.Text.Length == 0)
        {
            ErroNaEntrada("Digite a senha do banco de dados. Se não souber, chame o suporte.", _senha);
            return;
        }

        Configuracao configuracao;
        try
        {
            configuracao = _carregarConfiguracao(banco);
        }
        catch (ConfiguracaoInvalidaException ex)
        {
            ErroNaEntrada(ex.Message + " Chame o suporte.", null);
            return;
        }

        configuracao.Senha = _senha.Caixa.Text;
        var problemas = configuracao.Validar();
        if (problemas.Count > 0)
        {
            ErroNaEntrada("Não é possível reparar este banco:\n" + string.Join("\n", problemas.Select(p => "• " + p)), null);
            return;
        }

        _emAndamento = true;
        PrepararParaReparo(banco);

        var mascarador = new MascaradorSenha(configuracao.Senha);
        var interacao = new InteracaoJanela(this, mascarador);
        var reparoComecou = false;

        try
        {
            var sessao = await Task.Run(() => SessaoReparo.Executar(
                configuracao, interacao, mascarador,
                informarErro: msg => Invoke(() => MostrarResultado(false, msg)),
                logCriado: caminho => BeginInvoke(() => MostrarLog(caminho))));

            Codigo = sessao.Codigo;
            reparoComecou = _etapas.Quantidade > 0;
        }
        finally
        {
            _emAndamento = false;
            if (reparoComecou)
            {
                // Depois de um reparo de verdade, para repetir é preciso abrir o programa de novo.
                _fechar.Visible = true;
                _fechar.Focus();
            }
            else
            {
                // Nada foi feito (cancelado ou recusado logo no começo): volta para corrigir e tentar de novo.
                _painelReparo.Visible = false;
                _painelEntrada.Visible = true;
                _iniciar.Enabled = true;
            }
        }
    }

    /// <summary>Troca a entrada pelo acompanhamento do reparo (na mesma janela).</summary>
    internal void PrepararParaReparo(string banco)
    {
        _iniciar.Enabled = false;
        _painelEntrada.Visible = false;
        _painelReparo.Visible = true;
        _resultado.Visible = false;
        _caminhoLog.Visible = false;
        _fechar.Visible = false;
        _bancoEmReparo.Text = "Banco: " + banco;
        _contador.Text = "Preparando...";
        _etapaAtual.Text = "Conferindo antes de começar";
        _barra.CorFixa = null;
        _barra.Valor = 0;
        _etapas.Limpar();
        _primeiraEtapaDaLista = 1;
    }

    // ------------------------------------------------------------------ chamado por InteracaoJanela

    public void EtapaComecou(int etapa, int total, string descricao)
    {
        if (_etapas.Quantidade == 0)
            _primeiraEtapaDaLista = etapa;
        _barra.Maximo = total;
        _barra.Valor = etapa - 1;
        _contador.Text = $"Etapa {etapa} de {total}";
        _etapaAtual.Text = descricao;
        _etapas.Adicionar(descricao);
    }

    public void EtapaTerminou(int etapa, StatusEtapa status)
    {
        _etapas.Marcar(etapa - _primeiraEtapaDaLista, status switch
        {
            StatusEtapa.Ok => EstadoLinha.Ok,
            StatusEtapa.Aviso => EstadoLinha.Aviso,
            _ => EstadoLinha.Falha,
        });
        _barra.Valor = etapa;
    }

    public void MostrarResultado(bool sucesso, string mensagem)
    {
        var linhas = mensagem.Replace("\r", "").Split('\n');
        var titulo = linhas[0].Trim();
        var corpo = string.Join("\n", linhas.Skip(1)).Trim();
        var comecou = _etapas.Quantidade > 0;

        // Recusado/cancelado antes de começar: nada foi alterado, então é amarelo, não vermelho.
        var (estado, cor) = sucesso ? (EstadoLinha.Ok, _tema.Sucesso)
                          : comecou ? (EstadoLinha.Falha, _tema.Falha)
                          : (EstadoLinha.Aviso, _tema.Aviso);

        _tituloResultado.Text = sucesso ? "Reparo concluído" : titulo;
        _textoResultado.Text = sucesso ? corpo : corpo.Length > 0 ? corpo : "";
        _textoResultado.Visible = _textoResultado.Text.Length > 0;
        _seloResultado.Estado = estado;
        _seloResultado.Invalidate();
        _resultado.CorFaixa = cor;
        _resultado.Visible = true;
        _resultado.Invalidate();

        if (comecou)
        {
            _barra.CorFixa = sucesso ? _tema.Sucesso : _tema.Falha;
            if (sucesso)
                _barra.Valor = _barra.Maximo;
            _contador.Text = sucesso ? "Concluído" : "Interrompido";
            _etapaAtual.Text = sucesso ? "Tudo certo" : "O reparo parou";
        }
    }

    internal void MostrarErroDeEntradaParaPrevia() =>
        ErroNaEntrada("Digite a senha do banco de dados. Se não souber, chame o suporte.", _senha);

    internal void MostrarLog(string caminho)
    {
        _caminhoLog.Text = "Log para o suporte: " + caminho;
        _caminhoLog.Visible = true;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _tema.AplicarNaBarraDeTitulo(Handle);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _animacaoPagina.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_emAndamento)
        {
            e.Cancel = true;
            MessageBox.Show(this, "O reparo está em andamento. Aguarde ele terminar antes de fechar.", Titulo,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        base.OnFormClosing(e);
    }
}
