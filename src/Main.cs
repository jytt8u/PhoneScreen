using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
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
            bool render = args.Length == 2 && args[0] == "--render-preview";
            SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try {
                using (var form = new MainForm(AppDomain.CurrentDomain.BaseDirectory)) {
                    if (args.Length == 2 && args[0] == "--render-preview") {
                        // Create child HWNDs without showing a visible window to the user.
                        form.ShowInTaskbar = false;
                        form.Opacity = 0;
                        form.StartPosition = FormStartPosition.Manual;
                        form.Location = new Point(-32000, -32000);
                        form.Show();
                        Application.DoEvents();
                        form.PerformLayout();
                        using (var image = new Bitmap(form.Width, form.Height)) {
                            form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
                            image.Save(Path.GetFullPath(args[1]));
                        }
                        form.Hide();
                        return 0;
                    }
                    bool created;
                    using (var mutex = new Mutex(true, @"Local\PhoneScreenLauncher", out created)) {
                        if (!created) {
                            MessageBox.Show("PhoneScreen уже открыт. Переключись на его окно.", "PhoneScreen");
                            return 0;
                        }
                        Application.Run(form);
                    }
                }
                return 0;
            } catch (Exception ex) {
                if (render) {
                    File.WriteAllText(Path.GetFullPath(args[1]) + ".error.txt", ex.ToString());
                    return 1;
                }
                MessageBox.Show(ex.Message, "PhoneScreen — не удалось запустить", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }

    sealed class Surface : Panel {
        public Surface() { DoubleBuffered = true; }
    }
    sealed class ActionButton : Button {
        public ActionButton() { DoubleBuffered = true; }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            if (Enabled) return;
            e.Graphics.Clear(Color.FromArgb(26, 37, 46));
            using (var pen = new Pen(Color.FromArgb(48, 65, 75)))
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Color.FromArgb(135, 154, 165),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }
    sealed class MainForm : Form {
        static readonly Color Bg = Color.FromArgb(14, 20, 26);
        static readonly Color Plate = Color.FromArgb(22, 31, 39);
        static readonly Color Field = Color.FromArgb(31, 43, 53);
        static readonly Color Ink = Color.FromArgb(240, 246, 248);
        static readonly Color Muted = Color.FromArgb(168, 187, 197);
        static readonly Color Accent = Color.FromArgb(113, 223, 184);
        static readonly Color Warning = Color.FromArgb(255, 178, 150);
        readonly string root;
        readonly Engine engine;
        readonly Button install = new ActionButton(), download = new ActionButton(), pair = new ActionButton(), connect = new ActionButton(), stop = new ActionButton();
        readonly TextBox pairAddress = new TextBox(), pairCode = new TextBox(), connectAddress = new TextBox();
        readonly ComboBox quality = new ComboBox(), audioCodec = new ComboBox();
        readonly CheckBox sound = new CheckBox(), duplicate = new CheckBox(), control = new CheckBox(), keyboard = new CheckBox(), screenOff = new CheckBox();
        readonly Label packageStatus = new Label(), status = new Label(), statusDetail = new Label();
        readonly TextBox log = new TextBox();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        Process mirror;
        TlsBridge bridge;
        string serial;
        bool busy, closing, mayClose;
        Task activeTask;
        readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        readonly StringBuilder sessionOutput = new StringBuilder();
        public MainForm(string root) {
            this.root = root;
            engine = new Engine(root);
            Text = "PhoneScreen · Android на компьютере";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            BackColor = Bg; ForeColor = Ink;
            Font = new Font("Segoe UI", 10);
            ClientSize = new Size(930, Math.Min(750, Screen.PrimaryScreen.WorkingArea.Height - 60));
            MinimumSize = new Size(946, 680);
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            Build();
            LoadEndpoint();
            UpdateControls();
            SetStatus("Готово к настройке", "Android 11+ · Телефон и компьютер в одной сети Wi-Fi", false);
            download.Click += async (s, e) => await RunUi(async () => {
                var progress = new Progress<int>(value => SetStatus("Устанавливаю scrcpy · " + value + "%",
                    "Официальный архив · проверка SHA-256 · автоматическая установка", false));
                await Package.DownloadAndInstallAsync(root, progress, lifetime.Token);
                AddLog("scrcpy установлен. Контрольные суммы совпали.");
                SetStatus("Можно подключать телефон", "Выполни сопряжение по коду, затем введи адрес подключения.", false);
            });
            install.Click += async (s, e) => {
                using (var picker = new OpenFileDialog { Title = "Выбери официальный scrcpy-win64-v4.1.zip", Filter = "scrcpy ZIP|*.zip", CheckFileExists = true }) {
                    if (picker.ShowDialog(this) != DialogResult.OK) return;
                    string path = picker.FileName;
                    await RunUi(async () => {
                        SetStatus("Проверяю архив", "Сверяю SHA-256 перед установкой.", false);
                        await Task.Run(() => Package.Install(path, root));
                        AddLog("Установлен официальный scrcpy " + Package.Version + ". SHA-256 совпал.");
                        SetStatus("scrcpy готов", "Включи беспроводную отладку на телефоне и выполни сопряжение.", false);
                    });
                }
            };
            pair.Click += async (s, e) => await RunUi(PairAsync);
            connect.Click += async (s, e) => await RunUi(ConnectAsync);
            stop.Click += async (s, e) => await RunUi(async () => await StopAsync(true));
            sound.CheckedChanged += (s, e) => UpdateControls();
            control.CheckedChanged += (s, e) => UpdateControls();
            timer.Interval = 400;
            timer.Tick += async (s, e) => {
                if (mirror == null || busy || closing || !mirror.HasExited) return;
                await RunUi(async () => {
                    mirror.WaitForExit();
                    int exit = mirror.ExitCode;
                    await StopAsync(false);
                    if (exit != 0) {
                        SetStatus("Трансляция завершилась с ошибкой", FailureHint(sessionOutput.ToString()), true);
                        AddLog("scrcpy завершился с кодом " + exit + ".");
                    } else SetStatus("Трансляция остановлена", "Соединение с телефоном отключено. Можно подключиться снова.", false);
                });
            };
            timer.Start();
            FormClosing += CloseAsync;
        }
        Font F(float size, bool bold) { return new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular); }
        Label L(Control parent, string text, int x, int y, int width, int height, float size, Color color, bool bold) {
            var label = new Label { Text = text, Location = new Point(x, y), Size = new Size(width, height), ForeColor = color, Font = F(size, bold) };
            parent.Controls.Add(label); return label;
        }
        void Btn(Control parent, Button button, string text, int x, int y, int width, bool primary) {
            button.Text = text; button.SetBounds(x, y, width, 38);
            button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.FlatAppearance.BorderColor = Color.FromArgb(60, 82, 92);
            button.BackColor = primary ? Accent : Field; button.ForeColor = primary ? Bg : Ink;
            button.Font = F(10, true); button.Cursor = Cursors.Hand; parent.Controls.Add(button);
        }
        void Input(Control parent, TextBox box, int x, int y, int width) {
            box.SetBounds(x, y, width, 31); box.Font = F(12, false); box.BackColor = Field; box.ForeColor = Ink;
            box.BorderStyle = BorderStyle.FixedSingle; box.MaxLength = 64; parent.Controls.Add(box);
        }
        void Check(Control parent, CheckBox box, string text, int x, int y, int width, bool selected) {
            box.Text = text; box.SetBounds(x, y, width, 26); box.Checked = selected; box.ForeColor = Ink;
            box.Font = F(10, false); parent.Controls.Add(box);
        }
        void Combo(Control parent, ComboBox box, string[] options, int x, int y, int width) {
            box.SetBounds(x, y, width, 30); box.DropDownStyle = ComboBoxStyle.DropDownList;
            box.BackColor = Field; box.ForeColor = Ink; box.Font = F(10, false);
            box.Items.AddRange(options); box.SelectedIndex = 0; parent.Controls.Add(box);
        }
        void Build() {
            var outer = new Surface { Dock = DockStyle.Fill, AutoScroll = true };
            Controls.Add(outer);
            L(outer, "PHONESCREEN", 30, 22, 250, 26, 11, Accent, true);
            L(outer, "ANDROID  →  WINDOWS", 680, 22, 220, 26, 9, Muted, false).TextAlign = ContentAlignment.MiddleRight;
            L(outer, "Телефон. На большом экране.", 28, 55, 870, 52, 27, Ink, true);
            L(outer, "Экран, касания и звук — через твою сеть Wi-Fi.", 30, 111, 860, 26, 12, Muted, false);

            var setup = new Surface { BackColor = Plate, Location = new Point(30, 153), Size = new Size(870, 74) };
            outer.Controls.Add(setup);
            packageStatus.SetBounds(18, 14, 376, 49); packageStatus.Font = F(10, true); setup.Controls.Add(packageStatus);
            Btn(setup, download, "Установить scrcpy", 416, 18, 221, true);
            Btn(setup, install, "У меня есть ZIP", 653, 18, 195, false);

            var guide = new Surface { BackColor = Plate, Location = new Point(30, 240), Size = new Size(316, 287) };
            outer.Controls.Add(guide);
            L(guide, "НА ТЕЛЕФОНЕ", 18, 17, 270, 25, 10, Accent, true);
            L(guide, "1  Открой настройки разработчика", 18, 49, 280, 40, 11, Ink, true);
            L(guide, "Настройки → О телефоне → нажми 7 раз на «Номер сборки».", 18, 90, 275, 43, 10, Muted, false);
            L(guide, "2  Включи беспроводную отладку", 18, 140, 280, 40, 11, Ink, true);
            L(guide, "Открой «Сопряжение с помощью кода». Перенеси адрес и код в поля справа.", 18, 181, 277, 50, 10, Muted, false);
            L(guide, "3  Вернись на основной экран отладки\nЕго IP:порт нужен для подключения.", 18, 237, 280, 47, 10, Ink, false);

            var fields = new Surface { Location = new Point(370, 238), Size = new Size(530, 297) };
            outer.Controls.Add(fields);
            L(fields, "01  СОПРЯЖЕНИЕ · ОДИН РАЗ", 0, 0, 510, 24, 10, Accent, true);
            L(fields, "Адрес из окна с кодом", 0, 29, 310, 23, 10, Muted, false);
            L(fields, "6 цифр", 333, 29, 165, 23, 10, Muted, false);
            Input(fields, pairAddress, 0, 54, 315);
            Input(fields, pairCode, 333, 54, 190); pairCode.UseSystemPasswordChar = true; pairCode.MaxLength = 6;
            Btn(fields, pair, "Сопрячь телефон", 0, 93, 190, false);
            L(fields, "Код остаётся только в памяти.\nПосле ввода поле очищается.", 208, 93, 315, 40, 9, Muted, false);
            L(fields, "02  ПОДКЛЮЧЕНИЕ", 0, 141, 520, 24, 10, Accent, true);
            L(fields, "IP:порт с основного экрана отладки", 0, 167, 520, 24, 10, Muted, false);
            Input(fields, connectAddress, 0, 193, 523);
            L(fields, "Пример: 192.168.3.42:37001. Порты в двух окнах разные.", 0, 229, 523, 24, 9, Muted, false);
            Btn(fields, connect, "Показать экран", 0, 255, 320, true);
            Btn(fields, stop, "Отключить", 337, 255, 186, false);

            var options = new Surface { BackColor = Plate, Location = new Point(30, 541), Size = new Size(870, 104) };
            outer.Controls.Add(options);
            L(options, "Качество", 17, 11, 100, 23, 9, Muted, false);
            L(options, "Кодек звука", 308, 11, 115, 23, 9, Muted, false);
            Combo(options, quality, new[] { "Баланс · 1600 px / до 60 fps", "Слабый Wi-Fi · 1080 px / до 30 fps", "Видео · 1920 px / до 60 fps" }, 17, 34, 274);
            Combo(options, audioCodec, new[] { "Opus · основной", "AAC · совместимый" }, 309, 34, 223);
            Check(options, sound, "Звук на ПК", 555, 30, 147, true);
            Check(options, duplicate, "И на телефоне¹", 705, 30, 155, false);
            Check(options, control, "Управление мышью", 17, 73, 225, true);
            Check(options, keyboard, "Клавиатура²", 245, 73, 180, true);
            Check(options, screenOff, "Гасить экран телефона", 435, 73, 251, false);
            L(options, "¹ Android 13+  ² UHID", 687, 76, 180, 24, 8, Muted, false);

            status.SetBounds(30, 660, 870, 28); status.Font = F(12, true); outer.Controls.Add(status);
            statusDetail.SetBounds(30, 692, 870, 44); statusDetail.Font = F(10, false); statusDetail.ForeColor = Muted; outer.Controls.Add(statusDetail);
            L(outer, "Подсказки: клик — касание · перетаскивание — свайп · правый клик — назад · Alt+F — весь экран.", 30, 808, 870, 27, 9, Muted, false);
            L(outer, "После сеанса выключи беспроводную отладку на телефоне. Сопряжение даёт этому ПК доступ через ADB.", 30, 841, 870, 28, 9, Muted, false);
            L(outer, "ЖУРНАЛ ПОДКЛЮЧЕНИЯ", 30, 888, 550, 23, 9, Muted, true);
            log.SetBounds(30, 921, 870, 128); log.Multiline = true; log.ReadOnly = true; log.ScrollBars = ScrollBars.Vertical;
            log.BackColor = Plate; log.ForeColor = Muted; log.BorderStyle = BorderStyle.FixedSingle;
            log.Font = new Font("Consolas", 9); outer.Controls.Add(log);
            L(outer, "PhoneScreen 1.0 · scrcpy 4.1 от Genymobile · локальное соединение без аккаунта и облака", 30, 1063, 870, 30, 8, Muted, false);
            // Main controls remain above the fold at small laptop resolutions; details scroll.
            outer.AutoScrollMinSize = new Size(0, 1097);
        }
        void UpdateControls() {
            bool ready = Package.Ready(root);
            packageStatus.Text = ready ? "Движок установлен\nМожно подключать Android" : "Движок scrcpy 4.1\nУстановка одной кнопкой · Windows x64";
            packageStatus.ForeColor = ready ? Accent : Ink;
            bool idle = !busy && !closing && mirror == null;
            install.Enabled = !ready && idle; download.Enabled = !ready && idle;
            pair.Enabled = ready && idle; connect.Enabled = ready && idle;
            stop.Enabled = mirror != null && !busy && !closing;
            foreach (var item in new Control[] { pairAddress, pairCode, connectAddress, quality, sound, control }) item.Enabled = idle;
            duplicate.Enabled = idle && sound.Checked; audioCodec.Enabled = idle && sound.Checked;
            keyboard.Enabled = idle && control.Checked; screenOff.Enabled = idle && control.Checked;
        }
        void SetStatus(string title, string detail, bool error) {
            status.Text = title; status.ForeColor = error ? Warning : Accent; statusDetail.Text = detail;
        }
        void AddLog(string line) {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired) { try { BeginInvoke(new Action<string>(AddLog), line); } catch (InvalidOperationException) { } return; }
            if (log.TextLength > 20000) log.Text = log.Text.Substring(log.TextLength - 10000);
            log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + line + Environment.NewLine);
        }
        async Task RunUi(Func<Task> action) {
            if (busy || closing) return;
            busy = true; UpdateControls();
            var finished = new TaskCompletionSource<bool>(); activeTask = finished.Task;
            try {
                Exception failure = null;
                try { await action(); } catch (Exception ex) { failure = ex; }
                if (failure != null) {
                    SetStatus("Не удалось завершить действие", Friendly(failure), true);
                    AddLog(Friendly(failure));
                    // No half-open mirror or device connection after a failed start.
                    if (mirror == null && serial != null) {
                        try { await engine.AdbAsync("disconnect " + Engine.Quote(serial), null, 4000); } catch { }
                        serial = null;
                    }
                    if (mirror == null && bridge != null) { bridge.Dispose(); bridge = null; }
                    if (mirror == null) await engine.ShutdownAsync();
                }
            } finally { busy = false; activeTask = null; finished.TrySetResult(true); if (!IsDisposed) UpdateControls(); }
        }
        async Task PairAsync() {
            var endpoint = PhoneAddress.Parse(pairAddress.Text);
            string code = pairCode.Text.Trim(); pairCode.Clear();
            if (!Regex.IsMatch(code, @"^[0-9]{6}$")) throw new ArgumentException("Введи шестизначный код с телефона.");
            SetStatus("Сопрягаю телефон", "Оставь окно с кодом открытым на телефоне.", false);
            var result = await engine.AdbAsync("pair " + Engine.Quote(endpoint.ToString()), code, 20000);
            code = null;
            if (result.ExitCode != 0 || result.Output.IndexOf("Successfully paired", StringComparison.OrdinalIgnoreCase) < 0)
                throw new IOException("Сопряжение не прошло. Открой новый код и проверь именно адрес из окна с кодом. " + RedactCode(result.Output));
            // ADB output is deliberately not logged: it may contain the stdin prompt/code.
            SetStatus("Телефон сопряжён", "Вернись на основной экран «Беспроводная отладка» и введи его IP:порт подключения.", false);
            AddLog("Сопряжение подтверждено. Код не сохранён.");
            connectAddress.Focus();
        }
        static string RedactCode(string text) { return Regex.Replace(text ?? "", @"(?<!\d)\d{6}(?!\d)", "[код скрыт]"); }
        async Task ConnectAsync() {
            var endpoint = PhoneAddress.Parse(connectAddress.Text);
            string address = endpoint.ToString();
            SetStatus("Проверяю защищённый порт", "Телефон должен быть разблокирован; порт бери с основного экрана отладки.", false);
            await engine.InitializeAsync();
            bridge = new TlsBridge(endpoint);
            serial = bridge.Serial;
            var connection = await engine.AdbAsync("connect " + Engine.Quote(serial), null, 18000);
            if (connection.ExitCode != 0 || (connection.Output.IndexOf("connected to", StringComparison.OrdinalIgnoreCase) < 0))
                throw new IOException(bridge.Error ?? ("Телефон не подключился. Проверь сопряжение и порт. " + connection.Output));
            AddLog("Транспорт подтвердил STLS. ADB завершил TLS-аутентификацию.");
            var versionTask = engine.AdbAsync("-s " + Engine.Quote(serial) + " shell getprop ro.build.version.sdk", null, 6000);
            var portTask = engine.AdbAsync("-s " + Engine.Quote(serial) + " shell getprop service.adb.tls.port", null, 6000);
            await Task.WhenAll(versionTask, portTask);
            var version = await versionTask;
            int sdk;
            if (version.ExitCode != 0 || !Int32.TryParse(version.Output.Trim(), out sdk) || sdk < 30)
                throw new IOException("Для защищённого Wi-Fi и звука нужен Android 11 или новее.");
            var tlsPort = await portTask;
            int reportedPort;
            if (tlsPort.ExitCode != 0 || !Int32.TryParse(tlsPort.Output.Trim(), out reportedPort) || reportedPort != endpoint.Port)
                throw new IOException("Телефон не подтвердил выбранный TLS-порт. Проверь основной экран беспроводной отладки.");
            if (sound.Checked && duplicate.Checked && sdk < 33)
                throw new IOException("Звук одновременно на ПК и телефоне требует Android 13+. Сними галочку «И на телефоне».");
            sessionOutput.Clear();
            mirror = engine.StartMirror(serial, quality.SelectedIndex, sound.Checked, duplicate.Checked, control.Checked,
                keyboard.Checked, screenOff.Checked, audioCodec.SelectedIndex == 0 ? "opus" : "aac", line => {
                    if (IsDisposed || Disposing) return;
                    try { BeginInvoke(new Action(() => {
                        if (sessionOutput.Length > 16000) sessionOutput.Remove(0, 8000);
                        sessionOutput.AppendLine(line); AddLog(line);
                        if (line.IndexOf("Texture:", StringComparison.OrdinalIgnoreCase) >= 0)
                            SetStatus("Экран телефона открыт", "TLS · " + (control.Checked ? "управление включено" : "только просмотр")
                                + " · " + (sound.Checked ? "звук на ПК" : "без звука"), false);
                    })); } catch (InvalidOperationException) { }
                });
            SaveEndpoint(address);
            SetStatus("Запускаю экран телефона", "Откроется отдельное окно. Если звук недоступен, программа сообщит об ошибке.", false);
            AddLog("Android API " + sdk + ". TLS-порт подтверждён. Запуск трансляции.");
        }
        async Task StopAsync(bool requested) {
            if (mirror != null) {
                var running = mirror; mirror = null;
                try {
                    if (!running.HasExited) {
                        // Ask SDL to close normally first, allowing scrcpy device cleanup.
                        bool sent = running.CloseMainWindow();
                        bool exited = sent && await Task.Run(() => running.WaitForExit(1800));
                        if (!exited && !running.HasExited) running.Kill();
                    }
                    await Task.Run(() => running.WaitForExit(2000));
                } catch (InvalidOperationException) { }
                finally { running.Dispose(); }
            }
            if (serial != null) {
                string address = serial; serial = null;
                try { await engine.AdbAsync("disconnect " + Engine.Quote(address), null, 4000); }
                catch (Exception ex) { AddLog("Отключение ADB: " + Friendly(ex)); }
            }
            if (bridge != null) { bridge.Dispose(); bridge = null; }
            // Kill only the server belonging to this app, not Android Studio's server.
            await engine.ShutdownAsync();
            if (requested) SetStatus("Отключено", "Экран и звук остановлены. Выключи беспроводную отладку на телефоне после сеанса.", false);
        }
        async void CloseAsync(object sender, FormClosingEventArgs e) {
            if (mayClose) return;
            e.Cancel = true;
            if (closing) return;
            closing = true; lifetime.Cancel(); timer.Stop(); pairCode.Clear(); UpdateControls();
            SetStatus("Завершаю соединение", "Закрываю трансляцию и локальный ADB-сервер.", false);
            engine.CancelCommands();
            if (activeTask != null) { try { await activeTask; } catch { } }
            try { await StopAsync(false); } catch { }
            engine.Dispose(); mayClose = true; Close();
        }
        static string Friendly(Exception ex) {
            var socket = ex as System.Net.Sockets.SocketException;
            if (socket != null) return "Нет связи с телефоном. Проверь Wi-Fi, IP:порт и включённую беспроводную отладку. " + socket.SocketErrorCode;
            if (ex is OperationCanceledException) return "Действие отменено или истекло время ожидания.";
            return ex.Message;
        }
        static string FailureHint(string output) {
            if (output.IndexOf("audio", StringComparison.OrdinalIgnoreCase) >= 0 &&
                (output.IndexOf("ERROR", StringComparison.OrdinalIgnoreCase) >= 0 || output.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0))
                return "Разблокируй телефон и повтори запуск. Если не помогло — выбери AAC. Подробности в журнале ниже.";
            return "Проверь соединение. На Xiaomi для управления может понадобиться «Отладка по USB (Настройки безопасности)». Подробности в журнале ниже.";
        }
        void LoadEndpoint() {
            try {
                string path = Path.Combine(root, ".data", "last-address.txt");
                FileSafety.CheckPath(path);
                if (File.Exists(path)) connectAddress.Text = PhoneAddress.Parse(File.ReadAllText(path)).ToString();
            } catch { }
        }
        void SaveEndpoint(string endpoint) {
            // Only the last address is stored here. ADB manages its own pairing identity.
            string path = Path.Combine(root, ".data", "last-address.txt");
            FileSafety.CheckPath(path);
            File.WriteAllText(path, endpoint, Encoding.UTF8);
        }
        void OpenLink(string url) {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { SetStatus("Не удалось открыть браузер", ex.Message, true); }
        }
        protected override void Dispose(bool disposing) {
            if (disposing) { timer.Dispose(); engine.Dispose(); lifetime.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
