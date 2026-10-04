using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace PhoneScreen {
    static class UiChecks {
        static int count;
        static void Check(bool value, string name) {
            if (!value) throw new Exception("FAIL: " + name);
            count++; Console.WriteLine("PASS: " + name);
        }
        static T Field<T>(MainForm form, string name) {
            return (T)typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        }
        static void Call(MainForm form, string name, params object[] args) {
            typeof(MainForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, args);
            Application.DoEvents();
        }
        // Every visible child stays inside its card, and no two visible children overlap.
        static string LayoutProblem(MainForm form) {
            foreach (var cardName in new[] { "deviceCard", "connectCard", "pairCard", "settingsCard", "logCard" }) {
                var card = Field<Control>(form, cardName);
                if (!card.Visible) continue;
                var children = card.Controls.Cast<Control>().Where(c => c.Visible).ToList();
                foreach (var child in children)
                    if (child.Left < 0 || child.Top < 0 || child.Right > card.ClientSize.Width || child.Bottom > card.ClientSize.Height)
                        return cardName + "." + (child.AccessibleName ?? child.Text) + " leaves its card";
                for (int i = 0; i < children.Count; i++)
                    for (int j = i + 1; j < children.Count; j++)
                        if (children[i].Bounds.IntersectsWith(children[j].Bounds))
                            return cardName + ": '" + children[i].Text + "' overlaps '" + children[j].Text + "'";
            }
            var cards = new[] { "deviceCard", "connectCard", "pairCard", "settingsCard", "logCard" }.Select(n => Field<Control>(form, n)).Where(c => c.Visible).ToList();
            for (int i = 0; i < cards.Count; i++)
                for (int j = i + 1; j < cards.Count; j++)
                    if (cards[i].Bounds.IntersectsWith(cards[j].Bounds)) return "cards overlap";
            var content = Field<Control>(form, "content");
            foreach (var card in cards) if (card.Right > content.ClientSize.Width) return "a card is wider than the window";
            return null;
        }
        static void Layouts(MainForm form, string label) {
            foreach (var size in new[] { new Size(1040, 720), new Size(864, 561) }) {
                form.ClientSize = new Size(Theme.S(size.Width), Theme.S(size.Height)); Application.DoEvents();
                string tag = label + " " + size.Width + "x" + size.Height;
                foreach (var pairing in new[] { true, false })
                    foreach (var settings in new[] { false, true }) {
                        Call(form, "ExpandPairing", pairing);
                        Call(form, "ExpandSettings", settings);
                        string problem = LayoutProblem(form);
                        Check(problem == null, tag + (pairing ? " pairing" : "") + (settings ? " settings" : "") + " layout is clean" + (problem == null ? "" : ": " + problem));
                    }
            }
        }
        [STAThread] static int Main() {
            string scratch = Path.Combine(Path.GetTempPath(), "phonescreen-ui-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch);
            try {
                Application.EnableVisualStyles();
                using (var form = new MainForm(scratch, true)) {
                    form.ShowInTaskbar = false; form.Opacity = 0; form.Show(); Application.DoEvents();
                    var code = Field<TextBox>(form, "pairCode");
                    Check(!Field<Button>(form, "connect").Enabled, "connect waits for the engine");
                    Check(Field<Button>(form, "download").Visible, "engine install is offered");
                    Check(code.UseSystemPasswordChar && code.MaxLength == 6, "pairing code is masked");
                    Check(Field<bool>(form, "pairingExpanded"), "first run opens pairing");
                    Check(Field<QrView>(form, "qr").Modules != null, "pairing shows a QR code");
                    Layouts(form, "100%");
                    Call(form, "ExpandPairing", false);
                    Check(!Field<QrView>(form, "qr").Visible, "pairing folds away");
                    Call(form, "PrepareDemo", true);
                    Check(Field<TextBox>(form, "connectAddress").Text == "192.168.1.42:37105", "a discovered phone fills in the address");
                    Check(Field<Label>(form, "deviceName").Text == "Galaxy S23", "the known phone is shown by name");
                    Check(Field<bool>(form, "settingsExpanded") && Field<Control>(form, "quality").Visible, "settings expand on demand");
                    var ticket = Field<PairingTicket>(form, "ticket");
                    Call(form, "NewTicket");
                    Check(Field<PairingTicket>(form, "ticket").Password != ticket.Password, "a fresh QR code replaces the used one");
                    form.Hide();
                }
                float scale = Theme.Scale;
                try {
                    Theme.Scale = 1.5f;
                    using (var form = new MainForm(scratch, true)) {
                        Theme.Scale = 1.5f;
                        form.ShowInTaskbar = false; form.Opacity = 0; form.Show(); Application.DoEvents();
                        Layouts(form, "150%");
                        form.Hide();
                    }
                } finally { Theme.Scale = scale; }
                Console.WriteLine("All " + count + " UI checks passed."); return 0;
            } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            finally { FileSafety.DeleteChild(Path.GetTempPath(), scratch); }
        }
    }
}
