using System;
using System.Collections.Generic;
using System.Text;

namespace PhoneScreen {
    // Minimal QR encoder: byte mode, error correction level M, versions 1-6.
    // That covers the ADB pairing payload with plenty of room to spare.
    public static class QrCode {
        static readonly int[] DataCodewords = { 0, 16, 28, 44, 64, 86, 108 };
        static readonly int[] EccPerBlock = { 0, 10, 16, 26, 18, 24, 16 };
        static readonly int[] BlockCount = { 0, 1, 1, 1, 2, 2, 4 };

        public static bool[,] Encode(string text) {
            byte[] data = Encoding.UTF8.GetBytes(text ?? "");
            int version = 1;
            while (version <= 6 && 12 + data.Length * 8 > DataCodewords[version] * 8) version++;
            if (version > 6) throw new ArgumentException("The QR payload is too long.");

            var bits = new List<bool>();
            Append(bits, 0x4, 4);
            Append(bits, data.Length, 8);
            foreach (byte b in data) Append(bits, b, 8);
            int capacity = DataCodewords[version] * 8;
            Append(bits, 0, Math.Min(4, capacity - bits.Count));
            while (bits.Count % 8 != 0) bits.Add(false);
            var codewords = new List<byte>();
            for (int i = 0; i < bits.Count; i += 8) {
                int value = 0;
                for (int j = 0; j < 8; j++) value = (value << 1) | (bits[i + j] ? 1 : 0);
                codewords.Add((byte)value);
            }
            for (int pad = 0; codewords.Count < DataCodewords[version]; pad++) codewords.Add(pad % 2 == 0 ? (byte)0xEC : (byte)0x11);

            int blocks = BlockCount[version], length = DataCodewords[version] / blocks, ecc = EccPerBlock[version];
            var divisor = Divisor(ecc);
            var dataBlocks = new byte[blocks][];
            var eccBlocks = new byte[blocks][];
            for (int b = 0; b < blocks; b++) {
                dataBlocks[b] = codewords.GetRange(b * length, length).ToArray();
                eccBlocks[b] = Remainder(dataBlocks[b], divisor);
            }
            var stream = new List<byte>();
            for (int i = 0; i < length; i++) for (int b = 0; b < blocks; b++) stream.Add(dataBlocks[b][i]);
            for (int i = 0; i < ecc; i++) for (int b = 0; b < blocks; b++) stream.Add(eccBlocks[b][i]);

            int size = 17 + version * 4;
            var best = (bool[,])null;
            int bestPenalty = Int32.MaxValue;
            for (int mask = 0; mask < 8; mask++) {
                var grid = new Grid(size);
                grid.DrawFunctionPatterns(version);
                grid.PlaceData(stream);
                grid.ApplyMask(mask);
                grid.DrawFormat(mask);
                int penalty = grid.Penalty();
                if (penalty < bestPenalty) { bestPenalty = penalty; best = grid.Dark; }
            }
            return best;
        }

        static void Append(List<bool> bits, int value, int count) {
            for (int i = count - 1; i >= 0; i--) bits.Add(((value >> i) & 1) != 0);
        }
        static int Multiply(int x, int y) {
            int z = 0;
            for (int i = 7; i >= 0; i--) {
                z = (z << 1) ^ ((z >> 7) * 0x11D);
                z ^= ((y >> i) & 1) * x;
            }
            return z;
        }
        static byte[] Divisor(int degree) {
            var result = new byte[degree];
            result[degree - 1] = 1;
            int root = 1;
            for (int i = 0; i < degree; i++) {
                for (int j = 0; j < degree; j++) {
                    result[j] = (byte)Multiply(result[j], root);
                    if (j + 1 < degree) result[j] ^= result[j + 1];
                }
                root = Multiply(root, 0x02);
            }
            return result;
        }
        static byte[] Remainder(byte[] data, byte[] divisor) {
            var result = new byte[divisor.Length];
            foreach (byte b in data) {
                int factor = b ^ result[0];
                Array.Copy(result, 1, result, 0, result.Length - 1);
                result[result.Length - 1] = 0;
                for (int i = 0; i < result.Length; i++) result[i] ^= (byte)Multiply(divisor[i], factor);
            }
            return result;
        }

        sealed class Grid {
            public readonly bool[,] Dark;
            readonly bool[,] reserved;
            readonly int size;
            public Grid(int size) { this.size = size; Dark = new bool[size, size]; reserved = new bool[size, size]; }

            void Set(int x, int y, bool dark) { Dark[y, x] = dark; reserved[y, x] = true; }

