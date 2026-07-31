using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace TermServMultiScreen
{
    /// <summary>Fenêtre de texte simple : aperçu du .rdp, diagnostic des écrans.</summary>
    public sealed class TextDialog : Form
    {
        public TextDialog(string title, string content)
        {
            Text = title;
            Width = 780;
            Height = 580;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            ShowIcon = false;
            Font = new Font("Segoe UI", 9f);

            var box = new TextBox();
            box.Multiline = true;
            box.ReadOnly = true;
            box.ScrollBars = ScrollBars.Both;
            box.WordWrap = false;
            box.Font = new Font("Consolas", 9.5f);
            box.Text = content;
            box.Dock = DockStyle.Fill;
            box.Select(0, 0);

            var panel = new Panel();
            panel.Dock = DockStyle.Bottom;
            panel.Height = 46;

            var copy = new Button();
            copy.Text = "Copier";
            copy.SetBounds(8, 8, 110, 30);
            copy.Click += delegate
            {
                try { if (!string.IsNullOrEmpty(box.Text)) Clipboard.SetText(box.Text); }
                catch (Exception ex) { Log.Write("Copie impossible : " + ex.Message); }
            };

            var close = new Button();
            close.Text = "Fermer";
            close.SetBounds(panel.Width - 118, 8, 110, 30);
            close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            close.DialogResult = DialogResult.OK;

            panel.Controls.Add(copy);
            panel.Controls.Add(close);
            Controls.Add(box);
            Controls.Add(panel);
            AcceptButton = close;
            CancelButton = close;
        }
    }

    /// <summary>
    /// Correspondance manuelle écran → identifiant mstsc. Soupape de sécurité : si une machine
    /// numérote ses écrans autrement, on corrige ici une fois pour toutes.
    /// </summary>
    public sealed class MappingDialog : Form
    {
        private readonly AppConfig _config;
        private readonly List<MonitorInfo> _monitors;
        private readonly CheckBox _enable = new CheckBox();
        private readonly List<NumericUpDown> _spins = new List<NumericUpDown>();

        public MappingDialog(AppConfig config, List<MonitorInfo> monitors)
        {
            _config = config;
            _monitors = MonitorEnumerator.LeftToRight(monitors);

            Text = "Correspondance des identifiants mstsc";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            Font = new Font("Segoe UI", 9f);
            Width = 630;

            var intro = new Label();
            intro.Text = "L'application déduit ces identifiants de Windows, et cela fonctionne dans la quasi-totalité\r\n"
                       + "des cas. Ne changez ces valeurs que si la session s'ouvre sur le mauvais écran.\r\n"
                       + "Le bouton « Liste mstsc » affiche la liste des écrans telle que mstsc.exe la voit.";
            intro.SetBounds(14, 12, 590, 54);

            _enable.Text = "Utiliser une correspondance manuelle";
            _enable.Checked = config.ManualMappingEnabled;
            _enable.SetBounds(14, 70, 320, 24);
            _enable.CheckedChanged += delegate { foreach (var s in _spins) s.Enabled = _enable.Checked; };

            Controls.Add(intro);
            Controls.Add(_enable);

            int y = 102;
            foreach (var m in _monitors)
            {
                var label = new Label();
                label.Text = string.Format("Écran {0} — {1} (Windows {2}, {3})", m.Order, m.PositionLabel, m.WindowsNumber, m.ResolutionText);
                label.SetBounds(28, y + 4, 400, 22);

                var spin = new NumericUpDown();
                spin.Minimum = 0;
                spin.Maximum = Math.Max(31, _monitors.Count - 1);
                spin.Value = RdpFile.EffectiveRdpId(m, config);
                spin.SetBounds(446, y, 70, 24);
                spin.Enabled = _enable.Checked;
                spin.Tag = m;

                _spins.Add(spin);
                Controls.Add(label);
                Controls.Add(spin);
                y += 34;
            }

            var listButton = new Button();
            listButton.Text = "Liste mstsc";
            listButton.SetBounds(14, y + 12, 120, 30);
            listButton.Click += delegate
            {
                try { Process.Start("mstsc.exe", "/l"); }
                catch (Exception ex) { MessageBox.Show(this, "Impossible de lancer mstsc /l : " + ex.Message); }
            };

            var reset = new Button();
            reset.Text = "Valeurs automatiques";
            reset.SetBounds(142, y + 12, 160, 30);
            reset.Click += delegate
            {
                foreach (var spin in _spins) spin.Value = ((MonitorInfo)spin.Tag).RdpId;
                _enable.Checked = false;
            };

            var ok = new Button();
            ok.Text = "Valider";
            ok.SetBounds(390, y + 12, 100, 30);
            ok.DialogResult = DialogResult.OK;

            var cancel = new Button();
            cancel.Text = "Annuler";
            cancel.SetBounds(498, y + 12, 100, 30);
            cancel.DialogResult = DialogResult.Cancel;

            Controls.Add(listButton);
            Controls.Add(reset);
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
            ClientSize = new Size(614, y + 56);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (DialogResult == DialogResult.OK)
            {
                var ids = _spins.Select(s => (int)s.Value).ToList();
                if (_enable.Checked && ids.Distinct().Count() != ids.Count)
                {
                    MessageBox.Show(this, "Deux écrans ne peuvent pas avoir le même identifiant RDP.",
                        "Correspondance", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    e.Cancel = true;
                    return;
                }
                _config.ManualMappingEnabled = _enable.Checked;
                foreach (var spin in _spins)
                    _config.SetManualRdpId(((MonitorInfo)spin.Tag).StableKey, (int)spin.Value);
                _config.Save();
                Log.Write("Correspondance mstsc : " + (_enable.Checked ? "manuelle — " + string.Join(", ",
                    _spins.Select(s => "écran " + ((MonitorInfo)s.Tag).Order + " → id " + (int)s.Value).ToArray()) : "automatique"));
            }
            base.OnFormClosing(e);
        }
    }
}
