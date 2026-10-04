using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PhoneScreen {
    public enum AudioMode { Off = 0, Computer = 1, Both = 2 }

    public sealed class StreamPreferences {
        public const int ProfileCount = 4;
        public int Quality, Codec;
        public AudioMode Audio = AudioMode.Computer;
        public bool Control = true, Keyboard = true, ScreenOff, Clipboard, OnTop;

        public string Encode() {
            return String.Join("|", new[] { "2", N(Quality), N((int)Audio), N(Codec), B(Control), B(Keyboard), B(ScreenOff), B(Clipboard), B(OnTop) });
        }
        static string N(int value) { return value.ToString(CultureInfo.InvariantCulture); }
        static string B(bool value) { return value ? "1" : "0"; }

        public static StreamPreferences Parse(string text) {
            var fields = (text ?? "").Trim().Split('|');
            if (fields.Length == 8 && fields[0] == "1") return ParseVersion1(fields);
            if (fields.Length != 9 || fields[0] != "2") return new StreamPreferences();
            int[] limits = { ProfileCount - 1, 2, 1, 1, 1, 1, 1, 1 };
            var values = new int[limits.Length];
            for (int i = 0; i < values.Length; i++)
                if (!Int32.TryParse(fields[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out values[i]) || values[i] > limits[i]) return new StreamPreferences();
            return new StreamPreferences { Quality = values[0], Audio = (AudioMode)values[1], Codec = values[2], Control = values[3] == 1,
                Keyboard = values[4] == 1, ScreenOff = values[5] == 1, Clipboard = values[6] == 1, OnTop = values[7] == 1 };
        }

        // 1.0 stored: version|quality|sound|duplicate|control|keyboard|screenOff|codec
        static StreamPreferences ParseVersion1(string[] fields) {
            var values = new int[7];
            for (int i = 0; i < values.Length; i++)
                if (!Int32.TryParse(fields[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out values[i]) || values[i] > (i == 0 ? 2 : 1)) return new StreamPreferences();
            // Old profiles: 0 balanced, 1 low bandwidth, 2 video. New order: balanced, responsive, weak Wi-Fi, movies.
            int[] profile = { 0, 2, 3 };
            return new StreamPreferences { Quality = profile[values[0]], Audio = values[1] == 0 ? AudioMode.Off : values[2] == 1 ? AudioMode.Both : AudioMode.Computer,
                Control = values[3] == 1, Keyboard = values[4] == 1, ScreenOff = values[5] == 1, Codec = values[6] };
        }
    }

    // The phone this PC connected to last. Used to pick the right phone when several are discoverable.
    public sealed class SavedDevice {
        static readonly Regex Guid = new Regex(@"^[A-Za-z0-9._\-]{1,96}$");
        public string Service = "", Model = "", Address = "";

        public string Encode() { return String.Join("\n", new[] { "1", Service, Model, Address }); }

        public static SavedDevice Parse(string text) {
            var device = new SavedDevice();
            var lines = (text ?? "").Replace("\r", "").Split('\n');
            if (lines.Length < 4 || lines[0] != "1") return device;
            if (Guid.IsMatch(lines[1])) device.Service = lines[1];
            device.Model = CleanModel(lines[2]);
            try { device.Address = PhoneAddress.Parse(lines[3]).ToString(); } catch (ArgumentException) { }
            return device;
        }

        // Device properties are attacker-influenced text; keep only what is safe to display and pass to scrcpy.
        public static string CleanModel(string text) {
            var result = new StringBuilder();
            foreach (char c in (text ?? "").Trim()) {
                if (Char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_' || c == '.' || c == '(' || c == ')' || c == '+') result.Append(c);
                if (result.Length == 40) break;
            }
            return Regex.Replace(result.ToString(), @"\s+", " ").Trim();
        }
    }
}
