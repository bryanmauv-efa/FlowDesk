using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace TermServMultiScreen
{
    /// <summary>
    /// Plan des ecrans, dessine a l'echelle et dans la disposition reelle : on clique sur l'ecran
    /// qu'on voit, jamais sur un numero a deviner.
    /// </summary>
    public sealed class MonitorMapControl : Control
    {
        private static readonly Color Accent = Color.FromArgb(0, 103, 192);
        private static readonly Color AccentLight = Color.FromArgb(0, 120, 215);
        private static readonly Color Idle = Color.FromArgb(247, 247, 247);
        private static readonly Color IdleBorder = Color.FromArgb(170, 170, 170);

        private List<MonitorInfo> _monitors = new List<MonitorInfo>();
        private readonly List<MonitorInfo> _selected = new List<MonitorInfo>();
        private readonly Dictionary<MonitorInfo, Rectangle> _boxes = new Dictionary<MonitorInfo, Rectangle>();
        private MonitorInfo _hover;
        private int _focusIndex;
        private readonly ToolTip _tip = new ToolTip();
        private MonitorInfo _tipTarget;

        public event EventHandler SelectionChanged;

        public AppConfig Config;

        public MonitorMapControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                     | ControlStyles.Selectable, true);
            TabStop = true;
            BackColor = Color.White;
            _tip.InitialDelay = 350;
            _tip.ShowAlways = true;
        }

        public IList<MonitorInfo> Monitors
        {
            get { return _monitors; }
        }

        public List<MonitorInfo> Selection
        {
            get { return MonitorEnumerator.LeftToRight(_selected); }
        }

        public void SetMonitors(List<MonitorInfo> monitors, IEnumerable<MonitorInfo> selection)
        {
            _monitors = monitors ?? new List<MonitorInfo>();
            _selected.Clear();
            if (selection != null)
                foreach (var m in selection)
                    if (_monitors.Contains(m) && !_selected.Contains(m)) _selected.Add(m);
            _focusIndex = 0;
            _hover = null;
            ComputeBoxes();
            Invalidate();
            RaiseChanged();
        }

        public void SetSelection(IEnumerable<MonitorInfo> selection)
        {
            _selected.Clear();
            if (selection != null)
                foreach (var m in selection)
                    if (_monitors.Contains(m) && !_selected.Contains(m)) _selected.Add(m);
            Invalidate();
            RaiseChanged();
        }

        public bool IsSelected(MonitorInfo m)
        {
            return _selected.Contains(m);
        }

        public void Toggle(MonitorInfo m)
        {
            if (m == null) return;
            if (_selected.Contains(m))
            {
                if (_selected.Count == 1) return;   // au moins un ecran doit rester
                _selected.Remove(m);
            }
            else _selected.Add(m);
            Invalidate();
            RaiseChanged();
        }

        private void RaiseChanged()
        {
            var h = SelectionChanged;
            if (h != null) h(this, EventArgs.Empty);
        }

        // ---------- geometrie ----------

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ComputeBoxes();
        }

        private void ComputeBoxes()
        {
            _boxes.Clear();
            if (_monitors.Count == 0) return;

            Rectangle bbox = _monitors[0].LayoutBounds;
            foreach (var m in _monitors) bbox = Rectangle.Union(bbox, m.LayoutBounds);
            if (bbox.Width <= 0 || bbox.Height <= 0) return;

            const int pad = 14;
            int availableWidth = Math.Max(40, Width - 2 * pad);
            int availableHeight = Math.Max(40, Height - 2 * pad);
            double scale = Math.Min((double)availableWidth / bbox.Width, (double)availableHeight / bbox.Height);

            int totalWidth = (int)Math.Round(bbox.Width * scale);
            int totalHeight = (int)Math.Round(bbox.Height * scale);
            int offsetX = (Width - totalWidth) / 2;
            int offsetY = (Height - totalHeight) / 2;

            foreach (var m in _monitors)
            {
                var r = new Rectangle(
                    offsetX + (int)Math.Round((m.LayoutBounds.Left - bbox.Left) * scale),
                    offsetY + (int)Math.Round((m.LayoutBounds.Top - bbox.Top) * scale),
                    Math.Max(24, (int)Math.Round(m.LayoutBounds.Width * scale)),
                    Math.Max(24, (int)Math.Round(m.LayoutBounds.Height * scale)));
                r.Inflate(-3, -3);
                _boxes[m] = r;
            }
        }

        public MonitorInfo MonitorAt(Point p)
        {
            foreach (var kv in _boxes) if (kv.Value.Contains(p)) return kv.Key;
            return null;
        }

        // ---------- dessin ----------

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            if (_monitors.Count == 0)
            {
                TextRenderer.DrawText(g, "Aucun écran détecté", Font, ClientRectangle, Color.Gray,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            var ordered = MonitorEnumerator.LeftToRight(_monitors);
            for (int i = 0; i < ordered.Count; i++)
            {
                var m = ordered[i];
                Rectangle box;
                if (!_boxes.TryGetValue(m, out box)) continue;

                bool selected = _selected.Contains(m);
                bool hover = ReferenceEquals(m, _hover);
                bool focused = Focused && i == _focusIndex;

                Color fill = selected ? (hover ? AccentLight : Accent) : (hover ? Color.FromArgb(232, 240, 250) : Idle);
                Color border = selected ? Color.FromArgb(0, 70, 130) : IdleBorder;
                Color text = selected ? Color.White : Color.FromArgb(45, 45, 45);

                using (var path = RoundedRect(box, 8))
                using (var brush = new SolidBrush(fill))
                using (var pen = new Pen(border, selected ? 2f : 1f))
                {
                    g.FillPath(brush, path);
                    g.DrawPath(pen, path);
                }

                if (focused)
                {
                    var inner = box;
                    inner.Inflate(-4, -4);
                    using (var pen = new Pen(selected ? Color.White : Accent))
                    {
                        pen.DashStyle = DashStyle.Dot;
                        g.DrawRectangle(pen, inner);
                    }
                }

                DrawBoxText(g, box, m, text, selected);
            }
        }

        private void DrawBoxText(Graphics g, Rectangle box, MonitorInfo m, Color color, bool selected)
        {
            int bigSize = Math.Max(11, Math.Min(46, (int)(box.Height * 0.34)));
            int smallSize = Math.Max(7, Math.Min(11, (int)(box.Height * 0.10)));

            using (var bigFont = new Font(Font.FontFamily, bigSize, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var smallFont = new Font(Font.FontFamily, smallSize + 3, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var smallBold = new Font(Font.FontFamily, smallSize + 3, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                var lines = new List<KeyValuePair<string, Font>>();
                lines.Add(new KeyValuePair<string, Font>(m.Order.ToString(), bigFont));
                lines.Add(new KeyValuePair<string, Font>(m.PositionLabel, smallBold));
                lines.Add(new KeyValuePair<string, Font>("Windows " + m.WindowsNumber, smallFont));
                lines.Add(new KeyValuePair<string, Font>(m.ResolutionText, smallFont));
                if (m.IsPrimary) lines.Add(new KeyValuePair<string, Font>("PRINCIPAL", smallBold));

                // Ne garder que ce qui tient dans le rectangle.
                int totalHeight = 0;
                var kept = new List<KeyValuePair<string, Font>>();
                foreach (var line in lines)
                {
                    int h = TextRenderer.MeasureText(line.Key, line.Value).Height;
                    if (totalHeight + h > box.Height - 10) break;
                    kept.Add(line);
                    totalHeight += h;
                }
                if (kept.Count == 0) kept.Add(lines[0]);

                int y = box.Top + (box.Height - totalHeight) / 2;
                foreach (var line in kept)
                {
                    var size = TextRenderer.MeasureText(line.Key, line.Value);
                    TextRenderer.DrawText(g, line.Key, line.Value,
                        new Rectangle(box.Left, y, box.Width, size.Height), color,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                    y += size.Height;
                }
            }
        }

        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            if (r.Width < d || r.Height < d) { path.AddRectangle(r); return path; }
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        // ---------- souris ----------

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var hit = MonitorAt(e.Location);
            if (!ReferenceEquals(hit, _hover))
            {
                _hover = hit;
                Cursor = hit == null ? Cursors.Default : Cursors.Hand;
                Invalidate();
            }
            if (!ReferenceEquals(hit, _tipTarget))
            {
                _tipTarget = hit;
                if (hit == null) _tip.Hide(this);
                else _tip.SetToolTip(this, hit.DetailText + Environment.NewLine
                    + "(clic = ajouter ou retirer, double-clic = cet écran seul)");
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = null;
            _tipTarget = null;
            Cursor = Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            var hit = MonitorAt(e.Location);
            if (hit == null) return;
            var ordered = MonitorEnumerator.LeftToRight(_monitors);
            _focusIndex = ordered.IndexOf(hit);
            if (e.Button == MouseButtons.Left) Toggle(hit);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            var hit = MonitorAt(e.Location);
            if (hit != null) SetSelection(new[] { hit });
        }

        // ---------- clavier ----------

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Left:
                case Keys.Right:
                case Keys.Up:
                case Keys.Down:
                case Keys.Space:
                    return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            var ordered = MonitorEnumerator.LeftToRight(_monitors);
            if (ordered.Count == 0) return;

            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Up)
            {
                _focusIndex = (_focusIndex - 1 + ordered.Count) % ordered.Count;
                Invalidate(); e.Handled = true;
            }
            else if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Down)
            {
                _focusIndex = (_focusIndex + 1) % ordered.Count;
                Invalidate(); e.Handled = true;
            }
            else if (e.KeyCode == Keys.Space)
            {
                Toggle(ordered[Math.Min(_focusIndex, ordered.Count - 1)]);
                e.Handled = true;
            }
            else if (e.Control && e.KeyCode == Keys.A)
            {
                SetSelection(ordered);
                e.Handled = true;
            }
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _tip.Dispose();
            base.Dispose(disposing);
        }
    }
}
