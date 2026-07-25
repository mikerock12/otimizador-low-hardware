using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace OtimizadorWin10
{
    public class MainForm : Form
    {
        HardwareInfo hw;
        List<Optimization> catalogo;
        int tierEscolhido = 2;
        HashSet<string> idsMarcados = new HashSet<string>();
        bool aplicando = false;
        bool otimizacaoAplicada = false;
        bool diagRodando = false;

        Panel painelTopo, painelConteudo, painelRodape;
        Label lblTitulo, lblPasso;
        Button btnVoltar, btnAvancar;
        int telaAtual = 0;

        static readonly Color CorFundo = Color.White;
        static readonly Color CorTopo = Color.FromArgb(23, 48, 77);
        static readonly Color CorAcento = Color.FromArgb(0, 102, 204);
        static readonly Color CorAviso = Color.FromArgb(180, 90, 0);

        public MainForm()
        {
            Text = "Otimizador Low Hardware - Windows 10/11";
            Font = new Font("Segoe UI", 9F);
            ClientSize = new Size(920, 650);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = CorFundo;
            try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            painelTopo = new Panel();
            painelTopo.Dock = DockStyle.Top;
            painelTopo.Height = 64;
            painelTopo.BackColor = CorTopo;

            lblTitulo = new Label();
            lblTitulo.ForeColor = Color.White;
            lblTitulo.Font = new Font("Segoe UI Semibold", 14F);
            lblTitulo.Location = new Point(20, 10);
            lblTitulo.AutoSize = true;
            lblTitulo.Text = "Otimizador Low Hardware";

            lblPasso = new Label();
            lblPasso.ForeColor = Color.FromArgb(170, 200, 230);
            lblPasso.Font = new Font("Segoe UI", 9F);
            lblPasso.Location = new Point(22, 38);
            lblPasso.AutoSize = true;

            painelTopo.Controls.Add(lblTitulo);
            painelTopo.Controls.Add(lblPasso);

            painelRodape = new Panel();
            painelRodape.Dock = DockStyle.Bottom;
            painelRodape.Height = 56;
            painelRodape.BackColor = Color.FromArgb(243, 243, 243);

            btnAvancar = BotaoPadrao("Continuar >", true);
            btnAvancar.Location = new Point(790, 12);
            btnAvancar.Click += delegate { Avancar(); };

            btnVoltar = BotaoPadrao("< Voltar", false);
            btnVoltar.Location = new Point(660, 12);
            btnVoltar.Click += delegate { Voltar(); };

            painelRodape.Controls.Add(btnAvancar);
            painelRodape.Controls.Add(btnVoltar);

            // Creditos permanentes no rodape
            var logoPeq = SplashForm.CarregarLogo();
            if (logoPeq != null)
            {
                var picLogo = new PictureBox();
                picLogo.Image = logoPeq;
                picLogo.SizeMode = PictureBoxSizeMode.Zoom;
                picLogo.Size = new Size(40, 40);
                picLogo.Location = new Point(12, 8);
                picLogo.Cursor = Cursors.Hand;
                picLogo.Click += delegate { SplashForm.AbrirSite(); };
                painelRodape.Controls.Add(picLogo);
            }

            var lblCredito = new Label();
            lblCredito.Text = "Criado por Maicon Nunes — Smells Like Tech Informática";
            lblCredito.Font = new Font("Segoe UI", 8.25F);
            lblCredito.ForeColor = Color.FromArgb(90, 90, 90);
            lblCredito.Location = new Point(60, 10);
            lblCredito.AutoSize = true;
            painelRodape.Controls.Add(lblCredito);

            var linkSite = new LinkLabel();
            linkSite.Text = SplashForm.SITE_TEXTO;
            linkSite.Font = new Font("Segoe UI", 8.25F);
            linkSite.Location = new Point(60, 28);
            linkSite.AutoSize = true;
            linkSite.LinkClicked += delegate { SplashForm.AbrirSite(); };
            painelRodape.Controls.Add(linkSite);

            painelConteudo = new Panel();
            painelConteudo.Dock = DockStyle.Fill;
            painelConteudo.Padding = new Padding(20);

            Controls.Add(painelConteudo);
            Controls.Add(painelTopo);
            Controls.Add(painelRodape);

            Load += delegate { IniciarDeteccao(); };
            FormClosing += delegate(object s, FormClosingEventArgs e)
            {
                if (aplicando || diagRodando)
                {
                    MessageBox.Show(aplicando
                        ? "Aguarde o fim da aplicacao das otimizacoes."
                        : "Aguarde o fim do diagnostico.", "Otimizador",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    e.Cancel = true;
                }
            };
        }

        Button BotaoPadrao(string texto, bool primario)
        {
            var b = new Button();
            b.Text = texto;
            b.Size = new Size(120, 32);
            b.FlatStyle = FlatStyle.Flat;
            if (primario)
            {
                b.BackColor = CorAcento; b.ForeColor = Color.White;
                b.FlatAppearance.BorderSize = 0;
            }
            else
            {
                b.BackColor = Color.White; b.ForeColor = Color.FromArgb(60, 60, 60);
                b.FlatAppearance.BorderColor = Color.FromArgb(180, 180, 180);
            }
            return b;
        }

        // =====================================================
        // Navegacao
        // =====================================================
        void Avancar()
        {
            if (telaAtual == 0) { MostrarTela(1); }
            else if (telaAtual == 1) { MostrarTela(2); }
            else if (telaAtual == 2) { ColherSelecao(); MostrarTela(3); }
            else if (telaAtual == 3) { MostrarTela(4); } // apos aplicar -> diagnostico
        }

        void Voltar()
        {
            if (telaAtual == 4) MostrarTela(0);
            else if (telaAtual > 0 && telaAtual <= 3) MostrarTela(telaAtual - 1);
        }

        void MostrarTela(int n)
        {
            telaAtual = n;
            painelConteudo.Controls.Clear();
            btnVoltar.Visible = n > 0;
            btnAvancar.Visible = n <= 2;
            btnAvancar.Text = "Continuar >";
            switch (n)
            {
                case 0: TelaHardware(); break;
                case 1: TelaPerfil(); break;
                case 2: TelaPersonalizacao(); break;
                case 3: TelaAplicacao(); break;
                case 4: TelaDiagnostico(); break;
            }
        }

        // =====================================================
        // Tela 0 - Deteccao de hardware
        // =====================================================
        void IniciarDeteccao()
        {
            lblPasso.Text = "Detectando hardware...";
            var t = new Thread(delegate()
            {
                var h = HardwareInfo.Detectar();
                BeginInvoke((Action)delegate
                {
                    hw = h;
                    catalogo = Catalog.Construir(hw);
                    tierEscolhido = hw.TierRecomendado();
                    if (!hw.SistemaSuportado)
                        MessageBox.Show(
                            "Este programa foi feito para Windows 10 e Windows 11. Seu sistema foi identificado como: " +
                            hw.WindowsNome + ".\r\nVoce pode continuar, mas alguns ajustes podem nao ter efeito.",
                            "Sistema diferente do esperado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    MostrarTela(0);
                });
            });
            t.IsBackground = true;
            t.Start();
        }

        void TelaHardware()
        {
            lblPasso.Text = "Passo 1 de 5 - Hardware detectado";

            var lbl = new Label();
            lbl.Text = "Especificacoes detectadas automaticamente. Upgrades (SSD, mais RAM) ja aparecem aqui e mudam quais otimizacoes fazem sentido para esta maquina.";
            lbl.Location = new Point(20, 15);
            lbl.Size = new Size(860, 36);
            painelConteudo.Controls.Add(lbl);

            var caixa = new TextBox();
            caixa.Multiline = true; caixa.ReadOnly = true;
            caixa.Font = new Font("Consolas", 10F);
            caixa.BackColor = Color.FromArgb(248, 249, 250);
            caixa.BorderStyle = BorderStyle.FixedSingle;
            caixa.Location = new Point(20, 58);
            caixa.Size = new Size(860, 130);
            caixa.Text = hw.Resumo();
            painelConteudo.Controls.Add(caixa);

            var lblNao = new Label();
            lblNao.Text = "O que este programa NAO faz: nao desativa o Windows Update, nao desativa o antivirus " +
                          "(Windows Defender), nao aplica ajustes instaveis. Antes de aplicar, um ponto de " +
                          "restauracao e criado e todas as mudancas ficam registradas para reversao.";
            lblNao.Location = new Point(20, 210);
            lblNao.Size = new Size(860, 60);
            lblNao.ForeColor = Color.FromArgb(90, 90, 90);
            painelConteudo.Controls.Add(lblNao);

            var linkDiag = new LinkLabel();
            linkDiag.Text = "Executar somente o diagnostico e nota da maquina (sem otimizar)";
            linkDiag.Location = new Point(20, 290);
            linkDiag.AutoSize = true;
            linkDiag.LinkClicked += delegate { MostrarTela(4); };
            painelConteudo.Controls.Add(linkDiag);

            string undo = Engine.UltimoArquivoUndo();
            if (undo != null)
            {
                var link = new LinkLabel();
                link.Text = "Reverter as alteracoes da ultima otimizacao aplicada";
                link.Location = new Point(20, 318);
                link.AutoSize = true;
                link.LinkClicked += delegate { ReverterUltima(undo); };
                painelConteudo.Controls.Add(link);
            }
        }

        void ReverterUltima(string undoFile)
        {
            var r = MessageBox.Show(
                "Restaurar os servicos, chaves de registro e configuracoes ao estado anterior a ultima otimizacao?\r\n\r\n" +
                "(Apps desinstalados nao voltam automaticamente - podem ser reinstalados pela Microsoft Store.)",
                "Reverter alteracoes", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return;
            try
            {
                var linhas = new List<string>();
                int n = Engine.Reverter(undoFile, delegate(string s) { linhas.Add(s); });
                MessageBox.Show(n + " itens restaurados. Reinicie o computador para concluir.",
                    "Reversao concluida", MessageBoxButtons.OK, MessageBoxIcon.Information);
                MostrarTela(0);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Falha na reversao: " + ex.Message, "Erro",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // =====================================================
        // Tela 1 - Escolha do perfil
        // =====================================================
        void TelaPerfil()
        {
            lblPasso.Text = "Passo 2 de 5 - Escolha o nivel de otimizacao";
            int recomendado = hw.TierRecomendado();

            var lbl = new Label();
            lbl.Text = "Com base no hardware detectado, o nivel recomendado esta destacado. Em qualquer nivel voce vera e podera personalizar cada item antes de aplicar.";
            lbl.Location = new Point(20, 15);
            lbl.Size = new Size(860, 34);
            painelConteudo.Controls.Add(lbl);

            string[] nomes = { "Leve", "Master", "Ultra" };
            string[] descr = {
                "Ajustes seguros e invisiveis no dia a dia: telemetria, apps em segundo plano, efeitos visuais, sugestoes/anuncios, limpeza de temporarios." +
                "\r\nIdeal para: maquinas ja atualizadas (SSD + 8 GB RAM) ou quem nao quer abrir mao de nada.",
                "Tudo do Leve + servicos que a maioria nao usa (Fax, Xbox, biometria, telefonia...), remocao de apps nativos dispensaveis, indexacao (em HDD), plano de alto desempenho, economia de RAM." +
                "\r\nIdeal para: a maioria das maquinas antigas.",
                "Tudo do Master + remocao ampla de apps nativos, hibernacao, prefetch (SSD), fila de impressao e diagnosticos (opcionais)." +
                "\r\nIdeal para: maquinas muito limitadas (2-4 GB RAM + HDD) que precisam de cada MB."
            };

            int y = 60;
            for (int i = 0; i < 3; i++)
            {
                int tier = i + 1;
                var card = new Panel();
                card.Location = new Point(20, y);
                card.Size = new Size(860, 108);
                card.BorderStyle = BorderStyle.FixedSingle;
                card.BackColor = tier == tierEscolhido ? Color.FromArgb(232, 242, 252) : Color.White;
                card.Tag = tier;

                var rb = new RadioButton();
                rb.Text = nomes[i] + (tier == recomendado ? "   (RECOMENDADO para seu hardware)" : "");
                rb.Font = new Font("Segoe UI Semibold", 11F);
                rb.ForeColor = tier == recomendado ? CorAcento : Color.FromArgb(40, 40, 40);
                rb.Location = new Point(14, 8);
                rb.AutoSize = true;
                rb.Checked = tier == tierEscolhido;
                rb.Tag = tier;
                rb.CheckedChanged += PerfilMudou;

                var d = new Label();
                d.Text = descr[i];
                d.Location = new Point(34, 36);
                d.Size = new Size(810, 66);
                d.ForeColor = Color.FromArgb(80, 80, 80);

                card.Controls.Add(rb);
                card.Controls.Add(d);
                painelConteudo.Controls.Add(card);
                y += 120;
            }
        }

        void PerfilMudou(object sender, EventArgs e)
        {
            var rb = (RadioButton)sender;
            if (!rb.Checked) return;
            tierEscolhido = (int)rb.Tag;
            foreach (Control c in painelConteudo.Controls)
            {
                var p = c as Panel;
                if (p != null && p.Tag is int)
                    p.BackColor = (int)p.Tag == tierEscolhido ? Color.FromArgb(232, 242, 252) : Color.White;
            }
        }

        // =====================================================
        // Tela 2 - Personalizacao
        // =====================================================
        ListView lvItens;
        TextBox txtDetalhe;
        Label lblContagem;

        void TelaPersonalizacao()
        {
            lblPasso.Text = string.Format("Passo 3 de 5 - Personalize (perfil {0})",
                tierEscolhido == 1 ? "Leve" : (tierEscolhido == 2 ? "Master" : "Ultra"));

            var lbl = new Label();
            lbl.Text = "Marque/desmarque livremente. Clique em um item para ver exatamente o que sera feito. Itens em laranja tem alguma observacao importante.";
            lbl.Location = new Point(20, 12);
            lbl.Size = new Size(860, 32);
            painelConteudo.Controls.Add(lbl);

            lvItens = new ListView();
            lvItens.View = View.Details;
            lvItens.CheckBoxes = true;
            lvItens.FullRowSelect = true;
            lvItens.HideSelection = false;
            lvItens.Location = new Point(20, 48);
            lvItens.Size = new Size(520, 440);
            lvItens.Columns.Add("Otimizacao", 480);
            lvItens.HeaderStyle = ColumnHeaderStyle.None;

            var aplicaveis = catalogo.Where(o => o.AplicaA(hw)).ToList();
            var categorias = aplicaveis.Select(o => o.Categoria).Distinct().ToList();
            var grupos = new Dictionary<string, ListViewGroup>();
            foreach (string cat in categorias)
            {
                var g = new ListViewGroup(cat);
                grupos[cat] = g;
                lvItens.Groups.Add(g);
            }

            lvItens.ItemChecked += delegate { AtualizarContagem(); };
            lvItens.SelectedIndexChanged += delegate
            {
                if (lvItens.SelectedItems.Count > 0)
                {
                    var o = (Optimization)lvItens.SelectedItems[0].Tag;
                    txtDetalhe.Text = o.DescricaoCompleta();
                }
            };

            foreach (var o in aplicaveis)
            {
                var it = new ListViewItem(o.Nome, grupos[o.Categoria]);
                it.Tag = o;
                bool marcada = o.Tier <= tierEscolhido && !o.DesmarcadaPorPadrao;
                if (idsMarcados.Count > 0) marcada = idsMarcados.Contains(o.Id);
                it.Checked = marcada;
                if (!string.IsNullOrEmpty(o.Aviso)) it.ForeColor = CorAviso;
                lvItens.Items.Add(it);
            }
            painelConteudo.Controls.Add(lvItens);

            txtDetalhe = new TextBox();
            txtDetalhe.Multiline = true; txtDetalhe.ReadOnly = true;
            txtDetalhe.ScrollBars = ScrollBars.Vertical;
            txtDetalhe.BackColor = Color.FromArgb(248, 249, 250);
            txtDetalhe.BorderStyle = BorderStyle.FixedSingle;
            txtDetalhe.Location = new Point(552, 48);
            txtDetalhe.Size = new Size(330, 440);
            txtDetalhe.Text = "Selecione um item a esquerda para ver os detalhes do que sera feito.";
            painelConteudo.Controls.Add(txtDetalhe);

            lblContagem = new Label();
            lblContagem.Location = new Point(20, 496);
            lblContagem.AutoSize = true;
            lblContagem.Font = new Font("Segoe UI Semibold", 9F);
            painelConteudo.Controls.Add(lblContagem);
            AtualizarContagem();
        }

        void AtualizarContagem()
        {
            if (lvItens == null || lblContagem == null) return;
            int n = lvItens.CheckedItems.Count;
            lblContagem.Text = n + " otimizacao(oes) selecionada(s) de " + lvItens.Items.Count + " disponiveis.";
        }

        void ColherSelecao()
        {
            idsMarcados.Clear();
            foreach (ListViewItem it in lvItens.CheckedItems)
                idsMarcados.Add(((Optimization)it.Tag).Id);
        }

        // =====================================================
        // Tela 3 - Aplicacao
        // =====================================================
        TextBox txtLog;
        ProgressBar barra;
        CheckBox chkRestore;
        Button btnAplicar;

        void TelaAplicacao()
        {
            lblPasso.Text = "Passo 4 de 5 - Aplicar";

            var selecionadas = SelecionadasAtuais();

            var lbl = new Label();
            lbl.Text = string.Format("{0} otimizacao(oes) serao aplicadas. Recomendamos manter o ponto de restauracao marcado - se qualquer coisa nao agradar, da para voltar atras por ele ou pelo botao de reversao deste programa.",
                selecionadas.Count);
            lbl.Location = new Point(20, 12);
            lbl.Size = new Size(860, 36);
            painelConteudo.Controls.Add(lbl);

            chkRestore = new CheckBox();
            chkRestore.Text = "Criar ponto de restauracao do sistema antes de aplicar (recomendado)";
            chkRestore.Checked = true;
            chkRestore.Location = new Point(20, 52);
            chkRestore.AutoSize = true;
            painelConteudo.Controls.Add(chkRestore);

            btnAplicar = BotaoPadrao("APLICAR AGORA", true);
            btnAplicar.Size = new Size(180, 38);
            btnAplicar.Location = new Point(20, 84);
            btnAplicar.Click += delegate { Aplicar(); };
            painelConteudo.Controls.Add(btnAplicar);

            barra = new ProgressBar();
            barra.Location = new Point(220, 90);
            barra.Size = new Size(660, 26);
            painelConteudo.Controls.Add(barra);

            txtLog = new TextBox();
            txtLog.Multiline = true; txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.Font = new Font("Consolas", 9F);
            txtLog.BackColor = Color.FromArgb(30, 30, 30);
            txtLog.ForeColor = Color.FromArgb(220, 220, 220);
            txtLog.Location = new Point(20, 134);
            txtLog.Size = new Size(860, 356);
            painelConteudo.Controls.Add(txtLog);
        }

        List<Optimization> SelecionadasAtuais()
        {
            return catalogo.Where(o => o.AplicaA(hw) && idsMarcados.Contains(o.Id)).ToList();
        }

        void Aplicar()
        {
            var selecionadas = SelecionadasAtuais();
            if (selecionadas.Count == 0)
            {
                MessageBox.Show("Nenhuma otimizacao selecionada.", "Otimizador",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            aplicando = true;
            btnAplicar.Enabled = false;
            btnVoltar.Visible = false;
            chkRestore.Enabled = false;
            barra.Maximum = selecionadas.Count;
            bool restore = chkRestore.Checked;

            var t = new Thread(delegate()
            {
                Action<string> log = delegate(string s)
                {
                    BeginInvoke((Action)delegate { txtLog.AppendText(s + "\r\n"); });
                };
                Action<int, int> prog = delegate(int feito, int total)
                {
                    BeginInvoke((Action)delegate { barra.Value = Math.Min(feito, barra.Maximum); });
                };
                ApplyResult res = Engine.Aplicar(selecionadas, restore, log, prog);
                BeginInvoke((Action)delegate { Finalizar(res); });
            });
            t.IsBackground = true;
            t.Start();
        }

        void Finalizar(ApplyResult res)
        {
            aplicando = false;
            otimizacaoAplicada = true;
            lblPasso.Text = "Otimizacao concluida";
            txtLog.AppendText("\r\n==========================================\r\n");
            txtLog.AppendText(string.Format("CONCLUIDO: {0} acoes aplicadas, {1} falha(s), {2} aviso(s) nao criticos.\r\n",
                res.Sucesso, res.Falhas, res.Avisos));
            txtLog.AppendText("Proximo passo: diagnostico e nota da maquina. Reinicie o computador ao final.\r\n");
            if (res.ArquivoLog != null) txtLog.AppendText("Log salvo em: " + res.ArquivoLog + "\r\n");

            btnAplicar.Text = "Concluido";
            btnAvancar.Text = "Diagnostico >";
            btnAvancar.Visible = true;
        }

        // =====================================================
        // Tela 4 - Diagnostico e nota da maquina
        // =====================================================
        TextBox txtDiag;
        ProgressBar barraDiag;
        Button btnDiag, btnReiniciar;
        Label lblNota, lblVeredito;

        void TelaDiagnostico()
        {
            lblPasso.Text = "Passo 5 de 5 - Diagnostico e nota da maquina";

            var lbl = new Label();
            lbl.Text = "Teste rapido de RAM, saude do disco (SMART: horas ligado, temperatura, desgaste), benchmark com " +
                       "nota de 1 a 10 e, em HDD, verificacao chkdsk do sistema de arquivos (somente leitura). " +
                       "Leva de 1 a 10 minutos; evite usar o computador durante o teste.";
            lbl.Location = new Point(20, 12);
            lbl.Size = new Size(860, 36);
            painelConteudo.Controls.Add(lbl);

            btnDiag = BotaoPadrao("INICIAR DIAGNOSTICO", true);
            btnDiag.Size = new Size(180, 38);
            btnDiag.Location = new Point(20, 56);
            btnDiag.Click += delegate { RodarDiagnostico(); };
            painelConteudo.Controls.Add(btnDiag);

            barraDiag = new ProgressBar();
            barraDiag.Location = new Point(220, 62);
            barraDiag.Size = new Size(660, 26);
            barraDiag.Maximum = 100;
            painelConteudo.Controls.Add(barraDiag);

            txtDiag = new TextBox();
            txtDiag.Multiline = true; txtDiag.ReadOnly = true;
            txtDiag.ScrollBars = ScrollBars.Vertical;
            txtDiag.Font = new Font("Consolas", 9F);
            txtDiag.BackColor = Color.FromArgb(30, 30, 30);
            txtDiag.ForeColor = Color.FromArgb(220, 220, 220);
            txtDiag.Location = new Point(20, 106);
            txtDiag.Size = new Size(560, 384);
            painelConteudo.Controls.Add(txtDiag);

            var painelNota = new Panel();
            painelNota.Location = new Point(596, 106);
            painelNota.Size = new Size(286, 384);
            painelNota.BorderStyle = BorderStyle.FixedSingle;
            painelNota.BackColor = Color.FromArgb(248, 249, 250);

            var lblNotaTit = new Label();
            lblNotaTit.Text = "NOTA DA MAQUINA";
            lblNotaTit.Font = new Font("Segoe UI Semibold", 10F);
            lblNotaTit.TextAlign = ContentAlignment.MiddleCenter;
            lblNotaTit.Size = new Size(282, 30);
            lblNotaTit.Location = new Point(0, 14);
            painelNota.Controls.Add(lblNotaTit);

            lblNota = new Label();
            lblNota.Text = "-";
            lblNota.Font = new Font("Segoe UI", 44F, FontStyle.Bold);
            lblNota.TextAlign = ContentAlignment.MiddleCenter;
            lblNota.Size = new Size(282, 90);
            lblNota.Location = new Point(0, 46);
            lblNota.ForeColor = Color.FromArgb(120, 120, 120);
            painelNota.Controls.Add(lblNota);

            lblVeredito = new Label();
            lblVeredito.Text = "Clique em INICIAR DIAGNOSTICO.";
            lblVeredito.TextAlign = ContentAlignment.TopCenter;
            lblVeredito.Size = new Size(266, 200);
            lblVeredito.Location = new Point(8, 142);
            painelNota.Controls.Add(lblVeredito);

            btnReiniciar = BotaoPadrao("Reiniciar agora", false);
            btnReiniciar.Size = new Size(150, 32);
            btnReiniciar.Location = new Point(66, 344);
            btnReiniciar.Visible = false;
            btnReiniciar.Click += delegate
            {
                try
                {
                    System.Diagnostics.Process.Start("shutdown.exe",
                        "/r /t 15 /c \"Reiniciando para concluir a otimizacao\"");
                    Application.Exit();
                }
                catch { }
            };
            painelNota.Controls.Add(btnReiniciar);

            painelConteudo.Controls.Add(painelNota);
        }

        void RodarDiagnostico()
        {
            if (diagRodando) return;
            diagRodando = true;
            btnDiag.Enabled = false;
            btnVoltar.Enabled = false;
            txtDiag.Clear();

            var t = new Thread(delegate()
            {
                Action<string> log = delegate(string s)
                {
                    BeginInvoke((Action)delegate { txtDiag.AppendText(s + "\r\n"); });
                };
                Action<int> pct = delegate(int p)
                {
                    BeginInvoke((Action)delegate { barraDiag.Value = Math.Min(100, p); });
                };
                DiagResult res = null;
                try { res = Diagnostics.Executar(hw, log, pct); }
                catch (Exception ex) { log("ERRO no diagnostico: " + ex.Message); }
                BeginInvoke((Action)delegate { DiagnosticoConcluido(res); });
            });
            t.IsBackground = true;
            t.Start();
        }

        void DiagnosticoConcluido(DiagResult res)
        {
            diagRodando = false;
            btnDiag.Enabled = true;
            btnDiag.Text = "Repetir diagnostico";
            btnVoltar.Enabled = true;
            if (res == null) return;

            txtDiag.AppendText("\r\n" + Diagnostics.Relatorio(res));

            lblNota.Text = res.NotaFinal.ToString("0.0");
            if (res.NotaFinal < 3 || res.RamErros > 0 || res.SaudeNivel == 2)
                lblNota.ForeColor = Color.FromArgb(190, 40, 40);
            else if (res.NotaFinal < 5)
                lblNota.ForeColor = CorAviso;
            else if (res.NotaFinal < 7)
                lblNota.ForeColor = Color.FromArgb(180, 140, 0);
            else
                lblNota.ForeColor = Color.FromArgb(30, 140, 60);

            lblVeredito.Text = string.Format(
                "CPU: {0:0.0}   RAM: {1:0.0}   Disco: {2:0.0}\r\n\r\n{3}",
                res.NotaCpu, res.NotaRam, res.NotaDisco, res.Veredito);

            if (otimizacaoAplicada) btnReiniciar.Visible = true;

            // Salva o relatorio junto aos logs
            try
            {
                string caminho = System.IO.Path.Combine(Engine.PastaDados,
                    "diagnostico_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
                System.IO.File.WriteAllText(caminho,
                    hw.Resumo() + "\r\n\r\n" + txtDiag.Text, System.Text.Encoding.UTF8);
                txtDiag.AppendText("Relatorio salvo em: " + caminho + "\r\n");
            }
            catch { }
        }
    }
}
