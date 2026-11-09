using Reparo.Core;

namespace Reparo.Testes;

/// <summary>Reparo completo com as ferramentas simuladas em C# (sem processos externos).</summary>
public sealed class ReparadorTestes : IDisposable
{
    private const string Senha = "S3nha-Teste!";
    private static readonly DateTime Agora = new(2026, 11, 3, 10, 0, 0);

    private readonly PastaTemporaria _temp = new();
    private readonly string _base;
    private readonly string _banco;
    private readonly string _backupAnterior;
    private readonly SimuladorFerramentas _sim = new();
    private readonly InteracaoFalsa _interacao = new();
    private readonly RegistroFalso _registro = new();
    private readonly AmbienteFalso _ambiente = new();

    public ReparadorTestes()
    {
        _base = Path.Combine(_temp.Caminho, "SYSPDV");
        _banco = _temp.Arquivo(@"SYSPDV\syspdv_srv.fdb", "ORIGINAL");
        _backupAnterior = _temp.Arquivo(@"SYSPDV\bkpBd\syspdv_srv.fdb", "ANTERIOR");
        _temp.Arquivo(@"SYSPDV\syspdv.ini", "ini");
        _temp.Arquivo(@"SYSPDV\VERIFICAINDICE.EXE", "exe");
        _temp.Arquivo(@"SYSPDV\syspdv_server.exe", "exe");
        _temp.Arquivo(@"SYSPDV\srvconfig.ini", "não pode apagar: .ini");
        _temp.Arquivo(@"SYSPDV\bkpBd\leia-me.txt", "subpasta");
        _temp.Arquivo(@"SYSPDV\srv0001.tmp", "lixo");
        _temp.Arquivo(@"SYSPDV\syspdv.log", "lixo");
        _temp.Arquivo(@"SYSPDV\erros.err", "lixo");
        _temp.Arquivo(@"SYSPDV\banco_aux.tmp", "lixo");
        _temp.Arquivo(@"SYSPDV\relatorio.txt", "lixo");
        _temp.Arquivo(@"fora.txt", "fora da pasta base");
    }

    public void Dispose() => _temp.Dispose();

    private string Guardado => Path.Combine(_base, "bkpBd", "syspdv_srv_20261103_100000.fdb");
    private string Fbk => Path.Combine(_base, "bkpBd", "syspdv_srv_20261103_100000.fbk");

    private CodigoSaida Rodar(Configuracao? cfg = null) =>
        new Reparador(cfg ?? ConfigTeste.Criar(_base, Senha), _interacao, _registro, _sim, _ambiente, () => Agora).Executar();

    private int Indice(string ferramenta, string modo) =>
        _sim.Chamadas.FindIndex(c => c.Ferramenta == ferramenta && c.Modo == modo && c.Codigo == 0);

    [Fact]
    public void Normal_repara_o_banco_e_segue_a_ordem()
    {
        var resultado = Rodar();

        Assert.Equal(CodigoSaida.Sucesso, resultado);
        Assert.Equal("RESTAURADO:ORIGINAL", File.ReadAllText(_banco));
        Assert.Equal("ORIGINAL", File.ReadAllText(Guardado));
        Assert.Equal("ANTERIOR", File.ReadAllText(_backupAnterior));
        Assert.True(_sim.ServicoLigado);

        var ordem = new[]
        {
            Indice("gfix", "mend"), Indice("gbak", "backup"), Indice("gbak", "restore"),
            Indice("VERIFICAINDICE", "executar"), Indice("atualizasyspdv_server", "executar"),
            Indice("syspdv_server", "executar"),
        };
        Assert.DoesNotContain(-1, ordem);
        Assert.Equal(ordem.OrderBy(i => i), ordem);
    }

