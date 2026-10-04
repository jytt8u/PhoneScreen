using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PhoneScreen {
    // A phone advertised over mDNS by Android's Wireless debugging.
    public sealed class PhoneService {
        public string Name;
        public bool Pairing;
        public PhoneAddress Address;
        public override string ToString() { return Name + " " + Address; }
    }

    public static class Mdns {
        static readonly Regex SafeName = new Regex(@"^[A-Za-z0-9._\-]{1,96}$");

        // Parses `adb mdns services`: "<instance>\t<service type>\t<ip:port>" per line.
        public static List<PhoneService> Parse(string output) {
            var result = new List<PhoneService>();
            foreach (string raw in (output ?? "").Split('\n')) {
                string line = raw.Trim();
                if (line.Length == 0 || line.Length > 300) continue;
                var parts = line.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3) continue;
                string type = parts[parts.Length - 2];
                bool connect = type.StartsWith("_adb-tls-connect._tcp", StringComparison.Ordinal);
                bool pairing = type.StartsWith("_adb-tls-pairing._tcp", StringComparison.Ordinal);
                if (!connect && !pairing) continue;
                string name = String.Join(" ", parts, 0, parts.Length - 2);
                if (!SafeName.IsMatch(name)) continue;
                PhoneAddress address;
                try { address = PhoneAddress.Parse(parts[parts.Length - 1]); } catch (ArgumentException) { continue; }
                if (result.Exists(s => s.Name == name && s.Pairing == pairing && s.Address.ToString() == address.ToString())) continue;
                result.Add(new PhoneService { Name = name, Pairing = pairing, Address = address });
            }
            return result;
        }

        // "Successfully paired to 192.168.1.24:41235 [guid=adb-XXXX-yyyy]"
        public static string PairedGuid(string output) {
            var match = Regex.Match(output ?? "", @"\[guid=([A-Za-z0-9._\-]{1,96})\]");
            return match.Success ? match.Groups[1].Value : null;
        }
    }

    // One-time QR pairing ticket, in the same format Android Studio uses.
    // The phone scans it, then advertises a pairing service named after the ticket.
    public sealed class PairingTicket {
        const string Alphabet = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        public string Name { get; private set; }
        public string Password { get; private set; }
        public string Payload { get { return "WIFI:T:ADB;S:" + Name + ";P:" + Password + ";;"; } }
        public static PairingTicket Create() {
            return new PairingTicket { Name = "phonescreen-" + Random(8), Password = Random(12) };
        }
        static string Random(int length) {
            var bytes = new byte[length];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            var text = new StringBuilder(length);
            // 256 % 56 bias is irrelevant for a short-lived pairing secret, but avoid it anyway.
            for (int i = 0; i < length; i++) {
                int value = bytes[i];
                while (value >= 224) {
                    var one = new byte[1];
                    using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(one);
                    value = one[0];
                }
                text.Append(Alphabet[value % Alphabet.Length]);
            }
            return text.ToString();
        }
    }
}