            public void DrawFunctionPatterns(int version) {
                for (int i = 0; i < size; i++) { Set(6, i, i % 2 == 0); Set(i, 6, i % 2 == 0); }
                Finder(3, 3); Finder(size - 4, 3); Finder(3, size - 4);
                if (version > 1) Alignment(size - 7, size - 7);
                // Reserve the format areas; DrawFormat fills them in later.
                for (int i = 0; i < 9; i++) { reserved[8, i] = true; reserved[i, 8] = true; }
                for (int i = 0; i < 8; i++) { reserved[8, size - 1 - i] = true; reserved[size - 1 - i, 8] = true; }
                Set(8, size - 8, true);
            }
            void Finder(int cx, int cy) {
                for (int dy = -4; dy <= 4; dy++)
                    for (int dx = -4; dx <= 4; dx++) {
                        int x = cx + dx, y = cy + dy;
                        if (x < 0 || y < 0 || x >= size || y >= size) continue;
                        int distance = Math.Max(Math.Abs(dx), Math.Abs(dy));
                        Set(x, y, distance != 2 && distance != 4);
                    }
            }
            void Alignment(int cx, int cy) {
                for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                        Set(cx + dx, cy + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
            }
            public void PlaceData(List<byte> data) {
                int index = 0, total = data.Count * 8;
                for (int right = size - 1; right >= 1; right -= 2) {
                    if (right == 6) right = 5;
                    for (int step = 0; step < size; step++) {
                        for (int j = 0; j < 2; j++) {
                            int x = right - j;
                            bool upward = ((right + 1) & 2) == 0;
                            int y = upward ? size - 1 - step : step;
                            if (reserved[y, x]) continue;
                            if (index < total) Dark[y, x] = ((data[index >> 3] >> (7 - (index & 7))) & 1) != 0;
                            index++;
                        }
                    }
                }
            }
            public void ApplyMask(int mask) {
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++) {
                        if (reserved[y, x]) continue;
                        bool invert;
                        switch (mask) {
                            case 0: invert = (x + y) % 2 == 0; break;
                            case 1: invert = y % 2 == 0; break;
                            case 2: invert = x % 3 == 0; break;
                            case 3: invert = (x + y) % 3 == 0; break;
                            case 4: invert = (x / 3 + y / 2) % 2 == 0; break;
                            case 5: invert = x * y % 2 + x * y % 3 == 0; break;
                            case 6: invert = (x * y % 2 + x * y % 3) % 2 == 0; break;
                            default: invert = ((x + y) % 2 + x * y % 3) % 2 == 0; break;
                        }
                        if (invert) Dark[y, x] = !Dark[y, x];
                    }
            }
            public void DrawFormat(int mask) {
                // Error correction level M is encoded as 00.
                int data = mask, remainder = data;
                for (int i = 0; i < 10; i++) remainder = (remainder << 1) ^ ((remainder >> 9) * 0x537);
                int bits = ((data << 10) | remainder) ^ 0x5412;
                Func<int, bool> bit = i => ((bits >> i) & 1) != 0;
                for (int i = 0; i <= 5; i++) Set(8, i, bit(i));
                Set(8, 7, bit(6)); Set(8, 8, bit(7)); Set(7, 8, bit(8));
                for (int i = 9; i < 15; i++) Set(14 - i, 8, bit(i));
                for (int i = 0; i < 8; i++) Set(size - 1 - i, 8, bit(i));
                for (int i = 8; i < 15; i++) Set(8, size - 15 + i, bit(i));
                Set(8, size - 8, true);
            }
            public int Penalty() {
                int penalty = 0, dark = 0;
                for (int pass = 0; pass < 2; pass++)
                    for (int a = 0; a < size; a++) {
                        int run = 0; bool previous = false; int history = 0;
                        for (int b = 0; b < size; b++) {
                            bool value = pass == 0 ? Dark[a, b] : Dark[b, a];
                            if (b > 0 && value == previous) run++;
                            else { if (run >= 5) penalty += run - 2; run = 1; previous = value; }
                            history = ((history << 1) | (value ? 1 : 0)) & 0x7FF;
                            if (b >= 10 && (history == 0x5D0 || history == 0x05D)) penalty += 40;
                        }
                        if (run >= 5) penalty += run - 2;
                    }
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++) {
                        if (Dark[y, x]) dark++;
                        if (x + 1 < size && y + 1 < size && Dark[y, x] == Dark[y, x + 1] && Dark[y, x] == Dark[y + 1, x] && Dark[y, x] == Dark[y + 1, x + 1]) penalty += 3;
                    }
                int total = size * size;
                penalty += (Math.Abs(dark * 20 - total * 10) + total - 1) / total * 10 - 10;
                return penalty;
            }
        }
    }
}