    [Fact]
    public void Limpeza_apaga_so_os_temporarios_da_pasta_base()
    {
        Rodar();

        foreach (var lixo in new[] { "srv0001.tmp", "syspdv.log", "erros.err", "banco_aux.tmp", "relatorio.txt" })
            Assert.False(File.Exists(Path.Combine(_base, lixo)), lixo);

        Assert.True(File.Exists(Path.Combine(_base, "syspdv.ini")));
        Assert.True(File.Exists(Path.Combine(_base, "srvconfig.ini")));
        Assert.True(File.Exists(Path.Combine(_base, "bkpBd", "leia-me.txt")));
        Assert.True(File.Exists(Path.Combine(_temp.Caminho, "fora.txt")));
    }

    [Fact]
    public void Senha_vai_so_por_credenciais_e_nunca_nos_argumentos()
    {
        Rodar();

        Assert.All(_sim.Chamadas.Where(c => c.Ferramenta is "gfix" or "gbak"), c => Assert.True(c.Opcoes.UsarCredenciais));
        Assert.All(_sim.Chamadas.Where(c => c.Ferramenta is not ("gfix" or "gbak")), c => Assert.False(c.Opcoes.UsarCredenciais));
        Assert.DoesNotContain(_sim.Chamadas, c => c.Argumentos.Any(a => a.Contains(Senha)));
        Assert.DoesNotContain(Senha, _registro.Tudo);
    }

    [Fact]
    public void Banco_corrompido_na_validacao_continua()
    {
        _sim.Falhas.Add("gfix:validar");
        _sim.Falhas.Add("gfix:mend");

        Assert.Equal(CodigoSaida.Sucesso, Rodar());
        Assert.Equal("RESTAURADO:ORIGINAL", File.ReadAllText(_banco));
        Assert.Contains("Avisos", _interacao.Avisos.Last());
    }

    [Fact]
    public void Servico_ja_parado_segue_normalmente()
    {
        _sim.ServicoLigado = false;

        Assert.Equal(CodigoSaida.Sucesso, Rodar());
        Assert.True(_sim.ServicoLigado);
    }

    [Fact]
    public void Falha_no_backup_para_tudo_e_mantem_o_banco()
    {
        _sim.Falhas.Add("gbak:backup");

        Assert.Equal(CodigoSaida.FalhaNoReparo, Rodar());
        Assert.Equal("ORIGINAL", File.ReadAllText(_banco));
        Assert.Equal(-1, _sim.Chamadas.FindIndex(c => c.Modo == "restore"));
        Assert.False(File.Exists(Fbk));
        Assert.True(File.Exists(Fbk + ".incompleto"));
        Assert.True(_sim.ServicoLigado);
        Assert.Contains("não foi alterado", _interacao.Avisos.Last());
    }

    [Fact]
    public void Falha_no_restore_devolve_o_original_e_apaga_o_parcial()
    {
        _sim.Falhas.Add("gbak:restore");

        Assert.Equal(CodigoSaida.FalhaNoReparo, Rodar());
        Assert.Equal("ORIGINAL", File.ReadAllText(_banco));
        Assert.False(File.Exists(Guardado));
        Assert.Equal("ANTERIOR", File.ReadAllText(_backupAnterior));
        Assert.Equal(-1, _sim.Chamadas.FindIndex(c => c.Ferramenta == "VERIFICAINDICE"));
        Assert.True(_sim.ServicoLigado);
        Assert.Contains("devolvido ao lugar", _interacao.Avisos.Last());
    }

    [Fact]
    public void Banco_restaurado_e_conferido_antes_de_seguir()
    {
        Assert.Equal(CodigoSaida.Sucesso, Rodar());

        var restore = Indice("gbak", "restore");
        var conferencia = _sim.Chamadas.FindIndex(restore + 1, c => c.Ferramenta == "gfix");
        Assert.True(conferencia > restore, "o gfix deve rodar depois do restore");
        Assert.Equal(new[] { "-v", "-f", _banco }, _sim.Chamadas[conferencia].Argumentos);
        Assert.True(_sim.Chamadas[conferencia].Opcoes.UsarCredenciais);
        Assert.True(conferencia < Indice("VERIFICAINDICE", "executar"));

        // Firebird reiniciado entre o restore e a conferência (gfix -v não valida com outras conexões).
        var entre = _sim.Chamadas.Skip(restore + 1).Take(conferencia - restore - 1).Select(c => c.Modo).ToList();
        Assert.Equal(new[] { "stop", "start" }, entre);
    }

