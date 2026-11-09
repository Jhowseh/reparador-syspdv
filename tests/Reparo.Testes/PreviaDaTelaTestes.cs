using System.Drawing.Imaging;
using Reparo.App;
using Reparo.App.Visual;
using Reparo.Core;

namespace Reparo.Testes;

/// <summary>
/// Gera imagens da tela em cada estado, sem executar reparo nenhum (a janela nunca recebe um clique).
/// Só roda com REPARO_PREVIA_DIR definida: scripts/previa-tela.ps1.
/// </summary>
public sealed class PreviaDaTelaTestes
{
    private static readonly string[] Etapas =
    {
        "Conferir se está tudo pronto", "Colocar os programas do reparo na pasta do SysPDV", "Parar o Firebird",
        "Apagar arquivos temporários", "Ligar o Firebird", "Verificar o banco de dados (1 de 3)", "Reiniciar o Firebird",
        "Consertar o banco de dados (2 de 3)", "Reiniciar o Firebird", "Verificar o banco de dados (3 de 3)",
        "Reiniciar o Firebird", "Fazer a cópia de segurança (backup)", "Parar o Firebird",
        "Guardar o banco atual na pasta de backup", "Ligar o Firebird", "Reconstruir o banco a partir do backup (restore)",
        "Reiniciar o Firebird", "Conferir o banco reconstruído", "Reativar os índices (VERIFICAINDICE)",
        "Abrir o monitor do banco (dbMonitor)", "Atualizar o banco de dados (atualizasyspdv_server)",
        "Abrir o sistema (syspdv_server)",
    };

    [Fact]
    public void Gera_previas_da_tela_quando_pedido()
    {
        var pasta = Environment.GetEnvironmentVariable("REPARO_PREVIA_DIR");
        if (string.IsNullOrWhiteSpace(pasta))
            return;

        Directory.CreateDirectory(pasta);
        Exception? erro = null;
        var thread = new Thread(() =>
        {
            try
            {
                foreach (var (nome, tema) in new[] { ("claro", Tema.Claro), ("escuro", Tema.EscuroPadrao) })
                {
                    Capturar(pasta, $"1-inicio-{nome}", tema, _ => { });
                    Capturar(pasta, $"2-erro-entrada-{nome}", tema, j => j.MostrarErroDeEntradaParaPrevia());
                    Capturar(pasta, $"3-andamento-{nome}", tema, j => Andamento(j, 10));
                    Capturar(pasta, $"4-sucesso-{nome}", tema, j =>
                    {
                        Andamento(j, Etapas.Length, terminarUltima: true);
                        j.MostrarResultado(true,
                            "Reparo concluído com sucesso.\n" +
                            @"O banco antigo foi guardado em: C:\SYSPDV\bkpBd\syspdv_srv_20261105_081636.fdb" + "\n" +
                            @"Cópia de segurança: C:\SYSPDV\bkpBd\syspdv_srv_20261105_081636.fbk");
                        j.MostrarLog(@"C:\SYSPDV\logs-reparo\reparo_20261105_081634.log");
                    });
                    Capturar(pasta, $"5-falha-{nome}", tema, j =>
                    {
                        Andamento(j, 16);
                        j.EtapaTerminou(16, StatusEtapa.Falha);
                        j.MostrarResultado(false,
                            "O reparo não foi concluído.\n" +
                            "A etapa \"Reconstruir o banco a partir do backup (restore)\" falhou: o restore não deu certo (código 1).\n" +
                            "O banco original foi devolvido ao lugar. Nada foi perdido.");
                        j.MostrarLog(@"C:\SYSPDV\logs-reparo\reparo_20261105_081634.log");
                    });
                    Capturar(pasta, $"6-sobre-{nome}", tema, j => j.AbrirAba(1));
                }
            }
            catch (Exception ex)
            {
                erro = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (erro is not null)
            throw erro;
    }

    private static void Andamento(JanelaPrincipal j, int ate, bool terminarUltima = false)
    {
        j.PrepararParaReparo(@"C:\SYSPDV\syspdv_srv.fdb");
        for (var i = 1; i <= ate; i++)
        {
            j.EtapaComecou(i, Etapas.Length, Etapas[i - 1]);
            if (i < ate || terminarUltima)
                j.EtapaTerminou(i, i is 2 or 6 ? StatusEtapa.Aviso : StatusEtapa.Ok);
        }
    }

    private static void Capturar(string pasta, string nome, Tema tema, Action<JanelaPrincipal> preparar)
    {
        using var janela = new JanelaPrincipal(@"C:\SYSPDV\syspdv_srv.fdb",
            _ => throw new InvalidOperationException("a prévia nunca inicia o reparo"), tema)
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-20000, -20000),
            ShowInTaskbar = false,
        };
        janela.Show();
        Application.DoEvents();
        preparar(janela);
        janela.PerformLayout();
        Application.DoEvents();

        using var imagem = new Bitmap(janela.ClientSize.Width, janela.ClientSize.Height);
        using (var g = Graphics.FromImage(imagem))
            g.Clear(tema.Fundo);
        foreach (Control filho in janela.Controls)
            filho.DrawToBitmap(imagem, filho.Bounds);
        imagem.Save(Path.Combine(pasta, nome + ".png"), ImageFormat.Png);
        janela.Close();
    }
}
