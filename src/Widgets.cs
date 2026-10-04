using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PhoneScreen {
    static class Theme {
        public static readonly Color Bg = Color.FromArgb(13, 17, 22);
        public static readonly Color Card = Color.FromArgb(21, 27, 34);
        public static readonly Color CardLine = Color.FromArgb(33, 41, 50);
        public static readonly Color Field = Color.FromArgb(27, 34, 43);
        public static readonly Color FieldLine = Color.FromArgb(46, 57, 68);
        public static readonly Color Ink = Color.FromArgb(236, 242, 246);
        public static readonly Color Muted = Color.FromArgb(143, 158, 171);
        public static readonly Color Faint = Color.FromArgb(96, 110, 122);
        public static readonly Color Accent = Color.FromArgb(110, 224, 182);
        public static readonly Color AccentHover = Color.FromArgb(146, 236, 204);
        public static readonly Color AccentPressed = Color.FromArgb(86, 196, 156);
        public static readonly Color AccentInk = Color.FromArgb(9, 31, 24);
        public static readonly Color Warning = Color.FromArgb(255, 190, 120);
        public static readonly Color Danger = Color.FromArgb(255, 132, 128);
        public static float Scale = 1;
        public static int S(int value) { return (int)Math.Round(value * Scale); }
        public static Font Font(float size, bool strong) {
            return new Font(strong ? "Segoe UI Semibold" : "Segoe UI", size, FontStyle.Regular, GraphicsUnit.Point);
        }
    }

    static class Shapes {
        public static GraphicsPath Round(RectangleF r, float radius) {
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure(); return path;
        }
    }

    // The app mark, shared by the header, the window icon of the phone screen and the build icon.
    static class Logo {
        public static void Draw(Graphics g, RectangleF bounds, bool background) {
            var state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(bounds.X, bounds.Y);
            g.ScaleTransform(bounds.Width / 256f, bounds.Height / 256f);
            var mint = Theme.Accent;
            if (background) using (var fill = new SolidBrush(Color.FromArgb(14, 20, 26))) using (var p = Shapes.Round(new RectangleF(4, 4, 248, 248), 52)) g.FillPath(fill, p);
            using (var pen = new Pen(mint, 9) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round }) {
                using (var screen = new SolidBrush(Color.FromArgb(22, 37, 47))) using (var p = Shapes.Round(new RectangleF(32, 58, 177, 120), 13)) { g.FillPath(screen, p); g.DrawPath(pen, p); }
                g.DrawLine(pen, 93, 199, 153, 199); g.DrawLine(pen, 123, 179, 123, 197);
                using (var phone = new SolidBrush(Color.FromArgb(14, 20, 26))) using (var p = Shapes.Round(new RectangleF(160, 110, 66, 102), 13)) { g.FillPath(phone, p); g.DrawPath(pen, p); }
                pen.Width = 8;
                g.DrawLines(pen, new[] { new PointF(58, 144), new PointF(75, 116), new PointF(96, 143), new PointF(123, 96), new PointF(146, 119) });
                pen.Width = 6; g.DrawLine(pen, 182, 192, 203, 192);
            }
            g.Restore(state);
        }
        public static Bitmap Render(int size) {
            var image = new Bitmap(size, size);
            using (var g = Graphics.FromImage(image)) { g.Clear(Color.Transparent); Draw(g, new RectangleF(0, 0, size, size), true); }
            return image;
        }
    }

    sealed class LogoMark : Control {
        public LogoMark() { SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true); TabStop = false; }
        protected override void OnPaint(PaintEventArgs e) { e.Graphics.Clear(Parent.BackColor); Logo.Draw(e.Graphics, new RectangleF(0, 0, Width, Height), true); }
    }

    // Rounded surface with a hairline border.
    sealed class Card : Panel {
        public Card() {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
        }
        protected override void OnPaintBackground(PaintEventArgs e) {
            e.Graphics.Clear(Parent == null ? Theme.Bg : Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Shapes.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Theme.S(12)))
            using (var fill = new SolidBrush(BackColor))
            using (var line = new Pen(Theme.CardLine)) { e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(line, path); }
        }
    }

    sealed class StepBadge : Control {
        public StepBadge() { TabStop = false; SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); Font = Theme.Font(9, true); }
        protected override void OnPaint(PaintEventArgs e) {
            e.Graphics.Clear(Parent.BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var fill = new SolidBrush(Color.FromArgb(36, Theme.Accent))) e.Graphics.FillEllipse(fill, 0, 0, Width - 1, Height - 1);
            using (var ring = new Pen(Color.FromArgb(120, Theme.Accent))) e.Graphics.DrawEllipse(ring, 0.5f, 0.5f, Width - 2, Height - 2);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Theme.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    // Dark title bar and scroll bars on Windows 10 1809+ and Windows 11; silently ignored elsewhere.
    static class DarkMode {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);
        public static void TitleBar(IntPtr handle) {
            int on = 1;
            try { if (DwmSetWindowAttribute(handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(handle, 19, ref on, 4); } catch (Exception) { }
        }
        public static void ScrollBars(IntPtr handle) { try { SetWindowTheme(handle, "DarkMode_Explorer", null); } catch (Exception) { } }
    }

    sealed class Divider : Control {
        public Divider() { TabStop = false; SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true); }
        protected override void OnPaint(PaintEventArgs e) {
            e.Graphics.Clear(Parent.BackColor);
            using (var pen = new Pen(Theme.CardLine)) e.Graphics.DrawLine(pen, 0, Height / 2, Width, Height / 2);
        }
    }

    sealed class Dot : Control {
        Color color = Theme.Faint;
        public Color Color { get { return color; } set { if (color != value) { color = value; Invalidate(); } } }
        public Dot() { TabStop = false; SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
        protected override void OnPaint(PaintEventArgs e) {
            e.Graphics.Clear(Parent.BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float d = Math.Min(Width, Height) - 1;
            using (var halo = new SolidBrush(Color.FromArgb(50, color))) e.Graphics.FillEllipse(halo, 0, 0, d, d);
            using (var core = new SolidBrush(color)) e.Graphics.FillEllipse(core, d * 0.25f, d * 0.25f, d * 0.5f, d * 0.5f);
        }
    }

    sealed class CueTextBox : TextBox {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr window, int message, IntPtr parameter, string text);
        public string Hint { get; set; }
        protected override void OnHandleCreated(EventArgs e) {
            base.OnHandleCreated(e);
            if (!String.IsNullOrEmpty(Hint)) SendMessage(Handle, 0x1501, new IntPtr(1), Hint);
        }
    }

    // A borderless text box inside a rounded field that lights up on focus.
    sealed class InputFrame : Panel {
        public readonly TextBox Box;
        bool invalid;
        public bool Invalid { get { return invalid; } set { if (invalid != value) { invalid = value; Invalidate(); } } }
        public InputFrame(TextBox box) {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Box = box; BackColor = Theme.Field; Cursor = Cursors.IBeam;
            box.BorderStyle = BorderStyle.None; box.BackColor = Theme.Field; box.ForeColor = Theme.Ink;
            box.Font = new Font("Consolas", 11.5f);
            box.Enter += (s, e) => Invalidate(); box.Leave += (s, e) => Invalidate();
            Controls.Add(box);
        }
        protected override void OnClick(EventArgs e) { base.OnClick(e); Box.Focus(); }
        protected override void OnLayout(LayoutEventArgs e) {
            base.OnLayout(e);
            int pad = Theme.S(12);
            Box.SetBounds(pad, (Height - Box.PreferredHeight) / 2 + 1, Math.Max(10, Width - pad * 2), Box.PreferredHeight);
        }
        protected override void OnPaintBackground(PaintEventArgs e) {
            e.Graphics.Clear(Parent == null ? Theme.Card : Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color line = invalid ? Theme.Warning : Box.Focused ? Theme.Accent : Theme.FieldLine;
            using (var path = Shapes.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Theme.S(8)))
            using (var fill = new SolidBrush(Enabled ? Theme.Field : Theme.Card))
            using (var pen = new Pen(line, Box.Focused || invalid ? 1.6f : 1f)) { e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(pen, path); }
        }
        protected override void OnEnabledChanged(EventArgs e) { Box.BackColor = Enabled ? Theme.Field : Theme.Card; Invalidate(); base.OnEnabledChanged(e); }
    }

    sealed class ActionButton : Button {
        bool hover, pressed;
        public bool Primary { get; set; }
        public ActionButton() {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Cursor = Cursors.Hand;
            Font = Theme.Font(10, true);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override void OnPaint(PaintEventArgs e) {
            e.Graphics.Clear(Parent == null ? BackColor : Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = !Enabled ? Color.FromArgb(30, 38, 46) : Primary
                ? (pressed ? Theme.AccentPressed : hover ? Theme.AccentHover : Theme.Accent)
                : (pressed ? Color.FromArgb(30, 39, 48) : hover ? Color.FromArgb(44, 55, 66) : Color.FromArgb(35, 45, 55));
            using (var shape = Shapes.Round(new RectangleF(1, 1, Width - 3, Height - 3), Theme.S(8))) {
                using (var brush = new SolidBrush(fill)) e.Graphics.FillPath(brush, shape);
                if (Focused && ShowFocusCues) using (var pen = new Pen(Primary ? Theme.Ink : Theme.Accent, 2)) e.Graphics.DrawPath(pen, shape);
                else if (!Primary && Enabled) using (var pen = new Pen(Theme.FieldLine)) e.Graphics.DrawPath(pen, shape);
            }
            var bounds = ClientRectangle; if (pressed) bounds.Offset(0, 1);
            Color ink = !Enabled ? Theme.Faint : Primary ? Theme.AccentInk : Theme.Ink;
            TextRenderer.DrawText(e.Graphics, Text, Font, bounds, ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }

    // iOS-style switch with a label; behaves like a normal CheckBox for keyboard and screen readers.
    sealed class Switch : CheckBox {
        bool hover;
        public Switch() {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Cursor = Cursors.Hand; AutoSize = false; Font = Theme.Font(10, false); ForeColor = Theme.Ink;
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnPaint(PaintEventArgs e) {
            var g = e.Graphics; g.Clear(Parent.BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
            int w = Theme.S(36), h = Theme.S(20), y = (Height - h) / 2;
            Color track = !Enabled ? Color.FromArgb(32, 40, 48) : Checked ? (hover ? Theme.AccentHover : Theme.Accent) : (hover ? Color.FromArgb(58, 70, 82) : Color.FromArgb(47, 58, 69));
            using (var path = Shapes.Round(new RectangleF(1, y, w, h), h / 2f)) {
                using (var fill = new SolidBrush(track)) g.FillPath(fill, path);
                if (Focused && ShowFocusCues) using (var pen = new Pen(Theme.Ink, 1.5f)) g.DrawPath(pen, path);
            }
            float knob = h - Theme.S(6), kx = Checked ? 1 + w - knob - Theme.S(3) : 1 + Theme.S(3);
            Color knobColor = !Enabled ? Theme.Faint : Checked ? Theme.AccentInk : Theme.Ink;
            using (var brush = new SolidBrush(knobColor)) g.FillEllipse(brush, kx, y + Theme.S(3), knob, knob);
            var text = new Rectangle(w + Theme.S(12), 0, Width - w - Theme.S(12), Height);
            TextRenderer.DrawText(g, Text, Font, text, Enabled ? ForeColor : Theme.Faint, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }

    sealed class DarkMenuColors : ProfessionalColorTable {
        public override Color MenuItemSelected { get { return Color.FromArgb(40, 52, 63); } }
        public override Color MenuItemBorder { get { return Color.FromArgb(40, 52, 63); } }
        public override Color MenuBorder { get { return Theme.FieldLine; } }
        public override Color ToolStripDropDownBackground { get { return Theme.Field; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Field; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Field; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Field; } }
        public override Color CheckBackground { get { return Theme.Field; } }
        public override Color CheckSelectedBackground { get { return Color.FromArgb(40, 52, 63); } }
        public override Color CheckPressedBackground { get { return Color.FromArgb(40, 52, 63); } }
    }

    sealed class DarkMenuRenderer : ToolStripProfessionalRenderer {
        public DarkMenuRenderer() : base(new DarkMenuColors()) { RoundedEdges = false; }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = e.Item.Enabled ? Theme.Ink : Theme.Faint; base.OnRenderItemText(e); }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) {
            var r = e.ImageRectangle; e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(Theme.Accent, 2)) e.Graphics.DrawLines(pen, new[] {
                new PointF(r.Left + r.Width * 0.2f, r.Top + r.Height * 0.55f), new PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.75f), new PointF(r.Left + r.Width * 0.8f, r.Top + r.Height * 0.28f) });
        }
    }

    sealed class OptionPicker : Control {
        string[] items = new string[0];
        int selected;
        bool hover;
        public event EventHandler SelectedIndexChanged;
        public int Count { get { return items.Length; } }
        public int SelectedIndex {
            get { return selected; }
            set {
                if (value < 0 || value >= items.Length) throw new ArgumentOutOfRangeException("value");
                if (selected == value) return;
                selected = value; Text = items[selected]; Invalidate(); if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
        }
        public OptionPicker() {
            SetStyle(ControlStyles.Selectable | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            TabStop = true; Cursor = Cursors.Hand; AccessibleRole = AccessibleRole.ComboBox;
            BackColor = Theme.Field; ForeColor = Theme.Ink; Font = Theme.Font(10, false);
        }
        public void SetOptions(string[] options) { items = options; selected = 0; Text = items.Length == 0 ? "" : items[0]; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnClick(EventArgs e) {
            base.OnClick(e); if (!Enabled) return; Focus();
            var menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), ShowImageMargin = false, ShowCheckMargin = true, Font = Font, BackColor = Theme.Field };
            for (int i = 0; i < items.Length; i++) {
                int index = i;
                var item = new ToolStripMenuItem(items[i]) { Checked = index == selected, ForeColor = Theme.Ink, Padding = new Padding(0, Theme.S(3), 0, Theme.S(3)) };
                item.Click += (s, a) => SelectedIndex = index; menu.Items.Add(item);
            }
            menu.MinimumSize = new Size(Width, 0);
            menu.Closed += (s, a) => BeginInvoke(new Action(menu.Dispose));
            menu.Show(this, new Point(0, Height + Theme.S(4)));
        }
        protected override bool IsInputKey(Keys keyData) {
            return keyData == Keys.Up || keyData == Keys.Down || base.IsInputKey(keyData);
        }
        protected override bool ProcessDialogKey(Keys keyData) {
            if (keyData == Keys.Enter || keyData == Keys.Space) { OnClick(EventArgs.Empty); return true; }
            return base.ProcessDialogKey(keyData);
        }
        protected override void OnKeyDown(KeyEventArgs e) {
            if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down) {
                SelectedIndex = Math.Max(0, Math.Min(items.Length - 1, selected + (e.KeyCode == Keys.Up ? -1 : 1))); e.Handled = true;
            }
            base.OnKeyDown(e);
        }
        protected override void OnPaint(PaintEventArgs e) {
            e.Graphics.Clear(Parent.BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var shape = Shapes.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Theme.S(8))) {
                using (var fill = new SolidBrush(!Enabled ? Theme.Card : hover ? Color.FromArgb(33, 42, 52) : BackColor)) e.Graphics.FillPath(fill, shape);
                using (var pen = new Pen(Focused ? Theme.Accent : Theme.FieldLine, Focused ? 1.6f : 1f)) e.Graphics.DrawPath(pen, shape);
            }
            var ink = Enabled ? ForeColor : Theme.Faint;
            int pad = Theme.S(12), arrow = Theme.S(5);
            TextRenderer.DrawText(e.Graphics, items.Length == 0 ? "" : items[selected], Font, new Rectangle(pad, 0, Width - pad * 2 - arrow * 3, Height), ink, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            int cx = Width - pad - arrow, cy = Height / 2;
            using (var pen = new Pen(Enabled ? Theme.Muted : Theme.Faint, 1.6f)) e.Graphics.DrawLines(pen, new[] { new Point(cx - arrow, cy - arrow / 2), new Point(cx, cy + arrow / 2), new Point(cx + arrow, cy - arrow / 2) });
        }
    }

    sealed class QrView : Control {
        bool[,] modules;
        public bool[,] Modules { get { return modules; } set { modules = value; Invalidate(); } }
        public bool Dimmed { get; set; }
        public QrView() { TabStop = false; SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); AccessibleName = "Pairing QR code"; AccessibleRole = AccessibleRole.Graphic; }
        protected override void OnPaint(PaintEventArgs e) {
            var g = e.Graphics; g.Clear(Parent.BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var tile = Shapes.Round(new RectangleF(0, 0, Width - 1, Height - 1), Theme.S(10)))
            using (var white = new SolidBrush(Color.White)) g.FillPath(white, tile);
            if (modules == null) return;
            g.SmoothingMode = SmoothingMode.None;
            int count = modules.GetLength(0), quiet = 3;
            float cell = (float)Math.Floor(Math.Min(Width, Height) / (float)(count + quiet * 2));
            float offset = (Math.Min(Width, Height) - cell * count) / 2f;
            using (var ink = new SolidBrush(Color.FromArgb(12, 16, 20)))
                for (int y = 0; y < count; y++)
                    for (int x = 0; x < count; x++)
                        if (modules[y, x]) g.FillRectangle(ink, offset + x * cell, offset + y * cell, cell, cell);
            if (Dimmed) using (var veil = new SolidBrush(Color.FromArgb(200, 255, 255, 255))) g.FillRectangle(veil, 0, 0, Width, Height);
        }
    }

    // Phone illustration that reflects the connection state.
    sealed class PhoneArtwork : Control {
        int state;
        public int State { get { return state; } set { if (state != value) { state = value; Invalidate(); } } } // 0 idle, 1 found, 2 streaming
        public PhoneArtwork() { TabStop = false; SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }
        protected override void OnPaint(PaintEventArgs e) {
            var g = e.Graphics; g.Clear(Parent.BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = Math.Min(Width / 240f, Height / 180f);
            g.TranslateTransform((Width - 240 * s) / 2, (Height - 180 * s) / 2); g.ScaleTransform(s, s);
            Color line = state == 0 ? Color.FromArgb(62, 76, 88) : Theme.Accent;
            using (var glow = new SolidBrush(Color.FromArgb(state == 0 ? 0 : state == 1 ? 18 : 30, Theme.Accent))) g.FillEllipse(glow, 40, 10, 160, 160);
            using (var screen = new SolidBrush(state == 2 ? Color.FromArgb(24, 52, 46) : Color.FromArgb(24, 31, 39)))
            using (var pen = new Pen(line, 2.5f))
            using (var body = Shapes.Round(new RectangleF(80, 14, 80, 152), 16)) { g.FillPath(screen, body); g.DrawPath(pen, body); }
            using (var pen = new Pen(line, 3) { StartCap = LineCap.Round, EndCap = LineCap.Round }) {
                g.DrawLine(pen, 108, 27, 132, 27);
                if (state == 2) g.DrawBezier(pen, new PointF(94, 118), new PointF(108, 62), new PointF(128, 150), new PointF(146, 72));
            }
            using (var pen = new Pen(Color.FromArgb(state == 0 ? 50 : 120, line), 2) { DashStyle = state == 0 ? DashStyle.Dot : DashStyle.Solid, StartCap = LineCap.Round, EndCap = LineCap.Round }) {
                g.DrawArc(pen, 170, 70, 26, 40, -50, 100); g.DrawArc(pen, 178, 58, 38, 64, -50, 100);
                g.DrawArc(pen, 44, 70, 26, 40, 130, 100); g.DrawArc(pen, 24, 58, 38, 64, 130, 100);
            }
        }
    }
}