    [Fact]
    public void Outro_programa_conectado_no_banco_novo_so_gera_aviso()
    {
        // Caso real (05/11/2026): "secondary server attachments cannot validate databases".
        _sim.RestauradoComOutraConexao = true;

        Assert.Equal(CodigoSaida.Sucesso, Rodar());
        Assert.Equal("RESTAURADO:ORIGINAL", File.ReadAllText(_banco));
        Assert.Contains("outro programa estava conectado", _interacao.Avisos.Last());
    }

    [Fact]
    public void Banco_restaurado_com_erros_devolve_o_original_e_guarda_o_backup()
    {
        _sim.BancoRestauradoComErros = true;

        Assert.Equal(CodigoSaida.FalhaNoReparo, Rodar());
        Assert.Equal("ORIGINAL", File.ReadAllText(_banco));
        Assert.False(File.Exists(Guardado));
        Assert.Equal("FBK:ORIGINAL", File.ReadAllText(Fbk));
        Assert.Equal("ANTERIOR", File.ReadAllText(_backupAnterior));
        Assert.Equal(-1, _sim.Chamadas.FindIndex(c => c.Ferramenta == "VERIFICAINDICE"));
        Assert.True(_sim.ServicoLigado);
        Assert.Contains("Conferir o banco reconstruído", _interacao.Avisos.Last());
        Assert.Contains("devolvido ao lugar", _interacao.Avisos.Last());
    }

    [Fact]
    public void Falha_depois_do_restore_mantem_o_banco_novo_e_nao_segue()
    {
        _sim.Falhas.Add("VERIFICAINDICE:executar");

        Assert.Equal(CodigoSaida.FalhaNoReparo, Rodar());
        Assert.Equal("RESTAURADO:ORIGINAL", File.ReadAllText(_banco));
        Assert.Equal("ORIGINAL", File.ReadAllText(Guardado));
        Assert.Equal(-1, _sim.Chamadas.FindIndex(c => c.Ferramenta == "atualizasyspdv_server"));
        Assert.Equal(-1, _sim.Chamadas.FindIndex(c => c.Ferramenta == "syspdv_server"));
        Assert.Contains("Chame o suporte", _interacao.Avisos.Last());
    }

    [Fact]
    public void Falha_ao_abrir_o_sistema_e_so_aviso()
    {
        _sim.Falhas.Add("syspdv_server:executar");

        Assert.Equal(CodigoSaida.Sucesso, Rodar());
        Assert.Contains("abra manualmente", _interacao.Avisos.Last());
    }

    [Fact]
    public void Operador_cancela_e_nada_e_executado()
    {
        _interacao.Resposta = false;

        Assert.Equal(CodigoSaida.CanceladoPeloOperador, Rodar());
        Assert.Empty(_sim.Chamadas);
        Assert.Contains("caixas serão desconectados", _interacao.Perguntas.Single());
    }

    [Fact]
    public void Banco_inexistente_para_antes_de_mexer_no_servico()
    {
        File.Delete(_banco);

        Assert.Equal(CodigoSaida.FalhaNoReparo, Rodar());
        Assert.DoesNotContain(_sim.Chamadas, c => c.Ferramenta == "net" && c.Modo == "stop");
    }

