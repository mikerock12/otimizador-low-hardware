using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace OtimizadorWin10
{
    // Tela de abertura: logo da Smells Like Tech + credito.
    // Dura 5 segundos com fade in e fade out; um clique pula
    // (clicar no site abre o navegador sem fechar a abertura).
    public class SplashForm : Form
    {
        public const string SITE_URL = "https://www.smellsliketech.com.br";
        public const string SITE_TEXTO = "www.smellsliketech.com.br";

        const int DURACAO_TOTAL_MS = 5000;
        const int FADE_MS = 700;

        Timer timer;
        Stopwatch relogio;
        bool pulando;

        public SplashForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(480, 470);
            BackColor = Color.FromArgb(12, 12, 12);
            ShowInTaskbar = false;
            TopMost = true;
            Opacity = 0;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            Image logo = CarregarLogo();
            if (logo != null)
            {
                var pic = new PictureBox();
                pic.Image = logo;
                pic.SizeMode = PictureBoxSizeMode.Zoom;
                pic.Size = new Size(300, 300);
                pic.Location = new Point((ClientSize.Width - 300) / 2, 28);
                pic.BackColor = Color.Transparent;
                Controls.Add(pic);
            }

            var lblTitulo = new Label();
            lblTitulo.Text = "Otimizador Low Hardware";
            lblTitulo.Font = new Font("Segoe UI Semibold", 15F);
            lblTitulo.ForeColor = Color.FromArgb(255, 210, 74);
            lblTitulo.TextAlign = ContentAlignment.MiddleCenter;
            lblTitulo.Size = new Size(ClientSize.Width, 34);
            lblTitulo.Location = new Point(0, 338);
            Controls.Add(lblTitulo);

            var lblCredito = new Label();
            lblCredito.Text = "Criado por Maicon Nunes, da Smells Like Tech Informática.";
            lblCredito.Font = new Font("Segoe UI", 10F);
            lblCredito.ForeColor = Color.FromArgb(225, 225, 225);
            lblCredito.TextAlign = ContentAlignment.MiddleCenter;
            lblCredito.Size = new Size(ClientSize.Width, 26);
            lblCredito.Location = new Point(0, 380);
            Controls.Add(lblCredito);

            var linkSite = new LinkLabel();
            linkSite.Text = "Contato site: " + SITE_TEXTO;
            linkSite.Font = new Font("Segoe UI", 10F);
            linkSite.LinkColor = Color.FromArgb(120, 190, 255);
            linkSite.ActiveLinkColor = Color.FromArgb(180, 220, 255);
            linkSite.TextAlign = ContentAlignment.MiddleCenter;
            linkSite.Size = new Size(ClientSize.Width, 26);
            linkSite.Location = new Point(0, 406);
            linkSite.LinkClicked += delegate { AbrirSite(); };
            Controls.Add(linkSite);

            // Borda amarela fina
            Paint += delegate(object s, PaintEventArgs e)
            {
                using (var pen = new Pen(Color.FromArgb(255, 210, 74), 2))
                    e.Graphics.DrawRectangle(pen, 1, 1, ClientSize.Width - 3, ClientSize.Height - 3);
            };

            // Clique fora do site pula direto para o fade out
            EventHandler pular = delegate { Pular(); };
            Click += pular;
            lblTitulo.Click += pular;
            lblCredito.Click += pular;
            foreach (Control c in Controls)
                if (c is PictureBox) c.Click += pular;

            relogio = Stopwatch.StartNew();
            timer = new Timer();
            timer.Interval = 30;
            timer.Tick += delegate { AtualizarFade(); };
            timer.Start();
        }

        long _offsetMs = 0;

        void AtualizarFade()
        {
            long t = _offsetMs + relogio.ElapsedMilliseconds;
            if (t >= DURACAO_TOTAL_MS)
            {
                timer.Stop();
                Close();
                return;
            }
            double op;
            if (t < FADE_MS) op = t / (double)FADE_MS;                                  // fade in
            else if (t > DURACAO_TOTAL_MS - FADE_MS)
                op = (DURACAO_TOTAL_MS - t) / (double)FADE_MS;                          // fade out
            else op = 1.0;                                                              // visivel
            if (op < 0) op = 0; if (op > 1) op = 1;
            Opacity = op;
        }

        void Pular()
        {
            if (pulando) return;
            pulando = true;
            // Salta o relogio direto para o inicio do fade out
            long t = _offsetMs + relogio.ElapsedMilliseconds;
            long inicioFadeOut = DURACAO_TOTAL_MS - FADE_MS;
            if (t < inicioFadeOut)
            {
                _offsetMs = inicioFadeOut;
                relogio = Stopwatch.StartNew();
            }
        }

        public static void AbrirSite()
        {
            try { Process.Start(SITE_URL); } catch { }
        }

        public static Image CarregarLogo()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                Stream st = asm.GetManifestResourceStream("logo.png");
                if (st != null) return Image.FromStream(st);
            }
            catch { }
            // Fallback: arquivo ao lado do exe (caso compilado sem o recurso)
            try
            {
                string p = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "logo.png");
                if (File.Exists(p)) return Image.FromFile(p);
            }
            catch { }
            return null;
        }
    }
}
