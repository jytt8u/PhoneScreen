using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PhoneScreen {
    static class Program {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [STAThread] static int Main(string[] args) {
            bool render = args.Length == 2 && (args[0] == "--render-preview" || args[0] == "--render-settings");
            SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Mutex mutex = null;
            try {
                if (!render) {
                    bool created;
                    mutex = new Mutex(true, @"Local\PhoneScreenLauncher", out created);
                    if (!created) {
                        MessageBox.Show("PhoneScreen is already open.", "PhoneScreen");
                        return 0;
                    }
                }
                using (var form = new MainForm(AppDomain.CurrentDomain.BaseDirectory, render)) {
                    if (!render) { Application.Run(form); return 0; }
                    form.PrepareDemo(args[0] == "--render-settings");
                    // Create the window off-screen and capture it without showing anything to the user.
                    form.ShowInTaskbar = false;
                    form.Opacity = 0;
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(-32000, -32000);
                    form.Show();
                    Application.DoEvents();
                    form.ActiveControl = null;
                    form.PerformLayout();
                    using (var image = new Bitmap(form.Width, form.Height)) {
                        form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
                        // Keep only the client area: DrawToBitmap paints a classic, light title bar.
                        var origin = form.PointToScreen(Point.Empty);
                        var client = new Rectangle(origin.X - form.Left, origin.Y - form.Top, form.ClientSize.Width, form.ClientSize.Height);
                        using (var cropped = image.Clone(client, image.PixelFormat)) cropped.Save(Path.GetFullPath(args[1]));
                    }
                    form.Hide();
                    return 0;
                }
            } catch (Exception ex) {
                if (render) {
                    File.WriteAllText(Path.GetFullPath(args[1]) + ".error.txt", ex.ToString());
                    return 1;
                }
                MessageBox.Show(ex.Message, "PhoneScreen could not start", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            } finally { if (mutex != null) mutex.Dispose(); }
        }
    }

    enum Tone { Neutral, Good, Warn, Error }

    sealed class MainForm : Form {
        const string GuideUrl = "https://github.com/jytt8u/PhoneScreen/blob/main/GUIDE.md";
        static readonly string[] Profiles = { "Balanced", "Responsive", "Weak Wi-Fi", "Movies" };
        static readonly string[] ProfileHints = { "1600 px · 60 fps · 8 Mb/s", "1280 px · 60 fps · lowest delay", "1024 px · 30 fps · 3 Mb/s", "1920 px · 12 Mb/s · smooth A/V sync" };
        static readonly string[] AudioModes = { "Off", "On this PC", "PC and phone" };
        static readonly string[] AudioHints = { "No sound is captured", "The phone goes quiet", "Android 13+, some apps opt out" };

        readonly string root;
        readonly bool preview;
        readonly Engine engine;

        // Header and footer
        readonly Panel header = new Panel(), footer = new Panel(), body = new Panel(), content = new Panel();
        readonly LogoMark logo = new LogoMark();
        readonly Label appTitle = new Label(), appTagline = new Label();
        readonly LinkLabel guideLink = new LinkLabel(), logLink = new LinkLabel();
        readonly Dot statusDot = new Dot();
        readonly Label status = new Label(), statusDetail = new Label();

        // Left: device
        readonly Card deviceCard = new Card();
        readonly PhoneArtwork artwork = new PhoneArtwork();
        readonly Label deviceName = new Label(), deviceDetail = new Label();
        readonly Divider deviceDivider = new Divider();
        readonly Dot engineDot = new Dot(), networkDot = new Dot(), streamDot = new Dot();
        readonly Label engineText = new Label(), networkText = new Label(), streamText = new Label();
        readonly ActionButton download = new ActionButton(), install = new ActionButton();

        // Connect
        readonly Card connectCard = new Card();
        readonly Label connectTitle = new Label(), connectLead = new Label(), foundText = new Label(), addressHint = new Label();
        readonly Dot foundDot = new Dot();
        readonly LinkLabel chooseLink = new LinkLabel();
        readonly CueTextBox connectAddress = new CueTextBox { Hint = "192.168.1.24:37001" };
        readonly InputFrame connectFrame;
        readonly ActionButton connect = new ActionButton();

        // Pairing
        readonly Card pairCard = new Card();
        readonly Label pairTitle = new Label(), pairLead = new Label(), qrState = new Label(), codeLead = new Label(), codeHint = new Label();
        readonly StepBadge[] stepNumbers = { new StepBadge(), new StepBadge(), new StepBadge() };
        readonly Label[] stepTexts = { new Label(), new Label(), new Label() };
        readonly LinkLabel pairToggle = new LinkLabel();
        readonly QrView qr = new QrView();
        readonly Dot qrDot = new Dot();
        readonly Divider pairDivider = new Divider();
        readonly CueTextBox pairAddress = new CueTextBox { Hint = "192.168.1.24:41235" };
        readonly TextBox pairCode = new CueTextBox { Hint = "000000" };
        readonly InputFrame pairAddressFrame, pairCodeFrame;
        readonly ActionButton pair = new ActionButton();

        // Settings
        readonly Card settingsCard = new Card();
        readonly Label settingsTitle = new Label(), settingsSummary = new Label();
        readonly LinkLabel settingsToggle = new LinkLabel();
        readonly Label qualityLabel = new Label(), audioLabel = new Label(), codecLabel = new Label();
        readonly Label qualityHint = new Label(), audioHint = new Label(), codecHint = new Label();
        readonly OptionPicker quality = new OptionPicker(), audio = new OptionPicker(), codec = new OptionPicker();
        readonly Switch control = new Switch(), keyboard = new Switch(), screenOff = new Switch(), clipboard = new Switch(), onTop = new Switch();

        // Log
        readonly Card logCard = new Card();
        readonly Label logTitle = new Label();
        readonly TextBox log = new TextBox();

        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        readonly StringBuilder sessionOutput = new StringBuilder();

        bool pairingExpanded, settingsExpanded, logExpanded;
        Process mirror;
        TlsBridge bridge;
        string serial;
        bool busy, closing, mayClose;
        Task activeTask, discovery;
        SavedDevice saved = new SavedDevice();
        List<PhoneService> services = new List<PhoneService>();

        string chosenName, autoAddress;
        bool discoveryFailed, discoveryStarted, settingAddress;
        DateTime discoveryStart = DateTime.UtcNow;
        PairingTicket ticket;
        PhoneService pendingQr;
        string attemptedTicket;
        string connectedModel = "", connectedDetail = "";
        DateTime? streamingSince;
        int reconnects;
        string pendingRestart;
        bool audioWarned, chooseVisible, scrollbar;

        public MainForm(string root, bool preview) {
            this.root = root;
            this.preview = preview;
            engine = new Engine(root);
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) Theme.Scale = g.DpiX / 96f;
            AutoScaleMode = AutoScaleMode.None;
            Text = "PhoneScreen";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            BackColor = Theme.Bg; ForeColor = Theme.Ink;
            Font = Theme.Font(10, false);
            var area = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(Math.Min(S(1060), area.Width - S(40)), Math.Min(S(800), area.Height - S(60)));
            MinimumSize = new Size(S(880), S(600));
            StartPosition = FormStartPosition.CenterScreen;
            connectFrame = new InputFrame(connectAddress);
            pairAddressFrame = new InputFrame(pairAddress);
            pairCodeFrame = new InputFrame(pairCode);
            Build();
            LoadState();
            ExpandPairing(saved.Service.Length == 0 && connectAddress.TextLength == 0);
            UpdateControls();
            SetStatus("Ready", Package.Ready(root)
                ? "Turn on Wireless debugging on your phone. PhoneScreen looks for it on your Wi-Fi."
                : "Install the scrcpy engine to get started. It is a one-time download.", Tone.Neutral);
            WireEvents();
            Resize += (s, e) => Arrange();
            Arrange();
        }

        static int S(int value) { return Theme.S(value); }

        Label Lbl(Control parent, Label label, string text, float size, Color color, bool strong) {
            label.Text = text; label.UseMnemonic = false; label.AutoSize = false; label.ForeColor = color;
            label.Font = Theme.Font(size, strong); label.BackColor = Color.Transparent;
            parent.Controls.Add(label); return label;
        }
        void Link(Control parent, LinkLabel link, string text) {
            link.Text = text; link.AutoSize = false; link.Font = Theme.Font(10, false);
            link.LinkColor = Theme.Accent; link.ActiveLinkColor = Theme.AccentHover; link.VisitedLinkColor = Theme.Accent;
            link.DisabledLinkColor = Theme.Faint; link.LinkBehavior = LinkBehavior.HoverUnderline; link.BackColor = Color.Transparent;
            parent.Controls.Add(link);
        }
        void Btn(Control parent, ActionButton button, string text, bool primary) {
            button.Text = text; button.Primary = primary; parent.Controls.Add(button);
        }

        void Build() {
            SuspendLayout();
            header.BackColor = Theme.Bg; footer.BackColor = Theme.Card; body.BackColor = Theme.Bg; content.BackColor = Theme.Bg;
            body.AutoScroll = true;
            header.Controls.Add(logo);
            Lbl(header, appTitle, "PhoneScreen", 15, Theme.Ink, true);
            Lbl(header, appTagline, "Your Android screen, input and sound on this PC", 9.5f, Theme.Muted, false);
            Link(header, guideLink, "Help"); guideLink.TextAlign = ContentAlignment.MiddleRight;
            Link(header, logLink, "Show log"); logLink.TextAlign = ContentAlignment.MiddleRight;

            footer.Controls.Add(statusDot);
            Lbl(footer, status, "", 10.5f, Theme.Ink, true);
            Lbl(footer, statusDetail, "", 9, Theme.Muted, false);
            statusDetail.AutoEllipsis = true;

            // Device card
            deviceCard.Controls.Add(artwork);
            Lbl(deviceCard, deviceName, "", 13, Theme.Ink, true).TextAlign = ContentAlignment.MiddleCenter;
            Lbl(deviceCard, deviceDetail, "", 9, Theme.Muted, false).TextAlign = ContentAlignment.MiddleCenter;
            deviceName.AutoEllipsis = deviceDetail.AutoEllipsis = true;
            deviceCard.Controls.Add(deviceDivider);
            foreach (var dot in new[] { engineDot, networkDot, streamDot }) deviceCard.Controls.Add(dot);
            foreach (var label in new[] { engineText, networkText, streamText }) { Lbl(deviceCard, label, "", 9.5f, Theme.Muted, false); label.AutoEllipsis = true; }
            Btn(deviceCard, download, "Install engine", true);
            Btn(deviceCard, install, "I have the ZIP", false);

            // Connect card
            Lbl(connectCard, connectTitle, "Connect your phone", 15, Theme.Ink, true);
            Lbl(connectCard, connectLead, "Turn on Wireless debugging on the phone and keep both devices on the same Wi-Fi.", 9.5f, Theme.Muted, false);
            connectLead.AutoEllipsis = true;
            connectCard.Controls.Add(foundDot);
            Lbl(connectCard, foundText, "", 10, Theme.Ink, false); foundText.AutoEllipsis = true;
            Link(connectCard, chooseLink, "Choose phone"); chooseLink.TextAlign = ContentAlignment.MiddleRight; chooseLink.Visible = false;
            connectCard.Controls.Add(connectFrame);
            connectAddress.MaxLength = 21; connectAddress.AccessibleName = "Phone address";
            Btn(connectCard, connect, "Connect", true);
            Lbl(connectCard, addressHint, "", 9, Theme.Muted, false); addressHint.AutoEllipsis = true;

            // Pair card
            Lbl(pairCard, pairTitle, "Pair a new phone", 12, Theme.Ink, true);
            Lbl(pairCard, pairLead, "Only needed once for each phone.", 9.5f, Theme.Muted, false);
            Link(pairCard, pairToggle, "Show"); pairToggle.TextAlign = ContentAlignment.MiddleRight;
            pairCard.Controls.Add(qr);
            string[] steps = {
                "On the phone, open Settings → Developer options → Wireless debugging.",
                "Tap “Pair device with QR code” and point the camera at this code.",
                "That's it. PhoneScreen pairs and connects on its own."
            };
            for (int i = 0; i < 3; i++) {
                stepNumbers[i].Text = (i + 1).ToString(); pairCard.Controls.Add(stepNumbers[i]);
                Lbl(pairCard, stepTexts[i], steps[i], 10, Theme.Ink, false);
            }
            pairCard.Controls.Add(qrDot);
            Lbl(pairCard, qrState, "", 9.5f, Theme.Muted, false); qrState.AutoEllipsis = true;
            pairCard.Controls.Add(pairDivider);
            Lbl(pairCard, codeLead, "No camera? Use “Pair device with pairing code” instead:", 9.5f, Theme.Muted, false);
            pairCard.Controls.Add(pairAddressFrame); pairCard.Controls.Add(pairCodeFrame);
            pairAddress.MaxLength = 21; pairCode.MaxLength = 6; pairCode.UseSystemPasswordChar = true;
            pairAddress.AccessibleName = "Pairing address"; pairCode.AccessibleName = "Six-digit pairing code";
            Btn(pairCard, pair, "Pair", false);
            Lbl(pairCard, codeHint, "The address fills in by itself when the phone shows the code.", 9, Theme.Faint, false);
            codeHint.AutoEllipsis = true;

            // Settings card
            Lbl(settingsCard, settingsTitle, "Stream settings", 12, Theme.Ink, true);
            Lbl(settingsCard, settingsSummary, "", 9.5f, Theme.Muted, false); settingsSummary.AutoEllipsis = true;
            Link(settingsCard, settingsToggle, "Change"); settingsToggle.TextAlign = ContentAlignment.MiddleRight;
            Lbl(settingsCard, qualityLabel, "Picture", 9, Theme.Muted, true);
            Lbl(settingsCard, audioLabel, "Sound", 9, Theme.Muted, true);
            Lbl(settingsCard, codecLabel, "Audio codec", 9, Theme.Muted, true);
            quality.SetOptions(Profiles); audio.SetOptions(AudioModes); codec.SetOptions(new[] { "Opus", "AAC" });
            audio.SelectedIndex = 1;
            quality.AccessibleName = "Picture quality"; audio.AccessibleName = "Sound"; codec.AccessibleName = "Audio codec";
            foreach (var picker in new[] { quality, audio, codec }) settingsCard.Controls.Add(picker);
            foreach (var hint in new[] { qualityHint, audioHint, codecHint }) { Lbl(settingsCard, hint, "", 8.5f, Theme.Faint, false); hint.AutoEllipsis = true; }
            control.Text = "Control with mouse and keyboard"; keyboard.Text = "Hardware keyboard (any layout)";
            screenOff.Text = "Turn the phone screen off"; clipboard.Text = "Share clipboard"; onTop.Text = "Keep window on top";
            control.Checked = keyboard.Checked = true;
            foreach (var item in new[] { control, keyboard, screenOff, clipboard, onTop }) settingsCard.Controls.Add(item);

            // Log card
            Lbl(logCard, logTitle, "Connection log", 10, Theme.Muted, true);
            log.Multiline = true; log.ReadOnly = true; log.ScrollBars = ScrollBars.Vertical; log.BorderStyle = BorderStyle.None;
            log.BackColor = Theme.Card; log.ForeColor = Theme.Muted; log.Font = new Font("Consolas", 9); log.WordWrap = true;
            logCard.Controls.Add(log); logCard.Visible = false;

            // Tab order follows the visual flow.
            foreach (var card in new Control[] { connectCard, pairCard, settingsCard, logCard, deviceCard }) content.Controls.Add(card);
            body.Controls.Add(content);
            Controls.Add(body); Controls.Add(footer); Controls.Add(header);
            int tab = 0;
            foreach (Control c in new Control[] { connectFrame, connect, chooseLink, pairToggle, pairAddressFrame, pairCodeFrame, pair, settingsToggle,
                quality, audio, codec, control, keyboard, screenOff, clipboard, onTop, download, install, guideLink, logLink }) c.TabIndex = tab++;
            ResumeLayout(false);
        }

        void Arrange() {
            if (ClientSize.Width == 0 || ClientSize.Height == 0) return;
            SuspendLayout();
            int width = ClientSize.Width, pad = S(24), gap = S(16);
            header.SetBounds(0, 0, width, S(76));
            logo.SetBounds(pad, S(18), S(40), S(40));
            appTitle.SetBounds(pad + S(52), S(14), S(300), S(30));
            appTagline.SetBounds(pad + S(53), S(43), S(420), S(20));
            logLink.SetBounds(width - pad - S(90), S(26), S(90), S(24));
            guideLink.SetBounds(logLink.Left - S(70), S(26), S(60), S(24));

            footer.SetBounds(0, ClientSize.Height - S(62), width, S(62));
            statusDot.SetBounds(pad, S(16), S(14), S(14));
            status.SetBounds(pad + S(22), S(10), width - pad * 2 - S(22), S(24));
            statusDetail.SetBounds(pad + S(22), S(34), width - pad * 2 - S(22), S(20));

            body.SetBounds(0, header.Bottom, width, footer.Top - header.Bottom);
            int inner = body.Width - pad * 2 - (scrollbar ? SystemInformation.VerticalScrollBarWidth : 0);
            int leftWidth = S(256), rightX = pad + leftWidth + gap, rightWidth = inner - leftWidth - gap;

            // Device card
            int y = S(12);
            artwork.SetBounds(S(12), y, leftWidth - S(24), S(168)); y = artwork.Bottom + S(6);
            deviceName.SetBounds(S(16), y, leftWidth - S(32), S(28)); y += S(28);
            deviceDetail.SetBounds(S(16), y, leftWidth - S(32), S(20)); y += S(34);
            deviceDivider.SetBounds(S(20), y, leftWidth - S(40), S(2)); y += S(14);
            foreach (var row in new[] { Tuple.Create(engineDot, engineText), Tuple.Create(networkDot, networkText), Tuple.Create(streamDot, streamText) }) {
                row.Item1.SetBounds(S(22), y + S(5), S(12), S(12));
                row.Item2.SetBounds(S(42), y, leftWidth - S(58), S(22)); y += S(30);
            }
            bool ready = Package.Ready(root);
            if (!ready) {
                y += S(6);
                download.SetBounds(S(20), y, leftWidth - S(40), S(40)); y += S(48);
                install.SetBounds(S(20), y, leftWidth - S(40), S(36)); y += S(36);
            }
            deviceCard.SetBounds(pad, S(8), leftWidth, y + S(18));

            // Connect card
            int cw = rightWidth, cp = S(24);
            connectTitle.SetBounds(cp, S(18), cw - cp * 2, S(32));
            connectLead.SetBounds(cp, S(50), cw - cp * 2, S(22));
            foundDot.SetBounds(cp, S(92), S(14), S(14));
            chooseLink.SetBounds(cw - cp - S(150), S(87), S(150), S(24));
            foundText.SetBounds(cp + S(22), S(87), (chooseVisible ? chooseLink.Left - S(8) : cw - cp) - cp - S(22), S(24));
            int buttonWidth = S(170);
            connectFrame.SetBounds(cp, S(122), cw - cp * 2 - buttonWidth - S(12), S(46));
            connect.SetBounds(cw - cp - buttonWidth, S(122), buttonWidth, S(46));
            addressHint.SetBounds(cp, S(174), cw - cp * 2, S(20));
            connectCard.SetBounds(rightX, S(8), cw, S(208));
            y = connectCard.Bottom + gap;

            // Pair card
            pairTitle.SetBounds(cp, S(16), cw - cp * 2 - S(100), S(26));
            pairLead.SetBounds(cp, S(42), cw - cp * 2 - S(100), S(20));
            pairToggle.SetBounds(cw - cp - S(100), S(26), S(100), S(24));
            foreach (Control c in new Control[] { qr, qrDot, qrState, pairDivider, codeLead, pairAddressFrame, pairCodeFrame, pair, codeHint })
                c.Visible = pairingExpanded;
            foreach (var label in stepNumbers.Cast<Control>().Concat(stepTexts)) label.Visible = pairingExpanded;
            if (pairingExpanded) {
                int qrSize = S(172), top = S(80);
                qr.SetBounds(cp, top, qrSize, qrSize);
                int sx = cp + qrSize + S(24), sw = cw - sx - cp;
                for (int i = 0; i < 3; i++) {
                    stepNumbers[i].SetBounds(sx, top + S(2) + i * S(46), S(24), S(24));
                    stepTexts[i].SetBounds(sx + S(34), top + S(2) + i * S(46), sw - S(34), S(42));
                }
                qrDot.SetBounds(sx + S(4), top + S(150), S(14), S(14));
                qrState.SetBounds(sx + S(34), top + S(145), sw - S(34), S(24));
                int lower = top + qrSize + S(22);
                pairDivider.SetBounds(cp, lower, cw - cp * 2, S(2));
                codeLead.SetBounds(cp, lower + S(14), cw - cp * 2, S(22));
                int codeWidth = S(140), pairWidth = S(110), row = lower + S(42);
                pairAddressFrame.SetBounds(cp, row, cw - cp * 2 - codeWidth - pairWidth - S(24), S(42));
                pairCodeFrame.SetBounds(pairAddressFrame.Right + S(12), row, codeWidth, S(42));
                pair.SetBounds(pairCodeFrame.Right + S(12), row, pairWidth, S(42));
                codeHint.SetBounds(cp, row + S(48), cw - cp * 2, S(20));
                pairCard.SetBounds(rightX, y, cw, codeHint.Bottom + S(18));
            } else pairCard.SetBounds(rightX, y, cw, S(78));
            y = pairCard.Bottom + gap;

            // Settings card
            settingsTitle.SetBounds(cp, S(16), cw - cp * 2 - S(100), S(26));
            settingsSummary.SetBounds(cp, S(42), cw - cp * 2 - S(100), S(20));
            settingsToggle.SetBounds(cw - cp - S(100), S(26), S(100), S(24));
            foreach (Control c in new Control[] { qualityLabel, audioLabel, codecLabel, quality, audio, codec, qualityHint, audioHint, codecHint, control, keyboard, screenOff, clipboard, onTop })
                c.Visible = settingsExpanded;
            if (settingsExpanded) {
                int col = (cw - cp * 2 - S(24)) / 3, top = S(82);
                var labels = new[] { qualityLabel, audioLabel, codecLabel };
                var pickers = new[] { quality, audio, codec };
                var hints = new[] { qualityHint, audioHint, codecHint };
                for (int i = 0; i < 3; i++) {
                    int x = cp + i * (col + S(12));
                    labels[i].SetBounds(x, top, col, S(20));
                    pickers[i].SetBounds(x, top + S(22), col, S(40));
                    hints[i].SetBounds(x + S(2), top + S(66), col, S(18));
                }
                int half = (cw - cp * 2 - S(24)) / 2, sy = top + S(98);
                var switches = new[] { control, keyboard, screenOff, clipboard, onTop };
                for (int i = 0; i < switches.Length; i++)
                    switches[i].SetBounds(cp + (i % 2) * (half + S(24)), sy + (i / 2) * S(36), half, S(30));
                settingsCard.SetBounds(rightX, y, cw, sy + S(3 * 36) + S(10));
            } else settingsCard.SetBounds(rightX, y, cw, S(78));
            y = settingsCard.Bottom + gap;

            logCard.Visible = logExpanded;
            if (logExpanded) {
                logTitle.SetBounds(cp, S(14), cw - cp * 2, S(22));
                log.SetBounds(cp, S(44), cw - cp * 2, S(150));
                logCard.SetBounds(rightX, y, cw, S(210));
                y = logCard.Bottom + gap;
            }

            int height = Math.Max(y, deviceCard.Bottom + gap) + S(8);
            bool needed = height > body.Height;
            if (needed != scrollbar) { scrollbar = needed; ResumeLayout(false); Arrange(); return; }
            body.AutoScrollMinSize = new Size(0, needed ? height : 0);
            content.SetBounds(0, body.AutoScrollPosition.Y, inner + pad * 2, Math.Max(height, body.Height));
            ResumeLayout(true);
            Invalidate(true);
        }

        void WireEvents() {
            guideLink.LinkClicked += (s, e) => OpenLink(GuideUrl);
            logLink.LinkClicked += (s, e) => { logExpanded = !logExpanded; logLink.Text = logExpanded ? "Hide log" : "Show log"; Arrange(); if (logExpanded) body.ScrollControlIntoView(logCard); };
            pairToggle.LinkClicked += (s, e) => ExpandPairing(!pairingExpanded);
            settingsToggle.LinkClicked += (s, e) => ExpandSettings(!settingsExpanded);
            chooseLink.LinkClicked += (s, e) => ChoosePhone();
            connect.Click += async (s, e) => {
                if (mirror != null) await RunUi(async () => await StopAsync(true));
                else { reconnects = 0; await RunUi(ConnectAsync); }
            };
            pair.Click += async (s, e) => await RunUi(PairWithCodeAsync);
            download.Click += async (s, e) => await RunUi(async () => {
                var progress = new Progress<int>(value => SetStatus("Installing the engine · " + value + "%",
                    "Downloading the official scrcpy release and checking its SHA-256.", Tone.Neutral));
                await Package.DownloadAndInstallAsync(root, progress, lifetime.Token);
                AddLog("scrcpy installed. Checksums match.");
                SetStatus("Engine installed", "Turn on Wireless debugging on your phone.", Tone.Good);
                Arrange();
            });
            install.Click += async (s, e) => {
                using (var picker = new OpenFileDialog { Title = "Choose the official scrcpy-win64-v4.1.zip", Filter = "scrcpy ZIP|*.zip", CheckFileExists = true }) {
                    if (picker.ShowDialog(this) != DialogResult.OK) return;
                    string path = picker.FileName;
                    await RunUi(async () => {
                        SetStatus("Checking the archive", "Verifying its SHA-256 before installing.", Tone.Neutral);
                        await Task.Run(() => Package.Install(path, root));
                        AddLog("Installed scrcpy " + Package.Version + " from a local ZIP. SHA-256 matches.");
                        SetStatus("Engine installed", "Turn on Wireless debugging on your phone.", Tone.Good);
                        Arrange();
                    });
                }
            };
            connectAddress.TextChanged += (s, e) => {
                if (!settingAddress) { autoAddress = null; chosenName = null; }
                if (connectFrame.Invalid) ValidateAddressHint(false);
                UpdateControls();
            };
            connectAddress.Leave += (s, e) => ValidateAddressHint(false);
            pairAddress.TextChanged += (s, e) => UpdateControls();
            pairCode.TextChanged += (s, e) => UpdateControls();
            pairCode.KeyPress += (s, e) => { if (!Char.IsControl(e.KeyChar) && !Char.IsDigit(e.KeyChar)) e.Handled = true; };
            // Enter submits the section the cursor is in.
            foreach (var box in new[] { pairAddress, pairCode }) {
                box.Enter += (s, e) => AcceptButton = pair;
                box.Leave += (s, e) => AcceptButton = connect;
            }
            AcceptButton = connect;
            EventHandler settingsChanged = (s, e) => { UpdateControls(); if (!busy) TrySavePreferences(); };
            foreach (var picker in new[] { quality, audio, codec }) picker.SelectedIndexChanged += settingsChanged;
            foreach (var item in new[] { control, keyboard, screenOff, clipboard, onTop }) item.CheckedChanged += settingsChanged;
            timer.Interval = 400;
            timer.Tick += async (s, e) => await Tick();
            timer.Start();
            FormClosing += CloseAsync;
        }

        protected override void OnHandleCreated(EventArgs e) {
            base.OnHandleCreated(e);
            DarkMode.TitleBar(Handle);
            DarkMode.ScrollBars(body.Handle);
            DarkMode.ScrollBars(log.Handle);
        }

        protected override void OnShown(EventArgs e) {
            base.OnShown(e);
            if (!preview) DiscoveryLoop();
        }

        // ---------- State and presentation ----------

        void ExpandPairing(bool expanded) {
            pairingExpanded = expanded;
            pairToggle.Text = expanded ? "Hide" : "Show";
            if (expanded && ticket == null) NewTicket();
            Arrange();
        }
        void ExpandSettings(bool expanded) {
            settingsExpanded = expanded; settingsToggle.Text = expanded ? "Done" : "Change";
            Arrange();
        }
        void NewTicket() {
            ticket = PairingTicket.Create();
            qr.Modules = QrCode.Encode(ticket.Payload);
        }

        void UpdateControls() {
            if (IsDisposed) return;
            bool ready = Package.Ready(root);
            bool idle = !busy && !closing && mirror == null;
            download.Visible = install.Visible = !ready;
            download.Enabled = install.Enabled = !ready && idle;
            connect.Text = mirror != null ? "Disconnect" : "Connect";
            connect.Primary = mirror == null;
            connect.Enabled = ready && !closing && !busy;
            pair.Enabled = ready && idle && ValidAddress(pairAddress.Text) && Regex.IsMatch(pairCode.Text, @"^[0-9]{6}$");
            pairAddress.Enabled = pairCode.Enabled = connectAddress.Enabled = idle;
            pairAddressFrame.Enabled = pairCodeFrame.Enabled = connectFrame.Enabled = idle;
            pairToggle.Enabled = idle || pairingExpanded;
            chooseLink.Enabled = idle;
            qr.Dimmed = !ready || !idle; qr.Invalidate();
            foreach (var picker in new Control[] { quality, audio, control }) picker.Enabled = idle;
            codec.Enabled = idle && audio.SelectedIndex != 0;
            keyboard.Enabled = screenOff.Enabled = clipboard.Enabled = idle && control.Checked;
            onTop.Enabled = idle;
            if (!pairAddress.Focused && !pairCode.Focused) AcceptButton = mirror == null ? connect : null;

            qualityHint.Text = ProfileHints[quality.SelectedIndex];
            audioHint.Text = AudioHints[audio.SelectedIndex];
            codecHint.Text = codec.SelectedIndex == 0 ? "Best quality, low delay" : "Try this if there's no sound";
            settingsSummary.Text = Profiles[quality.SelectedIndex] + " · " + (audio.SelectedIndex == 0 ? "no sound" : audio.SelectedIndex == 1 ? "sound on PC" : "sound on PC and phone")
                + (audio.SelectedIndex == 0 ? "" : " (" + (codec.SelectedIndex == 0 ? "Opus" : "AAC") + ")") + " · " + (control.Checked ? "mouse and keyboard" : "view only");

            if (pairingExpanded) {
                if (!ready) SetQrState("Install the engine first", Theme.Faint);
                else if (busy && pendingQr == null && attemptedTicket != null && attemptedTicket == ticket.Name) SetQrState("Pairing…", Theme.Accent);
                else if (!idle) SetQrState("Paused while connected", Theme.Faint);
                else SetQrState("Waiting for the phone to scan…", Theme.Muted);
            }
            UpdateDevicePanel();
        }
        void SetQrState(string text, Color color) { qrState.Text = text; qrDot.Color = color; }

        void UpdateDevicePanel() {
            bool ready = Package.Ready(root);
            var connects = services.Where(s => !s.Pairing).ToList();
            var target = Target();
            engineDot.Color = ready ? Theme.Accent : Theme.Warning;
            engineText.Text = ready ? "Engine ready · scrcpy " + Package.Version : "Engine not installed";
            engineText.ForeColor = ready ? Theme.Muted : Theme.Ink;
            if (!ready) { networkDot.Color = Theme.Faint; networkText.Text = "Phone search is off"; }
            else if (discoveryFailed) { networkDot.Color = Theme.Warning; networkText.Text = "Auto-search unavailable"; }
            else if (connects.Count > 0) { networkDot.Color = Theme.Accent; networkText.Text = connects.Count == 1 ? "1 phone on your Wi-Fi" : connects.Count + " phones on your Wi-Fi"; }
            else { networkDot.Color = Theme.Faint; networkText.Text = discoveryStarted ? "Looking for phones…" : "Starting search…"; }
            streamDot.Color = mirror != null ? Theme.Accent : Theme.Faint;
            streamText.Text = mirror != null ? (streamingSince != null ? "Streaming" : "Starting the stream…") : "Not streaming";

            if (mirror != null) {
                artwork.State = 2;
                deviceName.Text = connectedModel.Length > 0 ? connectedModel : "Your phone";
                deviceDetail.Text = connectedDetail;
            } else if (target != null) {
                artwork.State = 1;
                deviceName.Text = DisplayName(target);
                deviceDetail.Text = "Ready · " + target.Address;
            } else {
                artwork.State = 0;
                deviceName.Text = saved.Model.Length > 0 ? saved.Model : "No phone yet";
                deviceDetail.Text = saved.Model.Length > 0 ? "Not found on the network right now" : "Turn on Wireless debugging";
            }

            if (mirror != null) { foundDot.Color = Theme.Accent; foundText.Text = "Connected to " + (connectedModel.Length > 0 ? connectedModel : "your phone"); }
            else if (!ready) { foundDot.Color = Theme.Faint; foundText.Text = "Install the engine to find your phone automatically."; }
            else if (target != null) { foundDot.Color = Theme.Accent; foundText.Text = "Found " + DisplayName(target) + " · " + target.Address; }
            else if (discoveryFailed) { foundDot.Color = Theme.Warning; foundText.Text = "Automatic search isn't available. Enter the address from the phone."; }
            else if ((DateTime.UtcNow - discoveryStart).TotalSeconds > 20) { foundDot.Color = Theme.Faint; foundText.Text = "No phone found yet. Is Wireless debugging on? You can also type the address."; }
            else { foundDot.Color = Theme.Faint; foundText.Text = "Looking for phones on your Wi-Fi…"; }
            foundText.ForeColor = target != null || mirror != null ? Theme.Ink : Theme.Muted;
            bool choose = mirror == null && connects.Count > 1;
            if (chooseVisible != choose) { chooseVisible = choose; chooseLink.Visible = choose; Arrange(); }
            chooseLink.Text = "Choose phone (" + connects.Count + ")";
            if (!connectFrame.Invalid) addressHint.Text = target != null ? "Filled in automatically. Press Enter or Connect."
                : "Address and port are on the phone's Wireless debugging screen, e.g. 192.168.1.24:37001.";
        }

        string DisplayName(PhoneService service) {
            if (service.Name == saved.Service && saved.Model.Length > 0) return saved.Model;
            var match = Regex.Match(service.Name, @"^adb-([A-Za-z0-9]+)-");
            return match.Success ? "Android phone " + match.Groups[1].Value.Substring(0, Math.Min(6, match.Groups[1].Value.Length)) : "Android phone";
        }

        // The phone Connect will use: the one picked by hand, the one used last time, or the only one around.
        PhoneService Target() {
            var connects = services.Where(s => !s.Pairing).ToList();
            if (connects.Count == 0) return null;
            if (chosenName != null) { var c = connects.FirstOrDefault(s => s.Name == chosenName); if (c != null) return c; }
            var known = connects.FirstOrDefault(s => s.Name == saved.Service);
            if (known != null) return known;
            PhoneAddress typed = null;
            try { typed = PhoneAddress.Parse(connectAddress.Text); } catch (ArgumentException) { }
            if (typed != null) { var sameIp = connects.FirstOrDefault(s => s.Address.IP.Equals(typed.IP)); if (sameIp != null) return sameIp; }
            return connects.Count == 1 ? connects[0] : null;
        }

        void ChoosePhone() {
            var connects = services.Where(s => !s.Pairing).ToList();
            var menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), Font = Theme.Font(10, false), ShowImageMargin = false, ShowCheckMargin = true };
            var target = Target();
            foreach (var service in connects) {
                var item = new ToolStripMenuItem(DisplayName(service) + "  ·  " + service.Address) { Checked = target != null && target.Name == service.Name };
                var pick = service;
                item.Click += (s, e) => { chosenName = pick.Name; FillAddress(pick); UpdateControls(); };
                menu.Items.Add(item);
            }
            menu.Closed += (s, e) => BeginInvoke(new Action(menu.Dispose));
            menu.Show(chooseLink, new Point(0, chooseLink.Height));
        }

        void FillAddress(PhoneService service) {
            string text = service.Address.ToString();
            if (connectAddress.Text == text) { autoAddress = text; return; }
            settingAddress = true;
            try { connectAddress.Text = text; autoAddress = text; } finally { settingAddress = false; }
            connectFrame.Invalid = false;
        }

        void ApplyServices(List<PhoneService> found) {
            services = found;
            string chosenKeep = chosenName;
            var target = Target();
            if (target != null) {
                PhoneAddress typed = null;
                try { typed = PhoneAddress.Parse(connectAddress.Text); } catch (ArgumentException) { }
                // Fill an empty or auto-filled field, and follow port changes: Android picks a new port every time debugging restarts.
                bool fieldIsOurs = connectAddress.TextLength == 0 || autoAddress == connectAddress.Text || connectAddress.Text == saved.Address
                    || (typed != null && typed.IP.Equals(target.Address.IP)) || chosenKeep == target.Name;
                bool typing = connectAddress.Focused && connectAddress.TextLength > 0 && connectAddress.Text != autoAddress && connectAddress.Text != saved.Address;
                if (fieldIsOurs && !typing) FillAddress(target);
            }
            // The pairing-code popup also advertises itself; prefill its address.
            var codePairing = found.FirstOrDefault(s => s.Pairing && (ticket == null || s.Name != ticket.Name));
            if (codePairing != null && !pairAddress.Focused && (pairAddress.TextLength == 0 || pairAddress.Tag as string == pairAddress.Text)) {
                pairAddress.Text = codePairing.Address.ToString(); pairAddress.Tag = pairAddress.Text;
            }
            if (pairingExpanded && ticket != null && attemptedTicket != ticket.Name) {
                var scanned = found.FirstOrDefault(s => s.Pairing && s.Name == ticket.Name);
                if (scanned != null) pendingQr = scanned;
            }
            UpdateControls();
        }

        void SetStatus(string title, string detail, Tone tone) {
            if (IsDisposed) return;
            status.Text = title; statusDetail.Text = detail;
            status.ForeColor = tone == Tone.Error ? Theme.Danger : tone == Tone.Warn ? Theme.Warning : Theme.Ink;
            statusDot.Color = tone == Tone.Good ? Theme.Accent : tone == Tone.Warn ? Theme.Warning : tone == Tone.Error ? Theme.Danger : Theme.Muted;
        }
        void AddLog(string line) {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired) { try { BeginInvoke(new Action<string>(AddLog), line); } catch (InvalidOperationException) { } return; }
            if (log.TextLength > 40000) log.Text = log.Text.Substring(log.TextLength - 20000);
            log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + line + Environment.NewLine);
        }
        static bool ValidAddress(string text) { try { PhoneAddress.Parse(text); return true; } catch (ArgumentException) { return false; } }
        void ValidateAddressHint(bool requested) {
            bool invalid = (requested || connectAddress.TextLength != 0) && !ValidAddress(connectAddress.Text);
            connectFrame.Invalid = invalid;
            if (invalid) {
                string reason;
                try { PhoneAddress.Parse(connectAddress.Text); reason = ""; } catch (ArgumentException ex) { reason = ex.Message; }
                addressHint.Text = reason; addressHint.ForeColor = Theme.Warning;
            } else { addressHint.ForeColor = Theme.Muted; UpdateDevicePanel(); }
        }

        // ---------- Background work ----------

        async Task RunUi(Func<Task> action) {
            if (busy || closing) return;
            busy = true; UpdateControls();
            var finished = new TaskCompletionSource<bool>(); activeTask = finished.Task;
            try {
                if (discovery != null) { try { await discovery; } catch { } }
                Exception failure = null;
                try { await action(); } catch (Exception ex) { failure = ex; }
                if (failure != null && !closing) {
                    SetStatus("That didn't work", Friendly(failure), Tone.Error);
                    AddLog(Friendly(failure));
                    // Never leave a half-open connection behind after a failed start.
                    if (mirror == null && serial != null) {
                        try { await engine.AdbAsync("disconnect " + Engine.Quote(serial), null, 4000); } catch { }
                        serial = null;
                    }
                    if (mirror == null && bridge != null) { bridge.Dispose(); bridge = null; }
                }
            } finally { busy = false; activeTask = null; finished.TrySetResult(true); if (!IsDisposed) UpdateControls(); }
        }

        async void DiscoveryLoop() {
            discoveryStart = DateTime.UtcNow;
            int delay = 300;
            while (!closing && !IsDisposed) {
                await Task.Delay(delay);
                delay = services.Count == 0 ? 1500 : 3000;
                if (closing || IsDisposed) break;
                if (pendingQr != null && !busy && mirror == null) {
                    var scanned = pendingQr; pendingQr = null;
                    await RunUi(() => PairWithQrAsync(scanned));
                    continue;
                }
                if (busy || mirror != null || !Package.Ready(root) || WindowState == FormWindowState.Minimized) continue;
                var task = DiscoverOnce();
                discovery = task;
                try { await task; } catch { } finally { discovery = null; }
            }
        }
        async Task DiscoverOnce() {
            try {
                var found = await engine.ServicesAsync();
                if (closing) return;
                if (discoveryFailed) AddLog("Phone search is working again.");
                discoveryFailed = false; discoveryStarted = true;
                ApplyServices(found);
            } catch (Exception ex) {
                if (closing) return;
                if (!discoveryFailed) AddLog("Phone search unavailable: " + Friendly(ex));
                discoveryFailed = true; services = new List<PhoneService>(); UpdateControls();
            }
        }
        async Task RefreshServices() {
            try { ApplyServices(await engine.ServicesAsync()); } catch (Exception ex) { AddLog("Phone search: " + Friendly(ex)); }
        }

        async Task Tick() {
            if (busy || closing) return;
            if (pendingRestart != null && mirror != null) {
                string reason = pendingRestart; pendingRestart = null;
                await RunUi(async () => {
                    SetStatus("Restarting the stream", reason, Tone.Warn);
                    await StopAsync(false);
                    await ConnectAsync();
                });
                return;
            }
            if (mirror == null || !mirror.HasExited) return;
            await RunUi(async () => {
                mirror.WaitForExit();
                int exit = mirror.ExitCode;
                bool wasStreaming = streamingSince != null && (DateTime.UtcNow - streamingSince.Value).TotalSeconds > 5;
                await StopAsync(false);
                if (exit == 0) { SetStatus("Stream closed", "Press Connect to open it again.", Tone.Neutral); return; }
                AddLog("scrcpy exited with code " + exit + ".");
                // Exit code 2 means the device went away. If it was working, try to come back on our own.
                if (exit == 2 && wasStreaming && reconnects < 3) {
                    reconnects++;
                    SetStatus("Connection dropped · reconnecting", "Attempt " + reconnects + " of 3. Keep the phone unlocked and on Wi-Fi.", Tone.Warn);
                    await Task.Delay(1500 * reconnects);
                    await RefreshServices();
                    await ConnectAsync();
                    return;
                }
                SetStatus(exit == 2 ? "The phone disconnected" : "The stream stopped with an error", FailureHint(sessionOutput.ToString()), Tone.Error);
            });
        }

        // ---------- Pairing ----------

        async Task PairWithCodeAsync() {
            var endpoint = PhoneAddress.Parse(pairAddress.Text);
            string code = pairCode.Text.Trim(); pairCode.Clear();
            if (!Regex.IsMatch(code, @"^[0-9]{6}$")) throw new ArgumentException("Enter the six-digit code shown on the phone.");
            SetStatus("Pairing", "Keep the pairing code popup open on the phone.", Tone.Neutral);
            var result = await engine.AdbAsync("pair " + Engine.Quote(endpoint.ToString()), code, 20000);
            code = null;
            if (result.ExitCode != 0 || result.Output.IndexOf("Successfully paired", StringComparison.OrdinalIgnoreCase) < 0)
                throw new IOException("Pairing failed. Open a fresh code on the phone and check the address in the same popup.");
            // ADB output isn't logged: it can echo the stdin prompt.
            await AfterPairedAsync(endpoint, Mdns.PairedGuid(result.Output));
        }

        async Task PairWithQrAsync(PhoneService scanned) {
            var current = ticket;
            if (current == null || scanned.Name != current.Name) return;
            attemptedTicket = current.Name;
            UpdateControls();
            SetStatus("Pairing", "The phone scanned the code. Finishing pairing…", Tone.Neutral);
            CommandResult result;
            try { result = await engine.AdbAsync("pair " + Engine.Quote(scanned.Address.ToString()), current.Password, 20000); }
            finally { NewTicket(); }
            if (result.ExitCode != 0 || result.Output.IndexOf("Successfully paired", StringComparison.OrdinalIgnoreCase) < 0)
                throw new IOException("Pairing failed. Scan the new QR code on the phone to try again.");
            await AfterPairedAsync(scanned.Address, Mdns.PairedGuid(result.Output));
        }

        async Task AfterPairedAsync(PhoneAddress endpoint, string guid) {
            AddLog("Paired with " + endpoint.IP + ". The code was not saved.");
            if (guid != null) { saved.Service = guid; chosenName = guid; TrySaveDevice(); }
            SetStatus("Paired", "Looking for the phone's connection port…", Tone.Good);
            ExpandPairing(false);
            // Right after pairing the phone announces its connection service. Give it a few seconds.
            for (int attempt = 0; attempt < 10 && !closing; attempt++) {
                await RefreshServices();
                var target = services.FirstOrDefault(s => !s.Pairing && (s.Name == guid || s.Address.IP.Equals(endpoint.IP)));
                if (target != null) { chosenName = target.Name; FillAddress(target); await ConnectAsync(); return; }
                await Task.Delay(1000);
            }
            settingAddress = true;
            try { connectAddress.Text = endpoint.IP + ":"; } finally { settingAddress = false; }
            connectAddress.Focus(); connectAddress.SelectionStart = connectAddress.TextLength;
            SetStatus("Paired", "Now enter the port from the main Wireless debugging screen and press Connect.", Tone.Good);
        }

        // ---------- Streaming ----------

        async Task ConnectAsync() {
            if (connectAddress.TextLength == 0) { var found = Target(); if (found != null) FillAddress(found); }
            if (!ValidAddress(connectAddress.Text)) {
                ValidateAddressHint(true); connectAddress.Focus();
                PhoneAddress.Parse(connectAddress.Text);
            }
            var endpoint = PhoneAddress.Parse(connectAddress.Text);
            string address = endpoint.ToString();
            var target = services.FirstOrDefault(s => !s.Pairing && s.Address.ToString() == address);
            SetStatus("Connecting to " + address, "Unlock the phone. If it asks, allow the connection.", Tone.Neutral);
            await engine.InitializeAsync();
            bridge = new TlsBridge(endpoint);
            serial = bridge.Serial;
            var connection = await engine.AdbAsync("connect " + Engine.Quote(serial), null, 18000);
            if (connection.ExitCode != 0 || connection.Output.IndexOf("connected to", StringComparison.OrdinalIgnoreCase) < 0)
                throw new IOException(bridge.Error ?? "The phone didn't answer. Check that Wireless debugging is on and the port is current.");
            try {
                var wait = await engine.AdbAsync("-s " + Engine.Quote(serial) + " wait-for-device", null, 15000);
                if (wait.ExitCode != 0) throw new TimeoutException();
            } catch (TimeoutException) {
                throw new IOException(bridge.Error ?? "The phone didn't accept this PC. Pair it again (removing a paired PC on the phone also requires re-pairing).");
            }
            AddLog("TLS confirmed on the connection. ADB authenticated the phone.");
            var props = await engine.AdbAsync("-s " + Engine.Quote(serial) + " shell "
                + Engine.Quote("getprop ro.build.version.sdk;getprop service.adb.tls.port;getprop ro.product.marketname;getprop ro.product.manufacturer;getprop ro.product.model"), null, 8000);
            var lines = props.Output.Replace("\r", "").Split('\n');
            int sdk, reportedPort;
            if (props.ExitCode != 0 || lines.Length < 5 || !Int32.TryParse(lines[0].Trim(), out sdk))
                throw new IOException("Couldn't read the Android version. Unlock the phone and try again.");
            if (sdk < 30) throw new IOException("Wireless debugging with sound needs Android 11 or newer.");
            if (!Int32.TryParse(lines[1].Trim(), out reportedPort) || reportedPort != endpoint.Port)
                throw new IOException("The phone didn't confirm this port. Use the address on the main Wireless debugging screen.");
            string market = SavedDevice.CleanModel(lines[2]), maker = SavedDevice.CleanModel(lines[3]), model = SavedDevice.CleanModel(lines[4]);
            if (market.Length == 0) market = model.StartsWith(maker, StringComparison.OrdinalIgnoreCase) || maker.Length == 0 ? model
                : SavedDevice.CleanModel(Char.ToUpperInvariant(maker[0]) + maker.Substring(1) + " " + model);
            connectedModel = market;
            connectedDetail = "Android " + AndroidVersion(sdk) + " · " + endpoint.IP;

            var options = new StreamOptions {
                Profile = quality.SelectedIndex, Audio = (AudioMode)audio.SelectedIndex, Codec = codec.SelectedIndex == 0 ? "opus" : "aac",
                Control = control.Checked, Keyboard = keyboard.Checked, ScreenOff = screenOff.Checked, Clipboard = clipboard.Checked, OnTop = onTop.Checked,
                Title = market.Length > 0 ? market : "PhoneScreen"
            };
            if (options.Audio == AudioMode.Both && sdk < 33) {
                options.Audio = AudioMode.Computer;
                AddLog("Playing on both devices needs Android 13. Sound plays on the PC only.");
            }
            engine.IconPath = EnsureIcon();
            sessionOutput.Clear(); streamingSince = null; audioWarned = false;
            int session = Environment.TickCount;
            mirror = engine.StartMirror(serial, options, line => {
                if (IsDisposed || Disposing) return;
                try { BeginInvoke(new Action(() => OnMirrorOutput(line, options, sdk))); } catch (InvalidOperationException) { }
            });
            if (target != null) saved.Service = target.Name;
            saved.Model = market; saved.Address = address;
            TrySaveDevice();
            TrySavePreferences();
            SetStatus("Opening the phone screen", "It appears in its own window in a moment.", Tone.Neutral);
            AddLog("Android " + AndroidVersion(sdk) + " (API " + sdk + "), " + (market.Length > 0 ? market : "unknown model") + ". Starting the stream.");
        }

        void OnMirrorOutput(string line, StreamOptions options, int sdk) {
            if (mirror == null) return;
            if (sessionOutput.Length > 32000) sessionOutput.Remove(0, 16000);
            sessionOutput.AppendLine(line); AddLog(line);
            if (line.IndexOf("Texture:", StringComparison.Ordinal) >= 0) {
                streamingSince = DateTime.UtcNow; reconnects = 0;
                if (!audioWarned) SetStatus("Streaming from " + (connectedModel.Length > 0 ? connectedModel : "your phone"),
                    (options.Audio == AudioMode.Off ? "Sound off" : "Sound on") + " · " + (options.Control ? "mouse and keyboard" : "view only")
                    + " · Alt+F fullscreen · close the window to stop", Tone.Good);
                UpdateControls();
            }
            bool audioLine = line.IndexOf("Demuxer 'audio'", StringComparison.Ordinal) >= 0 && line.IndexOf("starting", StringComparison.Ordinal) < 0 && line.IndexOf("end of frames", StringComparison.Ordinal) < 0
                || line.IndexOf("audio encoder", StringComparison.OrdinalIgnoreCase) >= 0 && line.IndexOf("Could not", StringComparison.Ordinal) >= 0
                || line.IndexOf("Audio capture error", StringComparison.Ordinal) >= 0 || line.IndexOf("Audio encoding error", StringComparison.Ordinal) >= 0
                || line.IndexOf("Audio disabled", StringComparison.Ordinal) >= 0;
            if (!audioLine || audioWarned || options.Audio == AudioMode.Off) return;
            audioWarned = true;
            bool encoder = line.IndexOf("encoder", StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("configuration error", StringComparison.Ordinal) >= 0
                || line.IndexOf("unsupported codec", StringComparison.Ordinal) >= 0;
            if (options.Codec == "opus" && encoder) {
                // Some phones ship without an Opus encoder. AAC works on practically all of them.
                codec.SelectedIndex = 1; TrySavePreferences();
                pendingRestart = "This phone can't encode Opus. Switching to AAC so you get sound.";
                AddLog(pendingRestart);
                return;
            }
            SetStatus("Picture is on, sound isn't", sdk == 30 ? "Android 11 only captures sound when the phone is unlocked at connect time. Unlock it and reconnect."
                : options.Codec == "opus" ? "Try Audio codec → AAC in Stream settings, then reconnect." : "Some apps block sound capture. Details are in the log.", Tone.Warn);
        }

        static string AndroidVersion(int sdk) {
            switch (sdk) {
                case 30: return "11"; case 31: return "12"; case 32: return "12L"; case 33: return "13";
                case 34: return "14"; case 35: return "15"; case 36: return "16";
                default: return sdk > 36 ? (sdk - 20).ToString() : "API " + sdk;
            }
        }

        async Task StopAsync(bool requested) {
            if (mirror != null) {
                var running = mirror; mirror = null;
                try {
                    if (!running.HasExited) {
                        // Let SDL close normally first so scrcpy can restore the phone's settings.
                        bool sent = running.CloseMainWindow();
                        bool exited = sent && await Task.Run(() => running.WaitForExit(1800));
                        if (!exited && !running.HasExited) running.Kill();
                    }
                    await Task.Run(() => running.WaitForExit(2000));
                } catch (InvalidOperationException) { }
                finally { running.Dispose(); }
            }
            streamingSince = null;
            if (serial != null) {
                string address = serial; serial = null;
                try { await engine.AdbAsync("disconnect " + Engine.Quote(address), null, 4000); }
                catch (Exception ex) { AddLog("ADB disconnect: " + Friendly(ex)); }
            }
            if (bridge != null) { bridge.Dispose(); bridge = null; }
            if (requested) SetStatus("Disconnected", "Turn off Wireless debugging on the phone when you're done.", Tone.Neutral);
        }

        async void CloseAsync(object sender, FormClosingEventArgs e) {
            if (mayClose) return;
            e.Cancel = true;
            if (closing) return;
            TrySavePreferences();
            closing = true; lifetime.Cancel(); timer.Stop(); pairCode.Clear(); UpdateControls();
            SetStatus("Closing", "Stopping the stream and the local ADB server.", Tone.Neutral);
            engine.CancelCommands();
            if (activeTask != null) { try { await activeTask; } catch { } }
            if (discovery != null) { try { await discovery; } catch { } }
            try { await StopAsync(false); } catch { }
            // Stop only our own ADB server, never one that Android Studio may be using.
            try { await engine.ShutdownAsync(); } catch { }
            engine.Dispose(); mayClose = true; Close();
        }

        static string Friendly(Exception ex) {
            var socket = ex as System.Net.Sockets.SocketException;
            if (socket != null) return "Can't reach the phone (" + socket.SocketErrorCode + "). Check Wi-Fi, the address and Wireless debugging.";
            if (ex is OperationCanceledException) return "Cancelled or timed out.";
            if (ex is TimeoutException) return ex.Message;
            return ex.Message;
        }
        static string FailureHint(string output) {
            if (output.IndexOf("Could not inject", StringComparison.OrdinalIgnoreCase) >= 0 || output.IndexOf("INJECT_EVENTS", StringComparison.Ordinal) >= 0)
                return "The phone blocked input. On Xiaomi phones enable “USB debugging (Security settings)”.";
            if (output.IndexOf("encoder", StringComparison.OrdinalIgnoreCase) >= 0)
                return "The phone's video encoder refused the settings. Try the Weak Wi-Fi or Responsive profile.";
            if (output.IndexOf("Device disconnected", StringComparison.OrdinalIgnoreCase) >= 0 || output.IndexOf("connection", StringComparison.OrdinalIgnoreCase) >= 0)
                return "The Wi-Fi connection dropped. Check that the phone is awake and on the same network, then reconnect.";
            return "Open the log for details, then try again.";
        }

        // ---------- Files ----------

        string DataPath(string name) {
            string directory = Path.Combine(root, ".data");
            FileSafety.CheckPath(directory);
            if (!Directory.Exists(directory)) { Directory.CreateDirectory(directory); FileSafety.ProtectDirectory(directory); }
            string path = Path.Combine(directory, name); FileSafety.CheckPath(path);
            return path;
        }
        static string ReadSmall(string path) {
            if (!File.Exists(path) || new FileInfo(path).Length > 1024) return null;
            return File.ReadAllText(path);
        }
        void LoadState() {
            try {
                string device = ReadSmall(Path.Combine(root, ".data", "device.txt"));
                if (device != null) saved = SavedDevice.Parse(device);
                if (saved.Address.Length == 0) {
                    string legacy = ReadSmall(Path.Combine(root, ".data", "last-address.txt"));
                    if (legacy != null) saved.Address = PhoneAddress.Parse(legacy).ToString();
                }
            } catch { }
            if (saved.Address.Length > 0) { settingAddress = true; connectAddress.Text = saved.Address; settingAddress = false; }
            try {
                string text = ReadSmall(Path.Combine(root, ".data", "preferences.txt"));
                if (text == null) return;
                var prefs = StreamPreferences.Parse(text);
                quality.SelectedIndex = prefs.Quality; audio.SelectedIndex = (int)prefs.Audio; codec.SelectedIndex = prefs.Codec;
                control.Checked = prefs.Control; keyboard.Checked = prefs.Keyboard; screenOff.Checked = prefs.ScreenOff;
                clipboard.Checked = prefs.Clipboard; onTop.Checked = prefs.OnTop;
            } catch { }
        }
        void TrySavePreferences() {
            if (preview) return;
            try {
                var prefs = new StreamPreferences { Quality = quality.SelectedIndex, Audio = (AudioMode)audio.SelectedIndex, Codec = codec.SelectedIndex,
                    Control = control.Checked, Keyboard = keyboard.Checked, ScreenOff = screenOff.Checked, Clipboard = clipboard.Checked, OnTop = onTop.Checked };
                File.WriteAllText(DataPath("preferences.txt"), prefs.Encode(), Encoding.UTF8);
            } catch (Exception ex) { AddLog("Couldn't save settings: " + ex.Message); }
        }
        void TrySaveDevice() {
            if (preview) return;
            try { File.WriteAllText(DataPath("device.txt"), saved.Encode(), Encoding.UTF8); }
            catch (Exception ex) { AddLog("Couldn't save the phone: " + ex.Message); }
        }
        string EnsureIcon() {
            try {
                string path = DataPath("window-icon.png");
                if (!File.Exists(path)) using (var image = Logo.Render(128)) image.Save(path, ImageFormat.Png);
                return path;
            } catch { return null; }
        }
        void OpenLink(string url) {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { SetStatus("Couldn't open the browser", ex.Message, Tone.Error); }
        }

        // Sample state for README screenshots (--render-preview / --render-settings).
        internal void PrepareDemo(bool settings) {
            saved = new SavedDevice { Service = "adb-R5CW71XXXX-Qp8sZt", Model = "Galaxy S23", Address = "192.168.1.42:37105" };
            discoveryStarted = true;
            services = new List<PhoneService> { new PhoneService { Name = saved.Service, Address = PhoneAddress.Parse(saved.Address) } };
            FillAddress(services[0]);
            ExpandPairing(!settings);
            ExpandSettings(settings);
            UpdateControls();
            SetStatus("Ready", "Galaxy S23 is on your Wi-Fi. Press Connect.", Tone.Good);
            Arrange();
        }

        protected override void Dispose(bool disposing) {
            if (disposing) { timer.Dispose(); engine.Dispose(); lifetime.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