    [Fact]
    public void Sem_administrador_em_producao_nao_comeca()
    {
        _ambiente.Administrador = false;

        Assert.Equal(CodigoSaida.FalhaNoReparo,
            Rodar(ConfigTeste.Criar(_base, Senha, modoTeste: false, naoInterativo: false)));
        Assert.DoesNotContain(_sim.Chamadas, c => c.Modo == "stop");
        Assert.Contains("administrador", _interacao.Avisos.Last());
    }

    private Configuracao ProducaoComCopia() =>
        ConfigTeste.Criar(_base, Senha, modoTeste: false, naoInterativo: false, copiarProgramas: true);

    [Fact]
    public void Programas_do_reparo_vao_para_a_pasta_antes_de_parar_o_servico()
    {
        Assert.Equal(CodigoSaida.Sucesso, Rodar(ProducaoComCopia()));

        foreach (var arquivo in ProgramasDoReparo.Embutidos())
            Assert.Equal(arquivo.Conteudo, File.ReadAllBytes(Path.Combine(_base, arquivo.Nome)));

        var copia = _interacao.Progresso.FindIndex(p => p.Contains("Colocar os programas"));
        var parar = _interacao.Progresso.FindIndex(p => p.Contains("Parar o Firebird"));
        Assert.Equal(1, copia); // logo depois das pré-checagens
        Assert.True(copia < parar);
        Assert.Equal(22, _interacao.Terminadas.Count);
    }

    [Fact]
    public void Sem_runtime_do_Visual_C_2005_nao_copia_nem_comeca()
    {
        _ambiente.RuntimeVc80 = false;

        Assert.Equal(CodigoSaida.FalhaNoReparo, Rodar(ProducaoComCopia()));
        Assert.False(File.Exists(Path.Combine(_base, "fbclient.dll")));
        Assert.DoesNotContain(_sim.Chamadas, c => c.Modo == "stop");
        Assert.Contains("Visual C++ 2005", _interacao.Avisos.Last());
    }

    [Fact]
    public void Pasta_que_nao_e_do_SysPDV_nao_recebe_as_ferramentas()
    {
        File.Delete(Path.Combine(_base, "VERIFICAINDICE.EXE"));
        File.Delete(Path.Combine(_base, "syspdv_server.exe"));

        Assert.Equal(CodigoSaida.FalhaNoReparo, Rodar(ProducaoComCopia()));
        Assert.False(File.Exists(Path.Combine(_base, "fbclient.dll")));
        Assert.False(File.Exists(Path.Combine(_base, "gfix.exe")));
    }

    [Fact]
    public void Modo_teste_nunca_copia_as_ferramentas()
    {
        Assert.Equal(CodigoSaida.Sucesso, Rodar());
        Assert.False(File.Exists(Path.Combine(_base, "fbclient.dll")));
        Assert.DoesNotContain(_interacao.Progresso, p => p.Contains("Colocar os programas"));
    }

    [Fact]
    public void Programas_embutidos_nao_sao_cobrados_nas_prechecagens()
    {
        // Pasta do SysPDV padrão: sem VerificaIndice, dbMonitor, gfix e gbak (o reparo é que leva).
        File.Delete(Path.Combine(_base, "VERIFICAINDICE.EXE"));
        foreach (var nome in new[] { "VERIFICAINDICE", "dbMonitor", "gfix", "gbak" })
            _sim.Ausentes.Add(nome);

        Rodar(ProducaoComCopia());

        Assert.Equal((1, StatusEtapa.Ok), _interacao.Terminadas[0]);
        Assert.Equal((2, StatusEtapa.Ok), _interacao.Terminadas[1]);
        Assert.True(File.Exists(Path.Combine(_base, "VerificaIndice.exe")));
        Assert.True(File.Exists(Path.Combine(_base, "dbMonitor.exe")));
    }

    [Fact]
    public void Gfix_que_nem_abre_para_na_primeira_verificacao()
    {
        _sim.Ausentes.Add("gfix");

        Assert.Equal(CodigoSaida.FalhaNoReparo, Rodar(ProducaoComCopia()));
        Assert.DoesNotContain(_sim.Chamadas, c => c.Ferramenta == "gbak");
        Assert.Contains("não foi possível abrir o gfix", _interacao.Avisos.Last());
    }

