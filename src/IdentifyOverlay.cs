using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace TermServMultiScreen
{
    /// <summary>
    /// Affiche un grand numéro sur chaque écran physique : la vérification ultime que le plan
    /// de l'application correspond bien à la réalité.
    /// </summary>
    public sealed class IdentifyOverlay : Form
    {
        private static readonly List<IdentifyOverlay> Open = new List<IdentifyOverlay>();
        private static Timer _timer;
        private static Action _onClosed;

        private readonly MonitorInfo _monitor;
        private readonly bool _selected;
        private readonly int _rdpId;

        private IdentifyOverlay(MonitorInfo monitor, bool selected, int rdpId)
        {
            _monitor = monitor;
            _selected = selected;
            _rdpId = rdpId;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            KeyPreview = true;
            Opacity = 0.90;
            DoubleBuffered = true;
            BackColor = selected ? Color.FromArgb(0, 90, 160) : Color.FromArgb(38, 38, 42);
            Bounds = monitor.WindowBounds.Width > 0 ? monitor.WindowBounds : new Rectangle(0, 0, 600, 400);
            Text = "Identification écran " + monitor.Order;
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }   // ne vole pas le focus a la fenetre principale
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int h = ClientSize.Height;
            int bigSize = Math.Max(48, Math.Min(420, (int)(h * 0.34)));
            int midSize = Math.Max(16, (int)(h * 0.055));
            int smallSize = Math.Max(12, (int)(h * 0.032));

            using (var big = new Font(FontFamily.GenericSansSerif, bigSize, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var mid = new Font(FontFamily.GenericSansSerif, midSize, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var small = new Font(FontFamily.GenericSansSerif, smallSize, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                var lines = new List<KeyValuePair<string, Font>>();
                lines.Add(new KeyValuePair<string, Font>(_monitor.Order.ToString(), big));
                lines.Add(new KeyValuePair<string, Font>(_monitor.PositionLabel.ToUpperInvariant(), mid));
                lines.Add(new KeyValuePair<string, Font>("Numéro Windows : " + _monitor.WindowsNumber + "     " + _monitor.ResolutionText, small));
                lines.Add(new KeyValuePair<string, Font>("Identifiant RDP : " + _rdpId + (_monitor.IsPrimary ? "     ÉCRAN PRINCIPAL" : ""), small));
                lines.Add(new KeyValuePair<string, Font>(_selected ? "▲  UTILISÉ PAR LA SESSION  ▲" : "non utilisé", mid));
                lines.Add(new KeyValuePair<string, Font>("Cliquez ou appuyez sur une touche pour fermer", small));

                int total = 0;
                foreach (var line in lines) total += TextRenderer.MeasureText(line.Key, line.Value).Height + 6;

                int y = (h - total) / 2;
                foreach (var line in lines)
                {
                    var size = TextRenderer.MeasureText(line.Key, line.Value);
                    TextRenderer.DrawText(g, line.Key, line.Value,
                        new Rectangle(0, y, ClientSize.Width, size.Height), Color.White,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
                    y += size.Height + 6;
                }
            }
        }

        protected override void OnMouseDown(MouseEventArgs e) { CloseAll(); }
        protected override void OnKeyDown(KeyEventArgs e) { CloseAll(); }

        /// <summary>Affiche les pastilles sur tous les ecrans pendant quelques secondes (non bloquant).</summary>
        public static void ShowAll(IList<MonitorInfo> monitors, IList<MonitorInfo> selection, AppConfig config, int milliseconds, Action onClosed)
        {
            CloseAll();
            _onClosed = onClosed;

            foreach (var m in monitors)
            {
                bool selected = selection != null && selection.Contains(m);
                var overlay = new IdentifyOverlay(m, selected, RdpFile.EffectiveRdpId(m, config));
                Open.Add(overlay);
                overlay.Show();
            }

            _timer = new Timer();
            _timer.Interval = Math.Max(800, milliseconds);
            _timer.Tick += delegate { CloseAll(); };
            _timer.Start();
        }

        public static bool AnyOpen
        {
            get { return Open.Count > 0; }
        }

        public static void CloseAll()
        {
            if (_timer != null)
            {
                _timer.Stop();
                _timer.Dispose();
                _timer = null;
            }
            var copy = new List<IdentifyOverlay>(Open);
            Open.Clear();
            foreach (var f in copy)
            {
                try { f.Close(); f.Dispose(); }
                catch (Exception ex) { Log.Write("Fermeture pastille : " + ex.Message); }
            }
            var callback = _onClosed;
            _onClosed = null;
            if (callback != null && copy.Count > 0) callback();
        }
    }
}
