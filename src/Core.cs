using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PhoneScreen {
    public sealed class PhoneAddress {
        public IPAddress IP;
        public int Port;
        public override string ToString() { return IP.ToString() + ":" + Port; }
        public static PhoneAddress Parse(string value) {
            // Only numeric RFC1918 addresses. No DNS, shell syntax or public endpoints.
            var match = Regex.Match((value ?? "").Trim(), @"^(\d{1,3}(?:\.\d{1,3}){3}):(\d{1,5})$");
            IPAddress ip;
            int port;
            if (!match.Success || !IPAddress.TryParse(match.Groups[1].Value, out ip)
                || ip.AddressFamily != AddressFamily.InterNetwork
                || !Int32.TryParse(match.Groups[2].Value, out port) || port < 1024 || port > 65535) {
                throw new ArgumentException("Введи адрес с телефона в формате IP:порт, например 192.168.3.42:37001.");
            }
            var b = ip.GetAddressBytes();
            bool local = b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168);
            if (!local) throw new ArgumentException("Нужен локальный адрес телефона из домашней сети Wi-Fi.");
            // 5555 is conventionally the legacy, unencrypted ADB port.
            if (port == 5555) throw new ArgumentException("Порт 5555 не используется. Возьми адрес из «Беспроводной отладки» Android 11+.");
            return new PhoneAddress { IP = ip, Port = port };
        }
    }

    public static class TlsProbe {
        public const uint STLS = 0x534c5453;
        public static byte[] ConnectPacket() {
            var payload = Encoding.ASCII.GetBytes("host::\0");
            uint sum = 0;
            foreach (byte b in payload) sum += b;
            using (var memory = new MemoryStream()) {
                using (var writer = new BinaryWriter(memory, Encoding.ASCII, true)) {
                    writer.Write((uint)0x4e584e43); // CNXN
                    writer.Write((uint)0x01000001);
                    writer.Write((uint)4096);
                    writer.Write((uint)payload.Length);
                    writer.Write(sum);
                    writer.Write((uint)(0x4e584e43 ^ 0xffffffff));
                    writer.Write(payload);
                }
                return memory.ToArray();
            }
        }
        public static bool IsTlsHeader(byte[] header) {
            if (header == null || header.Length != 24) return false;
            uint command = BitConverter.ToUInt32(header, 0);
            return command == STLS && BitConverter.ToUInt32(header, 20) == (command ^ 0xffffffff)
                && BitConverter.ToUInt32(header, 12) == 0 && BitConverter.ToUInt32(header, 16) == 0
                && BitConverter.ToUInt32(header, 4) >= 0x01000000 && BitConverter.ToUInt32(header, 8) == 0;
        }
        // A TLS-enabled adbd replies to CNXN with STLS, before authentication.
        // This sends only a generic protocol greeting; never media, keys or commands.
        public static async Task CheckAsync(IPAddress ip, int port, int timeoutMs) {
            using (var client = new TcpClient(AddressFamily.InterNetwork)) {
                var check = CheckInnerAsync(client, ip, port);
                if (await Task.WhenAny(check, Task.Delay(timeoutMs)) != check) {
                    client.Close();
                    try { await check; } catch { }
                    throw new TimeoutException("Телефон не ответил. Проверь Wi-Fi, адрес и включённую беспроводную отладку.");
                }
                await check;
            }
        }
        private static async Task CheckInnerAsync(TcpClient client, IPAddress ip, int port) {
            await client.ConnectAsync(ip, port);
            var stream = client.GetStream();
            var packet = ConnectPacket();
            await stream.WriteAsync(packet, 0, packet.Length);
            var response = new byte[24];
            int offset = 0;
            while (offset < response.Length) {
                int count = await stream.ReadAsync(response, offset, response.Length - offset);
                if (count == 0) throw new IOException("Соединение закрыто. Используй порт подключения, а не порт сопряжения.");
                offset += count;
            }
            if (!IsTlsHeader(response)) throw new IOException("Этот порт не подтвердил TLS. Подключение остановлено. Возьми адрес с основного экрана «Беспроводная отладка».");
        }
    }

    public sealed class TlsBridge : IDisposable {
        readonly TcpListener listener;
        TcpClient host, phone;
        volatile bool disposed;
        volatile string error;
        public string Error { get { return error; } }
        public string Serial { get; private set; }
        public Task Completion { get; private set; }
        public TlsBridge(PhoneAddress endpoint) {
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start(1);
            Serial = "127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port;
            Completion = Task.Run(async () => {
                try {
                    host = await listener.AcceptTcpClientAsync();
                    if (disposed) throw new ObjectDisposedException("TlsBridge");
                    host.NoDelay = true;
                    listener.Stop(); // Exactly one ADB transport, loopback only.
                    host.ReceiveTimeout = host.SendTimeout = 6000;
                    byte[] hello = ReadConnect(host.GetStream());
                    phone = new TcpClient(AddressFamily.InterNetwork);
                    if (disposed) throw new ObjectDisposedException("TlsBridge");
                    phone.NoDelay = true;
                    var connect = phone.ConnectAsync(endpoint.IP, endpoint.Port);
                    if (await Task.WhenAny(connect, Task.Delay(6000)) != connect) {
                        phone.Close();
                        try { await connect; } catch { }
                        throw new TimeoutException("Телефон не ответил. Проверь Wi-Fi и адрес подключения.");
                    }
                    await connect;
                    phone.ReceiveTimeout = phone.SendTimeout = 6000;
                    var remote = phone.GetStream();
                    var local = host.GetStream();
                    remote.Write(hello, 0, hello.Length);
                    byte[] tlsRequest = ReadExactly(remote, 24);
                    if (!TlsProbe.IsTlsHeader(tlsRequest))
                        throw new IOException("Порт не подтвердил TLS. Подключение остановлено. Используй адрес с основного экрана «Беспроводная отладка».");
                    local.Write(tlsRequest, 0, tlsRequest.Length);
                    byte[] tlsReply = ReadExactly(local, 24);
                    if (!TlsProbe.IsTlsHeader(tlsReply)) throw new IOException("ADB не подтвердил переход на TLS.");
                    remote.Write(tlsReply, 0, tlsReply.Length);
                    // Both ends have committed to TLS. ADB performs mutual authentication.
                    // The actual media transport is gated, not a separate preflight socket.
                    // This bridge never decrypts data and never accepts a legacy AUTH/CNXN reply.
                    host.ReceiveTimeout = host.SendTimeout = 0;
                    phone.ReceiveTimeout = phone.SendTimeout = 0;
                    var toPhone = local.CopyToAsync(remote);
                    var toHost = remote.CopyToAsync(local);
                    await Task.WhenAny(toPhone, toHost);
                    CloseClients();
                    try { await Task.WhenAll(toPhone, toHost); } catch { }
                } catch (Exception ex) {
                    if (!disposed) error = ex.Message;
                } finally { CloseClients(); try { listener.Stop(); } catch { } }
            });
        }
        static byte[] ReadExactly(Stream stream, int length) {
            var result = new byte[length];
            for (int offset = 0; offset < length;) {
                int count = stream.Read(result, offset, length - offset);
                if (count == 0) throw new IOException("Соединение закрыто. Проверь порт подключения; порт сопряжения отличается.");
                offset += count;
            }
            return result;
        }
        static byte[] ReadConnect(Stream stream) {
            byte[] header = ReadExactly(stream, 24);
            uint command = BitConverter.ToUInt32(header, 0);
            uint length = BitConverter.ToUInt32(header, 12);
            if (command != 0x4e584e43 || BitConverter.ToUInt32(header, 20) != (command ^ 0xffffffff) || length > 4096)
                throw new IOException("Неверный заголовок ADB.");
            byte[] body = ReadExactly(stream, (int)length);
            var packet = new byte[header.Length + body.Length];
            Buffer.BlockCopy(header, 0, packet, 0, header.Length);
            Buffer.BlockCopy(body, 0, packet, header.Length, body.Length);
            return packet;
        }
        void CloseClients() {
            try { if (host != null) host.Close(); } catch { }
            try { if (phone != null) phone.Close(); } catch { }
        }
        public void Dispose() { disposed = true; try { listener.Stop(); } catch { } CloseClients(); }
    }

    public static partial class Package {
        public const string Version = "4.1";
        public const string Download = "https://github.com/Genymobile/scrcpy/releases/download/v4.1/scrcpy-win64-v4.1.zip";
        public const string Hash = "5b12172b3264b2889f4583ee64752ce832e29bc8b1089dca81093459697165db";
        public static string HashFile(string path) {
            using (var hash = SHA256.Create())
            using (var file = File.OpenRead(path))
                return BitConverter.ToString(hash.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
        }
        public static bool Ready(string root) {
            string dir = Path.Combine(root, "tools", "scrcpy");
            return File.Exists(Path.Combine(dir, "scrcpy.exe")) && File.Exists(Path.Combine(dir, "adb.exe"))
                && File.Exists(Path.Combine(dir, "scrcpy-server"));
        }
        public static string SafeDestination(string root, string relative) {
            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (Path.IsPathRooted(relative) || relative.IndexOf(':') >= 0)
                throw new IOException("Неверный путь внутри архива.");
            string path = Path.GetFullPath(Path.Combine(fullRoot, relative));
            if (!path.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Путь внутри архива выходит за пределы папки установки.");
            return path;
        }
        public static void Install(string zip, string root) {
            FileSafety.CheckPath(root);
            string target = Path.Combine(root, "tools", "scrcpy");
            string stage = Path.Combine(root, "tools", "stage-" + Guid.NewGuid().ToString("N"));
            FileSafety.CheckPath(Path.Combine(root, "tools"));
            try {
                // Keep the same read-locked handle from hashing through extraction.
                using (var file = new FileStream(zip, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                    using (var sha = SHA256.Create()) {
                        string actual = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
                        if (actual != Hash) throw new IOException("SHA-256 не совпал. Нужен официальный scrcpy-win64-v4.1.zip. Этот архив не установлен.");
                    }
                    file.Position = 0;
                    Directory.CreateDirectory(stage);
                    using (var archive = new ZipArchive(file, ZipArchiveMode.Read, true)) {
                    const string prefix = "scrcpy-win64-v4.1/";
                    foreach (var entry in archive.Entries) {
                        string name = entry.FullName.Replace('\\', '/');
                        if (!name.StartsWith(prefix, StringComparison.Ordinal)) throw new IOException("Неожиданная структура архива.");
                        string relative = name.Substring(prefix.Length);
                        if (relative.Length == 0) continue;
                        string destination = SafeDestination(stage, relative);
                        if (name.EndsWith("/")) Directory.CreateDirectory(destination);
                        else {
                            Directory.CreateDirectory(Path.GetDirectoryName(destination));
                            entry.ExtractToFile(destination, false);
                        }
                    }
                    }
                }
                foreach (string name in new[] { "scrcpy.exe", "adb.exe", "scrcpy-server" })
                    if (!File.Exists(Path.Combine(stage, name))) throw new IOException("В архиве отсутствует " + name);
                using (AcquireRuntimeDirectory(stage)) { }
                FileSafety.ProtectDirectory(stage);
                if (Directory.Exists(target)) throw new IOException("Папка tools\\scrcpy уже существует. Закрой приложение и переименуй эту папку перед переустановкой.");
                Directory.Move(stage, target);
            } finally {
                // Both paths are fixed children of the application workspace, never user input.
                FileSafety.DeleteChild(Path.Combine(root, "tools"), stage);
            }
        }
    }

    public sealed class CommandResult {
        public int ExitCode;
        public string Output;
    }

    public sealed class Engine : IDisposable {
        private readonly string root;
        private readonly string state;
        private readonly string toolDir;
        private int port;
        private bool serverStarted;
        private bool serverAttempted;
        private IDisposable runtimeLease;
        private readonly object gate = new object();
        private readonly HashSet<Process> commands = new HashSet<Process>();
        private readonly SemaphoreSlim initialization = new SemaphoreSlim(1, 1);
        private bool disposed;
        public Engine(string root) {
            this.root = root;
            state = Path.Combine(root, ".data");
            toolDir = Path.Combine(root, "tools", "scrcpy");
        }
        public static void NormalizeProcessEnvironment() {
            // Some launchers supply both Path and PATH in the environment block.
            // .NET Framework's case-insensitive ProcessStartInfo dictionary rejects this.
            var groups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var values = Environment.GetEnvironmentVariables();
            foreach (System.Collections.DictionaryEntry item in values) {
                string name = (string)item.Key;
                List<string> names;
                if (!groups.TryGetValue(name, out names)) { names = new List<string>(); groups[name] = names; }
                names.Add(name);
            }
            foreach (var names in groups.Values) {
                if (names.Count < 2) continue;
                string value = (string)values[names[0]];
                foreach (string name in names) Environment.SetEnvironmentVariable(name, null, EnvironmentVariableTarget.Process);
                Environment.SetEnvironmentVariable(names[0], value, EnvironmentVariableTarget.Process);
            }
        }
        public static string Quote(string value) {
            // CreateProcess argument escaping; no shell is ever invoked.
            var result = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in value) {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') result.Append('\\', slashes * 2 + 1).Append(c);
                else { result.Append('\\', slashes).Append(c); }
                slashes = 0;
            }
            return result.Append('\\', slashes * 2).Append('"').ToString();
        }
        private ProcessStartInfo StartInfo(string exe, string args) {
            NormalizeProcessEnvironment();
            var info = new ProcessStartInfo(Path.Combine(toolDir, exe), args);
            info.WorkingDirectory = toolDir;
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.RedirectStandardInput = true;
            // Use a dedicated local server; pairing uses ADB's standard Windows identity.
            // A host in this spec marks the server as remote and disables autostart.
            // tcp:PORT starts ADB's local-only listener (we never pass -a).
            info.EnvironmentVariables["ADB_SERVER_SOCKET"] = "tcp:" + port;
            info.EnvironmentVariables.Remove("ADB_VENDOR_KEYS");
            info.EnvironmentVariables["ADB_MDNS_AUTO_CONNECT"] = "0";
            info.EnvironmentVariables["ADB_MDNS_OPENSCREEN"] = "1";
            info.EnvironmentVariables["ADB"] = Path.Combine(toolDir, "adb.exe");
            info.EnvironmentVariables.Remove("ANDROID_SERIAL");
            info.EnvironmentVariables.Remove("ADB_TRACE");
            info.EnvironmentVariables.Remove("SCRCPY_SERVER_PATH");
            info.EnvironmentVariables.Remove("ANDROID_ADB_SERVER_ADDRESS");
            info.EnvironmentVariables.Remove("ANDROID_ADB_SERVER_PORT");
            return info;
        }
        public async Task InitializeAsync() {
            await initialization.WaitAsync();
            try {
                if (disposed) throw new ObjectDisposedException("Engine");
                if (serverStarted) return;
                if (!Package.Ready(root)) throw new IOException("Сначала добавь официальный архив scrcpy.");
                if (runtimeLease == null) runtimeLease = Package.AcquireRuntime(root);
                FileSafety.CheckPath(state);
                Directory.CreateDirectory(state);
                // Protect local connection preferences from other accounts.
                var acl = new DirectorySecurity();
                acl.SetAccessRuleProtection(true, false);
                var inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
                acl.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User,
                    FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
                acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                    FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
                Directory.SetAccessControl(state, acl);
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                port = ((IPEndPoint)listener.LocalEndpoint).Port;
                listener.Stop();
                serverAttempted = true;
                var start = await RunAsync("adb.exe", "start-server", null, 12000);
                if (start.ExitCode != 0) throw new IOException("ADB не запустился: " + start.Output);
                serverStarted = true;
            } finally { initialization.Release(); }
        }
        public async Task<CommandResult> AdbAsync(string args, string input, int timeoutMs) {
            await InitializeAsync();
            return await RunAsync("adb.exe", args, input, timeoutMs);
        }
        private async Task<CommandResult> RunAsync(string exe, string args, string input, int timeoutMs) {
            using (var process = new Process { StartInfo = StartInfo(exe, args) }) {
                lock (gate) {
                    if (disposed) throw new ObjectDisposedException("Engine");
                    process.Start();
                    commands.Add(process);
                }
                try {
                    var stdout = ReadBoundedAsync(process.StandardOutput);
                    var stderr = ReadBoundedAsync(process.StandardError);
                    if (input != null) await process.StandardInput.WriteLineAsync(input);
                    process.StandardInput.Close();
                    var wait = Task.Run(() => process.WaitForExit());
                    if (await Task.WhenAny(wait, Task.Delay(timeoutMs)) != wait) {
                        try { process.Kill(); } catch { }
                        await wait;
                        throw new TimeoutException("Время ожидания истекло. Проверь телефон и повтори подключение.");
                    }
                    await wait;
                    return new CommandResult { ExitCode = process.ExitCode, Output = ((await stdout) + "\n" + (await stderr)).Trim() };
                } finally { lock (gate) { commands.Remove(process); } }
            }
        }
        private static async Task<string> ReadBoundedAsync(StreamReader reader) {
            const int limit = 32768;
            var result = new StringBuilder(); var buffer = new char[4096]; int read;
            while ((read = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0) {
                result.Append(buffer, 0, read);
                if (result.Length > limit) result.Remove(0, result.Length - limit);
            }
            return result.ToString();
        }
        public Process StartMirror(string endpoint, int profile, bool sound, bool duplicate, bool control, bool keyboard, bool displayOff, string codec, Action<string> output) {
            if (!serverStarted || disposed) throw new InvalidOperationException("Сначала подключи телефон.");
            string args = MirrorArguments(endpoint, profile, sound, duplicate, control, keyboard, displayOff, codec);
            var process = new Process { StartInfo = StartInfo("scrcpy.exe", args), EnableRaisingEvents = true };
            process.OutputDataReceived += (s, e) => { if (e.Data != null) output(e.Data); };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) output(e.Data); };
            process.Start();
            process.StandardInput.Close();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return process;
        }
        public static string MirrorArguments(string endpoint, int profile, bool sound, bool duplicate, bool control, bool keyboard, bool displayOff, string codec) {
            var local = Regex.Match(endpoint ?? "", @"^127\.0\.0\.1:(\d{1,5})$");
            int localPort;
            if (!local.Success || !Int32.TryParse(local.Groups[1].Value, out localPort) || localPort < 1024 || localPort > 65535)
                throw new ArgumentException("Трансляция разрешена только через локальный TLS-мост.");
            if (profile < 0 || profile > 2 || (codec != "opus" && codec != "aac")) throw new ArgumentException("Неверный профиль.");
            var args = new StringBuilder("--serial=" + Quote(endpoint) + " --window-title=" + Quote("PhoneScreen — Android") + " --video-codec=h264 --no-clipboard-autosync");
            if (profile == 0) args.Append(" --max-size=1600 --max-fps=60 --video-bit-rate=8M");
            if (profile == 1) args.Append(" --max-size=1080 --max-fps=30 --video-bit-rate=4M");
            if (profile == 2) args.Append(" --max-size=1920 --max-fps=60 --video-bit-rate=12M --video-buffer=120");
            if (sound) {
                args.Append(" --require-audio --audio-codec=" + codec + " --audio-bit-rate=192K --audio-buffer=" + (profile == 2 ? "120" : "80"));
                if (duplicate) args.Append(" --audio-dup");
                else args.Append(" --audio-source=output");
            } else args.Append(" --no-audio");
            if (!control) args.Append(" --no-control");
            else if (keyboard) args.Append(" --keyboard=uhid");
            if (displayOff && control) args.Append(" --turn-screen-off");
            return args.ToString();
        }
        public async Task ShutdownAsync() {
            if (serverAttempted && !disposed) {
                try { await RunAsync("adb.exe", "kill-server", null, 4000); } catch { }
                serverStarted = false;
                serverAttempted = false;
            }
            if (runtimeLease != null) { runtimeLease.Dispose(); runtimeLease = null; }
        }
        public void CancelCommands() {
            lock (gate) {
                foreach (var process in commands) { try { process.Kill(); } catch { } }
                if (runtimeLease != null) { runtimeLease.Dispose(); runtimeLease = null; }
            }
        }
        public void Dispose() {
            lock (gate) {
                disposed = true;
                foreach (var process in commands) { try { process.Kill(); } catch { } }
            }
        }
    }
}