    [Fact]
    public void Sem_janela_fora_do_teste_nao_comeca()
    {
        Assert.Equal(CodigoSaida.ConfiguracaoInvalida, Rodar(ConfigTeste.Criar(_base, Senha, modoTeste: false)));
        Assert.Empty(_sim.Chamadas);
        Assert.Empty(_interacao.Perguntas);
        Assert.Equal("ORIGINAL", File.ReadAllText(_banco));
        Assert.Contains("dois cliques", _interacao.Avisos.Last());
    }

    [Fact]
    public void Sem_administrador_no_modo_teste_e_so_aviso()
    {
        _ambiente.Administrador = false;
        Assert.Equal(CodigoSaida.Sucesso, Rodar());
    }

    [Fact]
    public void Pouco_espaco_em_disco_nao_comeca()
    {
        _ambiente.EspacoLivre = 1024;

        Assert.Equal(CodigoSaida.FalhaNoReparo, Rodar());
        Assert.DoesNotContain(_sim.Chamadas, c => c.Modo == "stop");
        Assert.Contains("espaço", _interacao.Avisos.Last());
    }

    [Fact]
    public void Ferramenta_obrigatoria_ausente_nao_comeca()
    {
        _sim.Ausentes.Add("gbak");

        Assert.Equal(CodigoSaida.FalhaNoReparo, Rodar());
        Assert.DoesNotContain(_sim.Chamadas, c => c.Modo == "stop");
        Assert.Contains("gbak", _interacao.Avisos.Last());
    }

    [Fact]
    public void Servico_que_nao_para_e_encerrado_a_forca()
    {
        _sim.Falhas.Add("net:stop");

        Rodar();

        Assert.Contains(_sim.Chamadas, c => c.Ferramenta == "taskkill" && c.Argumentos.Contains("fbserver.exe"));
    }

    [Fact]
    public void Backup_antigo_com_mesmo_nome_nao_e_sobrescrito()
    {
        var existente = _temp.Arquivo(@"SYSPDV\bkpBd\syspdv_srv_20261103_100000.fdb", "OUTRO REPARO");

        Assert.Equal(CodigoSaida.Sucesso, Rodar());
        Assert.Equal("OUTRO REPARO", File.ReadAllText(existente));
        Assert.Equal("ORIGINAL", File.ReadAllText(Path.Combine(_base, "bkpBd", "syspdv_srv_20261103_100000_2.fdb")));
    }

    [Fact]
    public void Senha_errada_para_na_primeira_verificacao_sem_mexer_no_banco()
    {
        _sim.SenhaErrada = true;

        Assert.Equal(CodigoSaida.FalhaNoReparo, Rodar());
        Assert.Equal("ORIGINAL", File.ReadAllText(_banco));
        Assert.DoesNotContain(_sim.Chamadas, c => c.Ferramenta == "gbak");
        Assert.True(_sim.ServicoLigado);
        Assert.Contains("senha", _interacao.Avisos.Last());
    }

    [Fact]
    public void Sem_conexao_com_o_banco_para_na_primeira_verificacao()
    {
        // Caso real (04/11/2026): gfix/gbak 2.5 falando com Firebird 3+.
        _sim.ErroConexao = "unknown ISC error 335545106";

        Assert.Equal(CodigoSaida.FalhaNoReparo, Rodar());
        Assert.Equal("ORIGINAL", File.ReadAllText(_banco));
        Assert.Single(_sim.Chamadas, c => c.Ferramenta == "gfix");
        Assert.DoesNotContain(_sim.Chamadas, c => c.Ferramenta == "gbak");
        Assert.True(_sim.ServicoLigado);
        Assert.Contains("Verificar o banco de dados (1 de 3)", _interacao.Avisos.Last());
        Assert.Contains("não foi possível conectar", _interacao.Avisos.Last());
        Assert.Contains("335545106", _interacao.Avisos.Last());
    }

