using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PhoneScreen {
    public static class FileSafety {
        public static void CheckPath(string path) {
            string current = Path.GetFullPath(path);
            while (current != null) {
                if ((Directory.Exists(current) || File.Exists(current)) &&
                    (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Ссылки и точки перенаправления в папках программы не поддерживаются: " + current);
                current = Path.GetDirectoryName(current);
            }
        }
        public static void DeleteChild(string parent, string child) {
            string root = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string target = Path.GetFullPath(child);
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new IOException("Небезопасный путь очистки.");
            CheckPath(target);
            if (Directory.Exists(target)) Directory.Delete(target, true);
            else if (File.Exists(target)) File.Delete(target);
        }
        public static void ProtectDirectory(string path) {
            CheckPath(path);
            var acl = new DirectorySecurity();
            acl.SetAccessRuleProtection(true, false);
            var inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            acl.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User,
                FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
            acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
            Directory.SetAccessControl(path, acl);
        }
    }
    sealed class RuntimeLease : IDisposable {
        readonly List<FileStream> handles;
        public RuntimeLease(List<FileStream> handles) { this.handles = handles; }
        public void Dispose() { foreach (var file in handles) file.Dispose(); handles.Clear(); }
    }
    public static partial class Package {
        const long MaxDownload = 128L * 1024 * 1024;
        public static Dictionary<string, string> RuntimeManifest() {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PhoneScreen.runtime.sha256")) {
                if (stream == null) throw new IOException("В сборке отсутствует список контрольных сумм движка.");
                using (var reader = new StreamReader(stream, Encoding.UTF8)) {
                    string line;
                    while ((line = reader.ReadLine()) != null) {
                        if (line.Length < 67 || line.Substring(64, 2) != "  ") throw new IOException("Неверный список контрольных сумм.");
                        result.Add(line.Substring(66), line.Substring(0, 64));
                    }
                }
            }
            return result;
        }
        public static IDisposable AcquireRuntime(string root) { return AcquireRuntimeDirectory(Path.Combine(root, "tools", "scrcpy")); }
        public static IDisposable AcquireRuntimeDirectory(string directory) {
            FileSafety.CheckPath(directory);
            var expected = RuntimeManifest();
            var handles = new List<FileStream>();
            try {
                var pending = new Stack<string>();
                pending.Push(directory);
                while (pending.Count != 0) {
                    string current = pending.Pop();
                    FileSafety.CheckPath(current);
                    foreach (string child in Directory.GetDirectories(current)) {
                        FileSafety.CheckPath(child);
                        pending.Push(child);
                    }
                    foreach (string file in Directory.GetFiles(current)) {
                        FileSafety.CheckPath(file);
                        string relative = file.Substring(directory.TrimEnd(Path.DirectorySeparatorChar).Length + 1).Replace('\\', '/');
                        // Old versions wrote this marker. It never grants execution permission.
                        if (relative == ".verified") continue;
                        if (!expected.ContainsKey(relative)) throw new IOException("Неизвестный файл в папке движка: " + relative);
                    }
                }
                foreach (var item in expected) {
                    string path = SafeDestination(directory, item.Key);
                    FileSafety.CheckPath(path);
                    var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    handles.Add(file);
                    using (var sha = SHA256.Create()) {
                        string actual = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
                        if (actual != item.Value) throw new IOException("Изменён файл движка: " + item.Key + ". Установи официальный архив заново.");
                    }
                }
                return new RuntimeLease(handles);
            } catch { foreach (var file in handles) file.Dispose(); throw; }
        }
        public static bool AllowedDownloadUri(Uri uri) {
            if (uri == null || uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443 || uri.UserInfo.Length != 0) return false;
            return uri.Host == "github.com" || uri.Host == "release-assets.githubusercontent.com" || uri.Host == "objects.githubusercontent.com";
        }
        public static async Task DownloadAndInstallAsync(string root, IProgress<int> progress, CancellationToken cancellation) {
            FileSafety.CheckPath(root);
            string cache = Path.Combine(root, "downloads");
            FileSafety.CheckPath(cache);
            Directory.CreateDirectory(cache);
            string zip = Path.Combine(cache, "scrcpy-win64-v4.1.zip");
            if (File.Exists(zip) && HashFile(zip) == Hash) {
                await Task.Run(() => Install(zip, root));
                if (progress != null) progress.Report(100);
                return;
            }
            string partial = Path.Combine(cache, "download-" + Guid.NewGuid().ToString("N") + ".part");
            try {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
                using (var handler = new HttpClientHandler { AllowAutoRedirect = false })
                using (var client = new HttpClient(handler)) {
                    timeout.CancelAfter(180000);
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("PhoneScreen/1.0");
                    Uri uri = new Uri(Download);
                    for (int redirect = 0; redirect < 6; redirect++) {
                        if (!AllowedDownloadUri(uri)) throw new IOException("Отклонён неподдерживаемый адрес загрузки.");
                        using (var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token)) {
                            int code = (int)response.StatusCode;
                            if (code >= 300 && code <= 399) {
                                if (response.Headers.Location == null) throw new IOException("Не удалось получить адрес архива.");
                                uri = response.Headers.Location.IsAbsoluteUri ? response.Headers.Location : new Uri(uri, response.Headers.Location);
                                continue;
                            }
                            response.EnsureSuccessStatusCode();
                            long total = response.Content.Headers.ContentLength ?? -1;
                            if (total > MaxDownload) throw new IOException("Архив превышает допустимый размер.");
                            using (var incoming = await response.Content.ReadAsStreamAsync())
                            using (var outgoing = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true)) {
                                var buffer = new byte[65536]; long received = 0; int read; int lastProgress = -1;
                                while ((read = await incoming.ReadAsync(buffer, 0, buffer.Length, timeout.Token)) != 0) {
                                    received += read;
                                    if (received > MaxDownload) throw new IOException("Архив превышает допустимый размер.");
                                    await outgoing.WriteAsync(buffer, 0, read, timeout.Token);
                                    int value = total > 0 ? (int)Math.Min(99, received * 100 / total) : 0;
                                    if (progress != null && value != lastProgress) { progress.Report(value); lastProgress = value; }
                                }
                            }
                            cancellation.ThrowIfCancellationRequested();
                            await Task.Run(() => Install(partial, root));
                            if (File.Exists(zip)) FileSafety.DeleteChild(cache, zip);
                            File.Move(partial, zip);
                            if (progress != null) progress.Report(100);
                            return;
                        }
                    }
                    throw new IOException("Слишком много перенаправлений при загрузке.");
                }
            } finally { FileSafety.DeleteChild(cache, partial); }
        }
    }
}
