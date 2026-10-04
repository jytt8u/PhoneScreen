using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using PhoneScreen;

static class Tests {
    static int count;
    static void Check(bool ok, string name) {
        if (!ok) throw new Exception("FAIL: " + name);
        Console.WriteLine("PASS: " + name); System.Threading.Interlocked.Increment(ref count);
    }
    static void Reject(Action action, string name) {
        bool threw = false;
        try { action(); } catch (ArgumentException) { threw = true; } catch (IOException) { threw = true; }
        Check(threw, name);
    }
    static byte[] Header(uint cmd, int length) {
        using (var m = new MemoryStream()) {
            using (var w = new BinaryWriter(m, Encoding.ASCII, true)) {
                w.Write(cmd); w.Write((uint)0x01000000); w.Write((uint)0); w.Write((uint)length);
                w.Write((uint)0); w.Write(cmd ^ 0xffffffff);
            }
            return m.ToArray();
        }
    }
    static byte[] Read(Stream s, int size) {
        byte[] data = new byte[size]; int offset = 0;
        while (offset < size) { int n = s.Read(data, offset, size - offset); if (n == 0) throw new IOException("EOF"); offset += n; }
        return data;
    }
    static async Task TestBridge(bool tls) {
        var phone = new TcpListener(IPAddress.Loopback, 0);
        phone.Start();
        try {
            int port = ((IPEndPoint)phone.LocalEndpoint).Port;
            using (var bridge = new TlsBridge(new PhoneAddress { IP = IPAddress.Loopback, Port = port }))
            using (var host = new TcpClient()) {
                int bridgePort = Int32.Parse(bridge.Serial.Split(':')[1]);
                var remote = Task.Run(async () => {
                    using (var device = await phone.AcceptTcpClientAsync()) {
                        device.ReceiveTimeout = 3000;
                        var wire = device.GetStream();
                        var hello = Read(wire, 24);
                        Read(wire, (int)BitConverter.ToUInt32(hello, 12));
                        byte[] response = Header(tls ? TlsProbe.STLS : (uint)0x48545541, 0);
                        wire.Write(response, 0, response.Length);
                        if (tls) {
                            Check(TlsProbe.IsTlsHeader(Read(wire, 24)), "host commits to STLS on real transport");
                            Check(Encoding.ASCII.GetString(Read(wire, 5)) == "hello", "post-STLS client bytes relayed");
                            wire.Write(Encoding.ASCII.GetBytes("reply"), 0, 5);
                        } else {
                            // No key, shell or media may reach a legacy endpoint.
                            Check(wire.ReadByte() == -1, "legacy device receives no authentication or media");
                        }
                    }
                });
                await host.ConnectAsync(IPAddress.Loopback, bridgePort);
                host.ReceiveTimeout = 3000;
                var stream = host.GetStream();
                byte[] cnxn = TlsProbe.ConnectPacket(); stream.Write(cnxn, 0, cnxn.Length);
                if (tls) {
                    Check(TlsProbe.IsTlsHeader(Read(stream, 24)), "device STLS reaches ADB host");
                    byte[] stls = Header(TlsProbe.STLS, 0); stream.Write(stls, 0, stls.Length);
                    stream.Write(Encoding.ASCII.GetBytes("hello"), 0, 5);
                    Check(Encoding.ASCII.GetString(Read(stream, 5)) == "reply", "post-STLS device bytes relayed");
                } else Check(stream.ReadByte() == -1, "plaintext AUTH rejected before reaching host");
                await remote;
                host.Close();
                Check(await Task.WhenAny(bridge.Completion, Task.Delay(4000)) == bridge.Completion, "bridge terminates and releases sockets");
                await bridge.Completion;
                Check(tls ? bridge.Error == null : bridge.Error != null && bridge.Error.Contains("TLS"), "bridge reports correct encryption outcome");
            }
        } finally { phone.Stop(); }
    }
    static async Task EngineCheck(string root) {
        using (var engine = new Engine(root)) {
            Exception failure = null;
            try {
                var version = await engine.AdbAsync("version", null, 10000);
                Check(version.ExitCode == 0 && version.Output.Contains("37.0.0"), "official ADB starts through private engine");
                var status = await engine.AdbAsync("server-status", null, 10000);
                Check(status.ExitCode == 0, "private ADB server responds");
                var portField = typeof(Engine).GetField("port", BindingFlags.NonPublic | BindingFlags.Instance);
                int serverPort = (int)portField.GetValue(engine);
                var listeners = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
                bool found = false, exposed = false;
                foreach (var endpoint in listeners) if (endpoint.Port == serverPort) {
                    found = true; if (!IPAddress.IsLoopback(endpoint.Address)) exposed = true;
                }
                Check(found && !exposed, "real ADB listener is loopback only");
                Check(status.Output.Contains("keystore_path:") && status.Output.Contains(".android"), "standard Windows ADB identity is reported accurately");
            } catch (Exception ex) { failure = ex; }
            await engine.ShutdownAsync();
            if (failure != null) throw failure;
        }
    }
    static void RuntimeChecks(string root) {
        var clock = Stopwatch.StartNew();
        using (Package.AcquireRuntime(root)) { }
        Console.WriteLine("Runtime verification: " + clock.ElapsedMilliseconds + " ms");
        Check(true, "installed runtime matches embedded manifest");
        string temp = Path.Combine(Path.GetTempPath(), "phonescreen-runtime-" + Guid.NewGuid().ToString("N"));
        string copy = Path.Combine(temp, "tools", "scrcpy"); Directory.CreateDirectory(copy);
        try {
            string original = Path.Combine(root, "tools", "scrcpy");
            foreach (string file in Directory.GetFiles(original)) File.Copy(file, Path.Combine(copy, Path.GetFileName(file)));
            string adb = Path.Combine(copy, "adb.exe");
            using (Package.AcquireRuntime(temp)) {
                Reject(() => { using (File.Open(adb, FileMode.Open, FileAccess.Write, FileShare.ReadWrite)) { } }, "verified binaries remain write-locked during execution");
            }
            using (var file = File.Open(adb, FileMode.Open, FileAccess.ReadWrite)) {
                int value = file.ReadByte(); file.Position = 0; file.WriteByte((byte)(value ^ 1));
            }
            File.WriteAllText(Path.Combine(copy, ".verified"), Package.Hash);
            Reject(() => { using (Package.AcquireRuntime(temp)) { } }, "forged marker cannot authorize modified ADB");
            File.Copy(Path.Combine(original, "adb.exe"), adb, true);
            File.WriteAllText(Path.Combine(copy, "injected.dll"), "fake DLL");
            Reject(() => { using (Package.AcquireRuntime(temp)) { } }, "unexpected DLL cannot enter runtime search directory");
        } finally { FileSafety.DeleteChild(Path.GetTempPath(), temp); }
    }
    static int Main(string[] cli) {
        try {
            if (cli.Length == 1 && cli[0] == "--download") {
                string installRoot = Path.Combine(Path.GetTempPath(), "phonescreen-download-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(installRoot);
                try {
                    var clock = Stopwatch.StartNew();
                    Package.DownloadAndInstallAsync(installRoot, null, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                    Check(Package.Ready(installRoot), "automatic download and installation complete");
                    using (Package.AcquireRuntime(installRoot)) { }
                    Check(true, "downloaded runtime matches embedded hashes");
                    Console.WriteLine("Download + install: " + clock.ElapsedMilliseconds + " ms");
                } finally { FileSafety.DeleteChild(Path.GetTempPath(), installRoot); }
                return 0;
            }
            Check(PhoneAddress.Parse("192.168.3.42:37001").ToString() == "192.168.3.42:37001", "RFC1918 Wi-Fi accepted");
            Check(PhoneAddress.Parse("10.0.0.2:44444").Port == 44444, "10/8 accepted");
            Check(PhoneAddress.Parse("172.31.4.2:65535").Port == 65535, "172.16/12 accepted");
            foreach (string bad in new[] { "8.8.8.8:40000", "127.0.0.1:40000", "172.32.0.1:40000", "192.168.1.2:5555", "192.168.1.2:80", "192.168.1.2:65536", "192.168.1.2", "phone.local:40000", "192.168.1.2:40000 & calc", "::1:40000", "192.168.999.1:40000" })
                Reject(() => PhoneAddress.Parse(bad), "reject endpoint " + bad);
            byte[] stlsHeader = Header(TlsProbe.STLS, 0);
            Check(TlsProbe.IsTlsHeader(stlsHeader), "valid STLS header accepted");
            stlsHeader[20] ^= 1;
            Check(!TlsProbe.IsTlsHeader(stlsHeader), "corrupt magic rejected");
            Check(!TlsProbe.IsTlsHeader(Header(TlsProbe.STLS, 10000)), "unexpected STLS body rejected");
            Check(!TlsProbe.IsTlsHeader(Header(0x4e584e43, 0)), "unencrypted CNXN reply rejected");
            Check(!TlsProbe.IsTlsHeader(new byte[5]), "truncated header rejected");
            var badVersion = Header(TlsProbe.STLS, 0); badVersion[7] = 0;
            Check(!TlsProbe.IsTlsHeader(badVersion), "invalid TLS protocol version rejected");
            foreach (string badUri in new[] { "http://github.com/x", "https://github.com.evil.test/x", "https://github.com:444/x", "https://user@github.com/x", "https://example.com/x" })
                Check(!Package.AllowedDownloadUri(new Uri(badUri)), "reject download redirect " + badUri);
            Check(Package.AllowedDownloadUri(new Uri(Package.Download)), "pinned official download URL accepted");
            Check(Package.RuntimeManifest().ContainsKey("adb.exe"), "runtime manifest embedded in executable");
            string args = Engine.MirrorArguments("127.0.0.1:42000", 0, true, false, true, true, false, "opus");
            Check(args.Contains("--require-audio") && args.Contains("--audio-source=output"), "audio failures cannot silently produce video only");
            Check(args.Contains("--no-clipboard-autosync"), "automatic clipboard synchronization disabled");
            Check(args.Contains("--keyboard=uhid"), "Unicode-capable keyboard enabled");
            args = Engine.MirrorArguments("127.0.0.1:42000", 1, false, false, false, true, true, "aac");
            Check(args.Contains("--no-control") && args.Contains("--no-audio") && !args.Contains("--keyboard") && !args.Contains("--turn-screen-off"), "view only does not alter phone or inject input");
            Check(args.Contains("--max-fps=30") && args.Contains("--video-bit-rate=4M"), "weak Wi-Fi profile reduces bandwidth");
            args = Engine.MirrorArguments("127.0.0.1:42000", 2, true, true, true, false, false, "aac");
            Check(args.Contains("--audio-dup") && args.Contains("--audio-codec=aac") && args.Contains("--video-buffer=120"), "duplicate audio and video profile configured");
            Reject(() => Engine.MirrorArguments("192.168.1.2:40000", 0, true, false, true, true, false, "opus"), "scrcpy cannot bypass TLS bridge");
            Reject(() => Engine.MirrorArguments("127.0.0.1:42000", 0, true, false, true, true, false, "opus --no-control"), "audio codec injection rejected");
            string scratch = Path.Combine(Path.GetTempPath(), "phonescreen-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch);
            try {
                string safe = Package.SafeDestination(scratch, "folder/adb.exe");
                Check(safe.StartsWith(scratch + Path.DirectorySeparatorChar), "ZIP destination stays within install directory");
                foreach (string name in new[] { "../escape.exe", "..\\escape.exe", "C:\\escape.exe", "file.txt:stream", "\\\\server\\share\\x" })
                    Reject(() => Package.SafeDestination(scratch, name), "reject ZIP path " + name);
                string fakeZip = Path.Combine(scratch, "fake.zip"); File.WriteAllText(fakeZip, "not an official ZIP");
                Reject(() => Package.Install(fakeZip, scratch), "unverified release rejected before extraction");
                Check(!Directory.Exists(Path.Combine(scratch, "tools", "scrcpy")), "bad archive creates no runtime");
                using (var engine = new Engine(scratch)) {
                    var method = typeof(Engine).GetMethod("StartInfo", BindingFlags.NonPublic | BindingFlags.Instance);
                    var info = (ProcessStartInfo)method.Invoke(engine, new object[] { "adb.exe", "version" });
                    Check(!info.UseShellExecute && info.CreateNoWindow, "no shell or console used for commands");
                    Check(info.EnvironmentVariables["ADB_SERVER_SOCKET"] == "tcp:0", "ADB server uses local-only socket spec");
                    Check(info.EnvironmentVariables["ADB_MDNS_AUTO_CONNECT"] == "0", "unrequested auto-connections disabled");
                    Check(info.EnvironmentVariables["ADB_VENDOR_KEYS"] == null, "inherited vendor key override removed");
                    Check(info.EnvironmentVariables["SCRCPY_SERVER_PATH"] == null, "inherited scrcpy server override removed");
                }
            } finally { FileSafety.DeleteChild(Path.GetTempPath(), scratch); }
            TestBridge(false).GetAwaiter().GetResult();
            TestBridge(true).GetAwaiter().GetResult();
            if (cli.Length == 2 && cli[0] == "--runtime") {
                RuntimeChecks(Path.GetFullPath(cli[1]));
                EngineCheck(Path.GetFullPath(cli[1])).GetAwaiter().GetResult();
            }
            Console.WriteLine("All " + count + " checks passed.");
            return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