    [Theory]
    [InlineData("Syspdv_server.exe", true)]
    [InlineData("Syspdv_pdv.exe", true)]
    [InlineData("SysPDV_Service.exe", true)]
    [InlineData("SysPDVTools.exe", true)]
    [InlineData("AtualizaSysPDV_SERVER.exe", true)]
    [InlineData("VERIFICAINDICE.EXE", true)]
    [InlineData("ReparoSysPDV.exe", false)]
    [InlineData("syspdv_srv.fdb", false)]
    [InlineData("setup.exe", false)]
    public void Reconhece_programas_do_SysPDV(string nome, bool esperado) =>
        Assert.Equal(esperado, EtapasReparo.EhProgramaDoSysPdv(nome));

    [Fact]
    public void Pasta_do_SysPDV_com_outros_programas_e_aceita()
    {
        // Caso real (05/11/2026): pasta sem VerificaIndice nem Syspdv_server.exe.
        File.Delete(Path.Combine(_base, "VERIFICAINDICE.EXE"));
        File.Delete(Path.Combine(_base, "syspdv_server.exe"));
        _temp.Arquivo(@"SYSPDV\Syspdv_pdv.exe", "exe");

        Assert.Equal(CodigoSaida.Sucesso, Rodar());
    }

    [Fact]
    public void Pasta_recusada_lista_no_log_os_programas_que_tem()
    {
        File.Delete(Path.Combine(_base, "VERIFICAINDICE.EXE"));
        File.Delete(Path.Combine(_base, "syspdv_server.exe"));
        _temp.Arquivo(@"SYSPDV\ReparoSysPDV.exe", "exe");

        Assert.Equal(CodigoSaida.FalhaNoReparo, Rodar());
        Assert.Contains(_registro.Linhas, l => l.Contains("Programas encontrados") && l.Contains("ReparoSysPDV.exe"));
    }

    [Fact]
    public void Pasta_que_nao_e_do_SysPDV_nao_e_limpa()
    {
        File.Delete(Path.Combine(_base, "VERIFICAINDICE.EXE"));
        File.Delete(Path.Combine(_base, "syspdv_server.exe"));

        Assert.Equal(CodigoSaida.FalhaNoReparo, Rodar());
        Assert.True(File.Exists(Path.Combine(_base, "relatorio.txt")));
        Assert.DoesNotContain(_sim.Chamadas, c => c.Modo == "stop");
        Assert.Contains("pasta do SysPDV", _interacao.Avisos.Last());
    }

    [Fact]
    public void Erro_inesperado_fora_das_etapas_ainda_liga_o_Firebird()
    {
        _interacao.ExplodirNaEtapa = 3; // etapa 2 parou o Firebird

        Assert.Throws<InvalidOperationException>(() => Rodar());
        Assert.True(_sim.ServicoLigado);
        Assert.Equal("ORIGINAL", File.ReadAllText(_banco));
    }

    [Fact]
    public void Tela_recebe_o_resultado_de_cada_etapa()
    {
        _sim.Falhas.Add("gfix:validar");

        Rodar();

        Assert.Equal(21, _interacao.Terminadas.Count);
        Assert.Contains(_interacao.Terminadas, t => t.Status == StatusEtapa.Aviso);
        Assert.True(_interacao.Sucesso);
    }

    [Fact]
    public void Configuracao_invalida_para_antes_de_tudo()
    {
        Assert.Equal(CodigoSaida.ConfiguracaoInvalida, Rodar(ConfigTeste.Criar(_base, senha: null)));
        Assert.Empty(_sim.Chamadas);
        Assert.Contains(_registro.Linhas, l => l.StartsWith("ERRO Configuração") && l.Contains("senha"));
    }
}
