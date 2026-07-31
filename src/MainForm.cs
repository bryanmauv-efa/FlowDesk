using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TermServMultiScreen
{
    public sealed class MainForm : Form
    {
        private readonly AppConfig _config;
        private List<MonitorInfo> _monitors;
        private bool _loading;

        private readonly ComboBox _cbProfiles = new ComboBox();
        private readonly TextBox _txtServer = new TextBox();
        private readonly TextBox _txtUser = new TextBox();

        private readonly MonitorMapControl _map = new MonitorMapControl();
        private readonly Label _lblSelection = new Label();
        private readonly Label _lblWarning = new Label();
        private readonly Label _lblStatus = new Label();
        private readonly Button _btnIdentify = new Button();
        private readonly Panel _presets = new Panel();

        private readonly CheckBox _chkClipboard = new CheckBox();
        private readonly CheckBox _chkPrinters = new CheckBox();
        private readonly CheckBox _chkSound = new CheckBox();
        private readonly CheckBox _chkDrives = new CheckBox();
        private readonly CheckBox _chkBar = new CheckBox();
        private readonly CheckBox _chkReconnect = new CheckBox();
        private readonly ComboBox _cbKeyboard = new ComboBox();
        private readonly CheckBox _chkWindowedSingle = new CheckBox();
        private readonly CheckBox _chkMultimonAll = new CheckBox();

        public MainForm(AppConfig config, List<MonitorInfo> monitors)
        {
            _config = config;
            _monitors = monitors;
            BuildUi();
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        }

        // ------------------------------------------------------------------ interface

        private void BuildUi()
        {
            Text = "Bureau à distance multi-écrans";
            ClientSize = new Size(988, 700);
            MinimumSize = new Size(900, 660);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);
            KeyPreview = true;
            try
            {
                string mstsc = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "mstsc.exe");
                if (File.Exists(mstsc)) Icon = Icon.ExtractAssociatedIcon(mstsc);
            }
            catch (Exception ex) { Log.Write("Icône : " + ex.Message); }

            // ---- ligne 1 : connexions enregistrées
            Controls.Add(MakeLabel("Connexion", 12, 17, 74));
            _cbProfiles.SetBounds(90, 14, 286, 24);
            _cbProfiles.DropDownStyle = ComboBoxStyle.DropDownList;
            _cbProfiles.SelectedIndexChanged += delegate { if (!_loading) ApplySelectedProfile(); };
            Controls.Add(_cbProfiles);

            Controls.Add(MakeButton("Nouvelle…", 384, 13, 96, 26, OnNewProfile));
            Controls.Add(MakeButton("Enregistrer", 486, 13, 100, 26, OnSaveProfile));
            Controls.Add(MakeButton("Supprimer", 592, 13, 96, 26, OnDeleteProfile));
            Controls.Add(MakeButton("Importer un .rdp…", 694, 13, 146, 26, OnImportRdp));

            // ---- ligne 2 : serveur et utilisateur
            Controls.Add(MakeLabel("Serveur", 12, 51, 74));
            _txtServer.SetBounds(90, 48, 286, 24);
            Controls.Add(_txtServer);

            Controls.Add(MakeLabel("Utilisateur", 384, 51, 70));
            _txtUser.SetBounds(458, 48, 228, 24);
            Controls.Add(_txtUser);

            var hint = MakeLabel("Le mot de passe est demandé par Windows.", 694, 51, 250);
            hint.ForeColor = Color.FromArgb(100, 100, 100);
            Controls.Add(hint);

            // ---- groupe écrans
            var grpScreens = new GroupBox();
            grpScreens.Text = "Écrans — classés de gauche à droite, comme dans Windows";
            grpScreens.SetBounds(12, 82, 964, 372);
            grpScreens.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            Controls.Add(grpScreens);

            _presets.SetBounds(14, 22, 936, 30);
            _presets.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            grpScreens.Controls.Add(_presets);

            _btnIdentify.Text = "Identifier les écrans (F2)";
            _btnIdentify.SetBounds(640, 0, 176, 28);
            _btnIdentify.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnIdentify.Click += OnIdentify;
            _presets.Controls.Add(_btnIdentify);

            var btnRefresh = MakeButton("Actualiser (F5)", 822, 0, 114, 28, OnRefresh);
            btnRefresh.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _presets.Controls.Add(btnRefresh);

            _map.SetBounds(14, 58, 936, 252);
            _map.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            _map.Config = _config;
            _map.SelectionChanged += delegate { UpdateSelectionLabel(); };
            grpScreens.Controls.Add(_map);

            _lblSelection.SetBounds(14, 316, 936, 22);
            _lblSelection.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _lblSelection.AutoSize = false;
            grpScreens.Controls.Add(_lblSelection);

            _lblWarning.SetBounds(14, 338, 936, 30);
            _lblWarning.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _lblWarning.AutoSize = false;
            _lblWarning.ForeColor = Color.FromArgb(176, 42, 42);
            grpScreens.Controls.Add(_lblWarning);

            // ---- groupe options
            var grpOptions = new GroupBox();
            grpOptions.Text = "Options de session";
            grpOptions.SetBounds(12, 462, 470, 168);
            grpOptions.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            Controls.Add(grpOptions);

            AddCheck(grpOptions, _chkClipboard, "Presse-papiers partagé", 14, 24, 210);
            AddCheck(grpOptions, _chkPrinters, "Imprimantes locales", 14, 50, 210);
            AddCheck(grpOptions, _chkSound, "Son de la session", 14, 76, 210);
            AddCheck(grpOptions, _chkDrives, "Disques locaux", 14, 102, 210);
            AddCheck(grpOptions, _chkBar, "Barre de connexion", 234, 24, 220);
            AddCheck(grpOptions, _chkReconnect, "Reconnexion automatique", 234, 50, 220);

            grpOptions.Controls.Add(MakeLabel("Raccourcis clavier Windows :", 234, 80, 210));
            _cbKeyboard.SetBounds(234, 100, 210, 24);
            _cbKeyboard.DropDownStyle = ComboBoxStyle.DropDownList;
            _cbKeyboard.Items.AddRange(new object[] { "Sur cet ordinateur", "Dans la session", "En plein écran (défaut)" });
            grpOptions.Controls.Add(_cbKeyboard);

            _chkWindowedSingle.SetBounds(14, 130, 444, 30);
            _chkWindowedSingle.AutoSize = false;
            _chkWindowedSingle.Text = "Si un seul écran : fenêtre maximisée au lieu du plein écran (compatibilité)";
            _chkWindowedSingle.CheckedChanged += delegate { if (!_loading) UpdateSelectionLabel(); };
            grpOptions.Controls.Add(_chkWindowedSingle);

            // ---- groupe fiabilité
            var grpAdvanced = new GroupBox();
            grpAdvanced.Text = "Fiabilité et vérification";
            grpAdvanced.SetBounds(494, 462, 482, 168);
            grpAdvanced.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            Controls.Add(grpAdvanced);

            _chkMultimonAll.SetBounds(14, 20, 452, 34);
            _chkMultimonAll.AutoSize = false;
            _chkMultimonAll.Text = "Tous les écrans cochés : utiliser le mode multi-écrans standard (sans liste)";
            _chkMultimonAll.CheckedChanged += delegate { if (!_loading) UpdateSelectionLabel(); };
            grpAdvanced.Controls.Add(_chkMultimonAll);

            grpAdvanced.Controls.Add(MakeButton("Correspondance mstsc…", 14, 60, 220, 28, OnMapping));
            grpAdvanced.Controls.Add(MakeButton("Liste mstsc (/l)", 244, 60, 222, 28, OnMstscList));
            grpAdvanced.Controls.Add(MakeButton("Aperçu du fichier .rdp", 14, 94, 220, 28, OnPreview));
            grpAdvanced.Controls.Add(MakeButton("Diagnostic des écrans", 244, 94, 222, 28, OnDiagnostic));
            grpAdvanced.Controls.Add(MakeButton("Dossier des sessions", 14, 128, 220, 28, OnOpenFolder));
            grpAdvanced.Controls.Add(MakeButton("Copier le .rdp sur le Bureau", 244, 128, 222, 28, OnDesktopCopy));

            // ---- bas de fenêtre
            _lblStatus.SetBounds(12, 638, 720, 46);
            _lblStatus.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _lblStatus.AutoSize = false;
            _lblStatus.ForeColor = Color.FromArgb(40, 90, 40);
            Controls.Add(_lblStatus);

            var btnConnect = new Button();
            btnConnect.Text = "Se connecter";
            btnConnect.SetBounds(760, 634, 216, 52);
            btnConnect.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnConnect.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
            btnConnect.Click += OnConnect;
            Controls.Add(btnConnect);
            AcceptButton = btnConnect;
        }

        private static Label MakeLabel(string text, int x, int y, int w)
        {
            var l = new Label();
            l.Text = text;
            l.SetBounds(x, y, w, 20);
            l.AutoSize = false;
            return l;
        }

        private static Button MakeButton(string text, int x, int y, int w, int h, EventHandler click)
        {
            var b = new Button();
            b.Text = text;
            b.SetBounds(x, y, w, h);
            b.Click += click;
            return b;
        }

        private static void AddCheck(Control parent, CheckBox box, string text, int x, int y, int w)
        {
            box.Text = text;
            box.SetBounds(x, y, w, 24);
            box.AutoSize = false;
            parent.Controls.Add(box);
        }

        // ------------------------------------------------------------------ cycle de vie

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            _loading = true;
            try
            {
                RefreshProfileList();
                BuildPresetButtons();

                Profile profile = _config.Find(_config.LastProfile) ?? _config.Profiles.FirstOrDefault();
                if (profile == null)
                {
                    profile = new Profile();
                    profile.Name = "Nouvelle connexion";
                    _config.Profiles.Add(profile);
                    RefreshProfileList();
                }
                _cbProfiles.SelectedItem = profile.Name;
            }
            finally { _loading = false; }

            ApplySelectedProfile();
            _map.Focus();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            IdentifyOverlay.CloseAll();
            base.OnFormClosed(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F5) { OnRefresh(this, EventArgs.Empty); e.Handled = true; }
            else if (e.KeyCode == Keys.F2) { OnIdentify(this, EventArgs.Empty); e.Handled = true; }
            base.OnKeyDown(e);
        }

        private void RefreshProfileList()
        {
            string current = _cbProfiles.SelectedItem as string;
            bool saved = _loading;
            _loading = true;
            _cbProfiles.Items.Clear();
            foreach (var p in _config.Profiles.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
                _cbProfiles.Items.Add(p.Name);
            if (current != null && _cbProfiles.Items.Contains(current)) _cbProfiles.SelectedItem = current;
            else if (_cbProfiles.Items.Count > 0) _cbProfiles.SelectedIndex = 0;
            _loading = saved;
        }

        private Profile CurrentProfile
        {
            get { return _config.Find(_cbProfiles.SelectedItem as string); }
        }

        private void ApplySelectedProfile()
        {
            var profile = CurrentProfile;
            if (profile == null) return;

            _loading = true;
            try
            {
                _txtServer.Text = profile.Address ?? "";
                _txtUser.Text = profile.UserName ?? "";
                _chkClipboard.Checked = profile.Clipboard;
                _chkPrinters.Checked = profile.Printers;
                _chkSound.Checked = profile.Sound;
                _chkDrives.Checked = profile.LocalDrives;
                _chkBar.Checked = profile.ConnectionBar;
                _chkReconnect.Checked = profile.AutoReconnect;
                _chkWindowedSingle.Checked = profile.WindowedWhenSingle;
                _chkMultimonAll.Checked = profile.MultimonSwitchWhenAll;
                _cbKeyboard.SelectedIndex = Math.Max(0, Math.Min(2, profile.KeyboardHook));

                var selection = AppConfig.ResolveScreens(profile, _monitors);
                if (selection.Count == 0) selection = DefaultSelection();
                _map.SetMonitors(_monitors, selection);
            }
            finally { _loading = false; }

            UpdateSelectionLabel();
            SetStatus("Connexion « " + profile.Name + " » chargée.", false);
        }

        private List<MonitorInfo> DefaultSelection()
        {
            var primary = _monitors.FirstOrDefault(m => m.IsPrimary) ?? _monitors.FirstOrDefault();
            return primary == null ? new List<MonitorInfo>() : new List<MonitorInfo> { primary };
        }

        private void CollectInto(Profile profile)
        {
            profile.Address = _txtServer.Text.Trim();
            profile.UserName = _txtUser.Text.Trim();
            profile.Clipboard = _chkClipboard.Checked;
            profile.Printers = _chkPrinters.Checked;
            profile.Sound = _chkSound.Checked;
            profile.LocalDrives = _chkDrives.Checked;
            profile.ConnectionBar = _chkBar.Checked;
            profile.AutoReconnect = _chkReconnect.Checked;
            profile.WindowedWhenSingle = _chkWindowedSingle.Checked;
            profile.MultimonSwitchWhenAll = _chkMultimonAll.Checked;
            profile.KeyboardHook = Math.Max(0, Math.Min(2, _cbKeyboard.SelectedIndex));
            profile.Screens = AppConfig.CaptureScreens(_map.Selection);
        }

        // ------------------------------------------------------------------ écrans

        private void BuildPresetButtons()
        {
            foreach (Control c in _presets.Controls.Cast<Control>().Where(c => (c.Tag as string) == "preset").ToList())
            {
                _presets.Controls.Remove(c);
                c.Dispose();
            }

            int x = 0;
            var all = MakeButton("Tous les écrans", x, 0, 120, 28, delegate { _map.SetSelection(_monitors); });
            all.Tag = "preset";
            _presets.Controls.Add(all);
            x += 124;

            for (int n = 1; n <= _monitors.Count; n++)
            {
                int count = n;
                var b = MakeButton(count == 1 ? "1 écran" : count + " écrans", x, 0, 86, 28,
                    delegate { _map.SetSelection(BestRun(count)); });
                b.Tag = "preset";
                _presets.Controls.Add(b);
                x += 90;
            }

            var primary = MakeButton("Écran principal", x, 0, 116, 28, delegate { _map.SetSelection(DefaultSelection()); });
            primary.Tag = "preset";
            _presets.Controls.Add(primary);
        }

        /// <summary>
        /// Choisit le meilleur groupe de N écrans voisins : on privilégie celui qui contient
        /// l'écran principal, puis le plus à gauche. mstsc exige des écrans adjacents.
        /// </summary>
        private List<MonitorInfo> BestRun(int count)
        {
            var ordered = MonitorEnumerator.LeftToRight(_monitors);
            if (count >= ordered.Count) return ordered;

            List<MonitorInfo> best = null;
            int bestScore = int.MinValue;
            for (int start = 0; start + count <= ordered.Count; start++)
            {
                var run = ordered.Skip(start).Take(count).ToList();
                if (!MonitorEnumerator.IsContiguous(run)) continue;
                int score = (run.Any(m => m.IsPrimary) ? 1000 : 0) + (ordered.Count - start);
                if (score > bestScore) { bestScore = score; best = run; }
            }
            return best ?? ordered.Take(count).ToList();
        }

        private void UpdateSelectionLabel()
        {
            var selection = _map.Selection;
            if (selection.Count == 0)
            {
                _lblSelection.Text = "Aucun écran sélectionné.";
                _lblWarning.Text = "";
                return;
            }

            string names = string.Join("  +  ", selection.Select(m =>
                string.Format(CultureInfo.CurrentCulture, "{0} {1} (Windows {2})", m.Order, m.PositionLabel, m.WindowsNumber)).ToArray());
            bool plainMultimon = selection.Count == _monitors.Count && _chkMultimonAll.Checked;

            string technical = plainMultimon
                ? "use multimon:i:1 (tous les écrans)"
                : "selectedmonitors:s:" + RdpFile.BuildSelectedMonitors(selection, _config);

            _lblSelection.Text = string.Format(CultureInfo.CurrentCulture, "{0} écran(s) : {1}      →  {2}",
                selection.Count, names, technical);

            var warnings = new List<string>();
            if (!MonitorEnumerator.IsContiguous(selection))
                warnings.Add("Ces écrans ne se touchent pas : le Bureau à distance refuse en général les écrans non adjacents.");
            else if (selection.Count > 1 && !MonitorEnumerator.FillsBoundingBox(selection))
                warnings.Add("La zone couverte n'est pas un rectangle plein : si la session s'ouvre mal, choisissez des écrans alignés.");
            if (selection.Count == 1 && _chkWindowedSingle.Checked)
                warnings.Add("Mode « fenêtre maximisée » actif pour un écran unique.");

            _lblWarning.Text = string.Join(Environment.NewLine, warnings.ToArray());
        }

        private void ReloadMonitors(bool keepSelection)
        {
            var previous = keepSelection ? _map.Selection.Select(m => m.StableKey).ToList() : new List<string>();
            _monitors = MonitorEnumerator.Enumerate();
            _map.Config = _config;

            var selection = _monitors.Where(m => previous.Contains(m.StableKey)).ToList();
            if (selection.Count == 0)
            {
                var profile = CurrentProfile;
                selection = profile != null ? AppConfig.ResolveScreens(profile, _monitors) : new List<MonitorInfo>();
                if (selection.Count == 0) selection = DefaultSelection();
            }
            BuildPresetButtons();
            _map.SetMonitors(_monitors, selection);
            UpdateSelectionLabel();
        }

        private void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            // SystemEvents déclenche cet événement sur un autre thread : on repasse par l'interface.
            if (InvokeRequired)
            {
                try { BeginInvoke((MethodInvoker)delegate { OnDisplaySettingsChanged(sender, e); }); }
                catch (Exception ex) { Log.Write("BeginInvoke écrans : " + ex.Message); }
                return;
            }
            try
            {
                ReloadMonitors(true);
                SetStatus("Configuration des écrans modifiée : la liste a été actualisée (" + _monitors.Count + " écrans).", false);
                Log.Write("DisplaySettingsChanged : " + _monitors.Count + " écrans.");
            }
            catch (Exception ex) { Log.Write("DisplaySettingsChanged : " + ex.Message); }
        }

        private void OnRefresh(object sender, EventArgs e)
        {
            ReloadMonitors(true);
            SetStatus(_monitors.Count + " écran(s) détecté(s).", false);
        }

        private void OnIdentify(object sender, EventArgs e)
        {
            _btnIdentify.Enabled = false;
            IdentifyOverlay.ShowAll(_monitors, _map.Selection, _config, 3500, delegate { _btnIdentify.Enabled = true; });
        }

        // ------------------------------------------------------------------ connexions enregistrées

        private void OnNewProfile(object sender, EventArgs e)
        {
            string name = InputDialog.Prompt(this, "Nouvelle connexion", "Nom de la connexion :", "Nouvelle connexion");
            if (string.IsNullOrEmpty(name)) return;
            if (_config.Find(name) != null)
            {
                MessageBox.Show(this, "Une connexion porte déjà ce nom.", "Nouvelle connexion",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var profile = new Profile();
            profile.Name = name.Trim();
            profile.Address = _txtServer.Text.Trim();
            profile.UserName = _txtUser.Text.Trim();
            profile.Screens = AppConfig.CaptureScreens(_map.Selection);
            _config.Profiles.Add(profile);
            _config.LastProfile = profile.Name;
            _config.Save();
            RefreshProfileList();
            _cbProfiles.SelectedItem = profile.Name;
            SetStatus("Connexion « " + profile.Name + " » créée.", false);
        }

        private void OnSaveProfile(object sender, EventArgs e)
        {
            var profile = CurrentProfile;
            if (profile == null) { OnNewProfile(sender, e); return; }
            CollectInto(profile);
            _config.LastProfile = profile.Name;
            _config.Save();
            SetStatus("Connexion « " + profile.Name + " » enregistrée (" + _map.Selection.Count + " écran(s)).", false);
        }

        private void OnDeleteProfile(object sender, EventArgs e)
        {
            var profile = CurrentProfile;
            if (profile == null) return;
            if (MessageBox.Show(this, "Supprimer la connexion « " + profile.Name + " » ?", "Supprimer",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _config.Profiles.Remove(profile);
            _config.Save();
            RefreshProfileList();
            if (_cbProfiles.Items.Count == 0)
            {
                var fresh = new Profile();
                fresh.Name = "Nouvelle connexion";
                _config.Profiles.Add(fresh);
                RefreshProfileList();
            }
            ApplySelectedProfile();
        }

        private void OnImportRdp(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "Importer un fichier .rdp";
                dlg.Filter = "Connexion Bureau à distance (*.rdp)|*.rdp|Tous les fichiers|*.*";
                dlg.InitialDirectory = AppDomain.CurrentDomain.BaseDirectory;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    string name = Path.GetFileNameWithoutExtension(dlg.FileName);
                    var profile = _config.Find(name);
                    bool isNew = profile == null;
                    if (isNew)
                    {
                        profile = new Profile();
                        profile.Name = name;
                    }
                    RdpFile.Import(dlg.FileName, profile);
                    profile.Screens = AppConfig.CaptureScreens(_map.Selection.Count > 0 ? _map.Selection : DefaultSelection());
                    if (isNew) _config.Profiles.Add(profile);
                    _config.LastProfile = profile.Name;
                    _config.Save();
                    RefreshProfileList();
                    _cbProfiles.SelectedItem = profile.Name;
                    ApplySelectedProfile();
                    SetStatus("Importé : " + Path.GetFileName(dlg.FileName)
                        + "  (réglages conservés, mot de passe non recopié)", false);
                }
                catch (Exception ex)
                {
                    Log.Write("Import .rdp : " + ex);
                    MessageBox.Show(this, "Import impossible : " + ex.Message, "Importer",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        // ------------------------------------------------------------------ actions

        private Profile BuildEffectiveProfile()
        {
            var profile = CurrentProfile;
            profile = profile == null ? new Profile() : profile.Clone();
            if (string.IsNullOrEmpty(profile.Name)) profile.Name = "session";
            CollectInto(profile);
            return profile;
        }

        private void OnPreview(object sender, EventArgs e)
        {
            try
            {
                var selection = _map.Selection;
                var profile = BuildEffectiveProfile();
                string content = RdpFile.Build(profile, selection, _monitors, _config);
                string header =
                    "Écrans utilisés : " + Launcher.Describe(selection, _config) + Environment.NewLine +
                    "Fichier généré  : " + Path.Combine(Paths.SessionsFolder, Paths.SafeFileName(profile.Name) + ".rdp") + Environment.NewLine +
                    new string('-', 78) + Environment.NewLine + Environment.NewLine;
                using (var dlg = new TextDialog("Aperçu du fichier .rdp", header + content)) dlg.ShowDialog(this);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Aperçu", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OnDiagnostic(object sender, EventArgs e)
        {
            using (var dlg = new TextDialog("Diagnostic des écrans", DiagnosticReport.Build(_monitors, _config)))
                dlg.ShowDialog(this);
        }

        private void OnMapping(object sender, EventArgs e)
        {
            using (var dlg = new MappingDialog(_config, _monitors))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    UpdateSelectionLabel();
                    SetStatus(_config.ManualMappingEnabled
                        ? "Correspondance manuelle activée."
                        : "Correspondance automatique (recommandée).", false);
                }
            }
        }

        private void OnMstscList(object sender, EventArgs e)
        {
            try { Process.Start("mstsc.exe", "/l"); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Impossible de lancer mstsc /l : " + ex.Message, "mstsc",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OnOpenFolder(object sender, EventArgs e)
        {
            try
            {
                Paths.EnsureFolders();
                Process.Start("explorer.exe", "\"" + Paths.SessionsFolder + "\"");
            }
            catch (Exception ex) { Log.Write("Ouverture dossier : " + ex.Message); }
        }

        private void OnDesktopCopy(object sender, EventArgs e)
        {
            try
            {
                var selection = _map.Selection;
                var profile = BuildEffectiveProfile();
                if (!Validate(profile, selection)) return;

                string content = RdpFile.Build(profile, selection, _monitors, _config);
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string file = Path.Combine(desktop, Paths.SafeFileName(profile.Name)
                    + " (" + selection.Count + (selection.Count > 1 ? " écrans" : " écran") + ").rdp");

                if (File.Exists(file) && MessageBox.Show(this,
                        "Le fichier existe déjà :" + Environment.NewLine + file + Environment.NewLine + Environment.NewLine + "Le remplacer ?",
                        "Bureau", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

                RdpFile.Write(file, content);
                SetStatus("Raccourci créé : " + file, false);
                Log.Write("Fichier bureau : " + file);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Création impossible : " + ex.Message, "Bureau",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private bool Validate(Profile profile, IList<MonitorInfo> selection)
        {
            if (string.IsNullOrEmpty(profile.Address))
            {
                MessageBox.Show(this, "Indiquez le nom ou l'adresse du serveur.", "Serveur manquant",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                _txtServer.Focus();
                return false;
            }
            if (selection.Count == 0)
            {
                MessageBox.Show(this, "Sélectionnez au moins un écran.", "Écrans",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            if (selection.Count > 1 && !MonitorEnumerator.IsContiguous(selection))
            {
                var answer = MessageBox.Show(this,
                    "Les écrans choisis ne se touchent pas." + Environment.NewLine + Environment.NewLine +
                    "Le Bureau à distance n'accepte en général que des écrans adjacents : la session" + Environment.NewLine +
                    "risque de s'ouvrir sur un seul écran." + Environment.NewLine + Environment.NewLine + "Continuer quand même ?",
                    "Écrans non adjacents", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes) return false;
            }
            return true;
        }

        private void OnConnect(object sender, EventArgs e)
        {
            try
            {
                var selection = _map.Selection;
                var profile = BuildEffectiveProfile();
                if (!Validate(profile, selection)) return;

                // La connexion en cours est mémorisée : au prochain démarrage tout est déjà prêt.
                var stored = CurrentProfile;
                if (stored != null)
                {
                    CollectInto(stored);
                    _config.LastProfile = stored.Name;
                    _config.Save();
                }

                string path = Launcher.Launch(profile, selection, _monitors, _config);
                SetStatus("Session lancée sur " + selection.Count + " écran(s) : "
                          + string.Join(" + ", selection.Select(m => m.Order + " " + m.PositionLabel).ToArray())
                          + "   (" + Path.GetFileName(path) + ")", false);
            }
            catch (Exception ex)
            {
                Log.Write("Connexion : " + ex);
                MessageBox.Show(this, "Le lancement a échoué : " + ex.Message, "Bureau à distance",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SetStatus(string message, bool error)
        {
            _lblStatus.ForeColor = error ? Color.FromArgb(176, 42, 42) : Color.FromArgb(40, 90, 40);
            _lblStatus.Text = message;
        }
    }

    /// <summary>Petite boîte de saisie (nom d'une connexion).</summary>
    public static class InputDialog
    {
        public static string Prompt(IWin32Window owner, string title, string label, string initial)
        {
            using (var form = new Form())
            {
                form.Text = title;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = FormStartPosition.CenterParent;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.ShowIcon = false;
                form.ClientSize = new Size(420, 130);
                form.Font = new Font("Segoe UI", 9f);

                var lbl = new Label();
                lbl.Text = label;
                lbl.SetBounds(14, 16, 390, 20);

                var box = new TextBox();
                box.Text = initial ?? "";
                box.SetBounds(14, 40, 390, 24);

                var ok = new Button();
                ok.Text = "Valider";
                ok.SetBounds(196, 82, 100, 30);
                ok.DialogResult = DialogResult.OK;

                var cancel = new Button();
                cancel.Text = "Annuler";
                cancel.SetBounds(304, 82, 100, 30);
                cancel.DialogResult = DialogResult.Cancel;

                form.Controls.Add(lbl);
                form.Controls.Add(box);
                form.Controls.Add(ok);
                form.Controls.Add(cancel);
                form.AcceptButton = ok;
                form.CancelButton = cancel;
                form.Shown += delegate { box.Focus(); box.SelectAll(); };

                return form.ShowDialog(owner) == DialogResult.OK ? box.Text.Trim() : null;
            }
        }
    }
}
