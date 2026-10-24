using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace TransportFever3Modifier.Portable
{
    static class Launcher
    {
        const string ResourcePrefix = "TransportFever3Modifier.Portable.";
        public sealed class PayloadFile
        {
            public string path { get; set; }
            public string resource { get; set; }
            public string sha256 { get; set; }
            public long bytes { get; set; }
        }
        public sealed class PayloadIndex
        {
            public int format { get; set; }
            public string mainProgram { get; set; }
            public string version { get; set; }
            public PayloadFile[] files { get; set; }
        }

        static bool Offline
        {
            get { return Environment.GetEnvironmentVariable("TF3_MODIFIER_BUILD_CHECK") == "1" || Array.IndexOf(Environment.GetCommandLineArgs(), "--layout-check") >= 0; }
        }
        static string Hex(byte[] value) { return BitConverter.ToString(value).Replace("-", "").ToLowerInvariant(); }
        static string Hash(byte[] value) { using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(value)); }
        static string FileHash(string path)
        {
            using (var input = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(input));
        }
        static bool Matches(string path, PayloadFile file)
        { return File.Exists(path) && new FileInfo(path).Length == file.bytes && FileHash(path) == file.sha256; }
        static string PayloadPath(string root, string relative)
        {
            if (string.IsNullOrEmpty(relative) || Path.IsPathRooted(relative) || relative.IndexOf(':') >= 0)
                throw new InvalidDataException("便携资源路径无效。");
            string full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("便携资源路径超出缓存目录。");
            return full;
        }
        static void Extract(string root, PayloadFile file)
        {
            string path = PayloadPath(root, file.path);
            if (Matches(path, file)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var input = Assembly.GetExecutingAssembly().GetManifestResourceStream(file.resource))
                {
                    if (input == null) throw new InvalidDataException("缺少内嵌资源：" + file.path);
                    using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) input.CopyTo(output);
                }
                if (!Matches(temp, file)) throw new InvalidDataException("内嵌资源校验失败：" + file.path);
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        static string Quote(string value)
        {
            // Windows command-line escaping, including quotes and trailing slashes.
            var result = new StringBuilder("\""); int slashes = 0;
            foreach (char c in value)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') { result.Append('\\', slashes * 2 + 1); result.Append(c); }
                else { result.Append('\\', slashes); result.Append(c); }
                slashes = 0;
            }
            result.Append('\\', slashes * 2); result.Append('"'); return result.ToString();
        }
        static string DataHome(string portableDirectory, string localData, string cacheBase)
        {
            if (Offline) { string test = Path.Combine(cacheBase, "profile"); Directory.CreateDirectory(test); return test; }
            string probe = Path.Combine(portableDirectory, ".tf3-write-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
                File.Delete(probe); return portableDirectory;
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
            string fallback = Path.Combine(localData, "TransportFever3Modifier", "UserData");
            Directory.CreateDirectory(fallback); return fallback;
        }

        [STAThread]
        static int Main(string[] args)
        {
            try
            {
                byte[] indexBytes;
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourcePrefix + "Index.json"))
                {
                    if (stream == null) throw new InvalidDataException("缺少便携资源清单。");
                    using (var memory = new MemoryStream()) { stream.CopyTo(memory); indexBytes = memory.ToArray(); }
                }
                var index = new JavaScriptSerializer().Deserialize<PayloadIndex>(Encoding.UTF8.GetString(indexBytes));
                if (index == null || index.format != 1 || index.files == null || index.files.Length < 1 || index.files.Length > 32 ||
                    !index.files.Any(f => f != null && f.path == index.mainProgram)) throw new InvalidDataException("便携资源清单无效。");
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var file in index.files)
                    if (file == null || !names.Add(file.path ?? "") || file.bytes < 0 || file.bytes > 67108864 ||
                        file.sha256 == null || file.sha256.Length != 64 || !file.sha256.All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f') ||
                        file.resource == null || !file.resource.StartsWith(ResourcePrefix + "File.", StringComparison.Ordinal))
                        throw new InvalidDataException("便携资源记录无效。");
                string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string cacheBase = Path.Combine(localData, "TransportFever3Modifier", "portable");
                string testCache = Environment.GetEnvironmentVariable("TF3_MODIFIER_PORTABLE_CACHE");
                if (Offline && !string.IsNullOrWhiteSpace(testCache)) cacheBase = Path.GetFullPath(testCache);
                string identity = Hash(indexBytes), cache = Path.GetFullPath(Path.Combine(cacheBase, identity));
                Directory.CreateDirectory(cache);
                using (var gate = new Mutex(false, "Local\\TF3ModifierPortable_" + identity))
                {
                    bool acquired = false;
                    try
                    {
                        try { acquired = gate.WaitOne(30000); } catch (AbandonedMutexException) { acquired = true; }
                        if (!acquired) throw new IOException("另一个便携程序正在准备资源，请稍后重试。");
                        foreach (var file in index.files) Extract(cache, file);
                    }
                    finally { if (acquired) gate.ReleaseMutex(); }
                }
                string portableDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                var start = new ProcessStartInfo(PayloadPath(cache, index.mainProgram)) {
                    UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Environment.CurrentDirectory,
                    Arguments = string.Join(" ", args.Select(Quote).ToArray())
                };
                start.EnvironmentVariables["TF3_MODIFIER_PORTABLE_HOME"] = DataHome(portableDirectory, localData, cacheBase);
                if (Offline) start.EnvironmentVariables["TF3_MODIFIER_BUILD_CHECK"] = "1";
                using (var child = Process.Start(start))
                {
                    if (child == null) throw new IOException("便携程序启动失败。");
                    if (Offline && !child.WaitForExit(45000))
                    {
                        child.Kill(); child.WaitForExit(5000); throw new TimeoutException("便携程序的离线界面检查超时。");
                    }
                    child.WaitForExit(); return child.ExitCode;
                }
            }
            catch (Exception error)
            {
                if (Offline)
                {
                    string report = Environment.GetEnvironmentVariable("TF3_MODIFIER_LAYOUT_REPORT");
                    if (string.IsNullOrWhiteSpace(report) && args.Length > 1) report = args[1];
                    if (!string.IsNullOrWhiteSpace(report)) File.WriteAllText(report,
                        new JavaScriptSerializer().Serialize(new[] { new { portableLauncher = error.ToString(), pass = false } }), Encoding.UTF8);
                }
                else MessageBox.Show("便携程序启动失败：\r\n" + error.Message, "狂热运输3修改器", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 3;
            }
        }
    }
}
