using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace LocalSubtitleGui
{
    internal sealed class ModelDefinition
    {
        internal readonly string DisplayName;
        internal readonly string FileName;
        internal readonly string Url;
        internal readonly string Sha1;
        internal readonly long MinimumBytes;
        internal readonly string SizeText;

        internal ModelDefinition(string displayName, string fileName, string url,
            string sha1, long minimumBytes, string sizeText)
        {
            DisplayName = displayName;
            FileName = fileName;
            Url = url;
            Sha1 = sha1;
            MinimumBytes = minimumBytes;
            SizeText = sizeText;
        }
    }

    internal static class ComponentInstaller
    {
        internal const string WhisperBuildTag = "b5130";
        internal const string WhisperVersion = "1.9.4";
        internal const string WhisperArchiveName = "whisper-bin-x64.zip";
        internal const string WhisperArchiveUrl = "https://github.com/ggml-org/whisper.cpp/releases/download/b5130/whisper-bin-x64.zip";
        internal const string WhisperArchiveSha256 = "F9EC6C52A2E949B62AB51FA21D0D497958F9E41C3010C157C4E42932D5316F3C";
        internal const string FfmpegArchiveName = "ffmpeg-master-latest-win64-gpl.zip";
        internal const string FfmpegUrl = "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";
        internal const string FfmpegChecksumsUrl = "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/checksums.sha256";
        internal const string FfmpegBackupUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";
        internal const string FfmpegBackupChecksumsUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/checksums.sha256";

        internal static readonly ModelDefinition[] Models =
        {
            new ModelDefinition(
                "快速 · Base（约 142 MiB）",
                "ggml-base.bin",
                "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin",
                "465707469FF3A37A2B9B8D8F89F2F99DE7299DAC",
                120L * 1024L * 1024L,
                "约 142 MiB"),
            new ModelDefinition(
                "均衡 · Small（约 466 MiB，推荐）",
                "ggml-small.bin",
                "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin",
                "55356645C2B361A969DFD0EF2C5A50D530AFD8D5",
                400L * 1024L * 1024L,
                "约 466 MiB"),
            new ModelDefinition(
                "高质量 · Medium（约 1.5 GiB，速度较慢）",
                "ggml-medium.bin",
                "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-medium.bin",
                "FD9727B6E1217C2F614F9B698455C4FFD82463B4",
                1300L * 1024L * 1024L,
                "约 1.5 GiB")
        };

        private sealed class DownloadSource
        {
            internal readonly string Name;
            internal readonly string FileUrl;
            internal readonly string ChecksumsUrl;

            internal DownloadSource(string name, string fileUrl, string checksumsUrl)
            {
                Name = name;
                FileUrl = fileUrl;
                ChecksumsUrl = checksumsUrl;
            }
        }

        internal static string WhisperDirectory(string appDir)
        {
            return Path.Combine(appDir, "runtime", "whisper");
        }

        internal static string WhisperExecutable(string appDir)
        {
            return Path.Combine(WhisperDirectory(appDir), "whisper-cli.exe");
        }

        internal static string FfmpegExecutable(string appDir)
        {
            return Path.Combine(appDir, "runtime", "ffmpeg", "ffmpeg.exe");
        }

        internal static string ModelsDirectory(string appDir)
        {
            return Path.Combine(appDir, "models");
        }

        internal static string ModelPath(string appDir, ModelDefinition model)
        {
            return Path.Combine(ModelsDirectory(appDir), model.FileName);
        }

        internal static string[] MissingRuntimeComponents(string appDir)
        {
            var result = new List<string>();
            if (!IsWhisperRuntimeComplete(appDir)) result.Add("whisper-cli CPU运行库");
            if (!File.Exists(FfmpegExecutable(appDir))) result.Add("FFmpeg");
            return result.ToArray();
        }

        internal static bool IsWhisperRuntimeComplete(string appDir)
        {
            string directory = WhisperDirectory(appDir);
            if (!File.Exists(Path.Combine(directory, "whisper-cli.exe")) ||
                !File.Exists(Path.Combine(directory, "whisper.dll")) ||
                !File.Exists(Path.Combine(directory, "ggml.dll")) ||
                !File.Exists(Path.Combine(directory, "ggml-base.dll"))) return false;
            return Directory.Exists(directory) &&
                   Directory.GetFiles(directory, "ggml-cpu-*.dll", SearchOption.TopDirectoryOnly).Length > 0;
        }

        internal static bool IsModelInstalled(string appDir, ModelDefinition model)
        {
            string path = ModelPath(appDir, model);
            if (!File.Exists(path) || new FileInfo(path).Length < model.MinimumBytes) return false;
            string marker = path + ".sha1";
            if (!File.Exists(marker)) return false;
            return String.Equals(File.ReadAllText(marker).Trim(), model.Sha1,
                StringComparison.OrdinalIgnoreCase);
        }

        internal static void InstallRuntime(string appDir, Action<int, string> report,
            Action<string> log, Func<bool> isCancelled)
        {
            if (!Directory.Exists(appDir)) throw new DirectoryNotFoundException("程序目录不存在：" + appDir);
            string tempDir = Path.Combine(appDir, ".component-cache-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                if (!IsWhisperRuntimeComplete(appDir))
                {
                    report(0, "正在下载 whisper.cpp CPU运行库…");
                    string archive = Path.Combine(tempDir, WhisperArchiveName);
                    DownloadFixedAndVerify(WhisperArchiveUrl, WhisperArchiveSha256, archive,
                        p => report(p, "正在下载 whisper.cpp：" + p + "%"), log, isCancelled);
                    string extracted = Path.Combine(tempDir, "whisper-runtime");
                    ExtractWhisperRuntime(archive, extracted);
                    ValidateExecutable(Path.Combine(extracted, "whisper-cli.exe"), "whisper-cli.exe");
                    ReplaceDirectory(extracted, WhisperDirectory(appDir));
                    log("whisper.cpp " + WhisperVersion + " CPU运行库已安装并通过 SHA-256 校验。");
                }

                if (!File.Exists(FfmpegExecutable(appDir)))
                {
                    report(0, "正在下载 FFmpeg（文件较大，请耐心等待）…");
                    string archive = Path.Combine(tempDir, FfmpegArchiveName);
                    DownloadAndVerify(new[] {
                            new DownloadSource("yt-dlp FFmpeg 官方构建线路", FfmpegUrl, FfmpegChecksumsUrl),
                            new DownloadSource("FFmpeg-Builds 上游备用线路", FfmpegBackupUrl, FfmpegBackupChecksumsUrl)
                        }, FfmpegArchiveName, archive,
                        p => report(p, "正在下载 FFmpeg：" + p + "%"), log, isCancelled);
                    string ffmpeg = ExtractNamedExecutable(archive, "ffmpeg.exe", tempDir);
                    ValidateExecutable(ffmpeg, "ffmpeg.exe");
                    string ffmpegDir = Path.GetDirectoryName(FfmpegExecutable(appDir));
                    Directory.CreateDirectory(ffmpegDir);
                    InstallFile(ffmpeg, FfmpegExecutable(appDir));
                    log("FFmpeg 已安装，压缩包已通过 SHA-256 校验。");
                }
            }
            finally
            {
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); }
                catch { }
            }
        }

        internal static void InstallModel(string appDir, ModelDefinition model,
            Action<int, string> report, Action<string> log, Func<bool> isCancelled)
        {
            string modelsDir = ModelsDirectory(appDir);
            Directory.CreateDirectory(modelsDir);
            string destination = ModelPath(appDir, model);

            if (File.Exists(destination) && new FileInfo(destination).Length >= model.MinimumBytes)
            {
                report(0, "正在校验已有模型…");
                string existingHash = ComputeSha1(destination);
                if (existingHash.Equals(model.Sha1, StringComparison.OrdinalIgnoreCase))
                {
                    File.WriteAllText(destination + ".sha1", model.Sha1, Encoding.ASCII);
                    log(model.FileName + " 已存在并通过 SHA-1 校验。");
                    report(100, "模型已就绪。");
                    return;
                }
                log("已有模型校验不通过，将保留原文件，下载验证通过后再替换。");
            }

            string temp = destination + ".download-" + Guid.NewGuid().ToString("N");
            try
            {
                Exception lastError = null;
                for (int attempt = 1; attempt <= 2; attempt++)
                {
                    try
                    {
                        if (File.Exists(temp)) File.Delete(temp);
                        log("模型官方下载线路（第 " + attempt + " 次尝试）");
                        report(0, "正在下载 " + model.DisplayName + "…");
                        DownloadFile(model.Url, temp,
                            p => report(p, "正在下载模型：" + p + "%（" + model.SizeText + "）"), isCancelled);
                        if (new FileInfo(temp).Length < model.MinimumBytes)
                            throw new InvalidDataException("模型文件大小异常，未安装。");
                        report(100, "下载完成，正在校验模型…");
                        string hash = ComputeSha1(temp);
                        if (!hash.Equals(model.Sha1, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("模型 SHA-1 校验失败，未安装。");
                        InstallFile(temp, destination);
                        File.WriteAllText(destination + ".sha1", model.Sha1, Encoding.ASCII);
                        log(model.FileName + " 已安装并通过 SHA-1 校验：" + hash);
                        return;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        lastError = ex;
                        if (File.Exists(temp)) File.Delete(temp);
                        log("模型下载或校验失败：" + FriendlyNetworkMessage(ex));
                        if (attempt < 2) System.Threading.Thread.Sleep(1000);
                    }
                }
                throw new InvalidOperationException("模型下载或校验连续失败；原有文件没有被替换。", lastError);
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); }
                catch { }
            }
        }

        internal static string ParseExpectedHash(string checksumText, string fileName)
        {
            if (String.IsNullOrWhiteSpace(checksumText)) throw new InvalidDataException("校验文件为空。");
            foreach (string rawLine in checksumText.Replace("\r", "").Split('\n'))
            {
                string line = rawLine.Trim();
                Match match = Regex.Match(line, "^([0-9a-fA-F]{64})\\s+\\*?(.+)$");
                if (!match.Success) continue;
                string listedName = Path.GetFileName(match.Groups[2].Value.Trim());
                if (listedName.Equals(fileName, StringComparison.OrdinalIgnoreCase))
                    return match.Groups[1].Value.ToUpperInvariant();
            }
            throw new InvalidDataException("官方校验清单中没有找到 " + fileName + "。请稍后重试。");
        }

        internal static string ComputeSha256(string path)
        {
            using (var algorithm = SHA256.Create()) return ComputeHash(path, algorithm);
        }

        internal static string ComputeSha1(string path)
        {
            using (var algorithm = SHA1.Create()) return ComputeHash(path, algorithm);
        }

        private static string ComputeHash(string path, HashAlgorithm algorithm)
        {
            using (var stream = File.OpenRead(path))
            {
                byte[] hash = algorithm.ComputeHash(stream);
                var text = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash) text.Append(value.ToString("X2"));
                return text.ToString();
            }
        }

        internal static string ExtractNamedExecutable(string archivePath, string executableName, string outputDir)
        {
            string outputPath = Path.Combine(outputDir, Guid.NewGuid().ToString("N") + "-" + executableName);
            using (var archive = ZipFile.OpenRead(archivePath))
            {
                ZipArchiveEntry found = null;
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (!Path.GetFileName(entry.FullName).Equals(executableName, StringComparison.OrdinalIgnoreCase)) continue;
                    if (found != null) throw new InvalidDataException("压缩包中存在多个 " + executableName + "，已停止安装。");
                    found = entry;
                }
                if (found == null || found.Length <= 0) throw new InvalidDataException("压缩包中没有找到 " + executableName + "。");
                using (Stream input = found.Open())
                using (var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    input.CopyTo(output);
            }
            return outputPath;
        }

        internal static void ExtractWhisperRuntime(string archivePath, string outputDir)
        {
            Directory.CreateDirectory(outputDir);
            using (var archive = ZipFile.OpenRead(archivePath))
            {
                string prefix = null;
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (!Path.GetFileName(entry.FullName).Equals("whisper-cli.exe", StringComparison.OrdinalIgnoreCase)) continue;
                    prefix = entry.FullName.Substring(0, entry.FullName.Length - Path.GetFileName(entry.FullName).Length);
                    break;
                }
                if (prefix == null) throw new InvalidDataException("whisper.cpp 压缩包中没有找到 whisper-cli.exe。");

                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (!entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || entry.Length == 0) continue;
                    string name = Path.GetFileName(entry.FullName);
                    if (String.IsNullOrWhiteSpace(name)) continue;
                    if (!names.Add(name)) throw new InvalidDataException("whisper.cpp 压缩包中存在重复文件：" + name);
                    string destination = Path.Combine(outputDir, name);
                    using (Stream input = entry.Open())
                    using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        input.CopyTo(output);
                }
            }
        }

        internal static void ValidateExecutable(string path, string displayName)
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < 65536) throw new InvalidDataException(displayName + " 文件过小或不存在。");
            using (var stream = File.OpenRead(path))
            {
                if (stream.ReadByte() != 'M' || stream.ReadByte() != 'Z')
                    throw new InvalidDataException(displayName + " 不是有效的 Windows 可执行文件。");
            }
        }

        private static void DownloadFixedAndVerify(string url, string expectedSha256,
            string destination, Action<int> progress, Action<string> log, Func<bool> isCancelled)
        {
            Exception lastError = null;
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    if (File.Exists(destination)) File.Delete(destination);
                    log("下载线路：whisper.cpp 官方 GitHub Release（第 " + attempt + " 次尝试）");
                    DownloadFile(url, destination, progress, isCancelled);
                    string actual = ComputeSha256(destination);
                    if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException(Path.GetFileName(destination) + " 的 SHA-256 校验失败。");
                    log(Path.GetFileName(destination) + " SHA-256：" + actual);
                    return;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    lastError = ex;
                    if (File.Exists(destination)) File.Delete(destination);
                    log("whisper.cpp 运行库下载失败：" + FriendlyNetworkMessage(ex));
                    if (attempt < 2) System.Threading.Thread.Sleep(1000);
                }
            }
            throw new InvalidOperationException("whisper.cpp 运行库下载或校验连续失败。", lastError);
        }

        private static void DownloadAndVerify(DownloadSource[] sources, string fileName,
            string destination, Action<int> progress, Action<string> log, Func<bool> isCancelled)
        {
            Exception lastError = null;
            foreach (DownloadSource source in sources)
            {
                for (int attempt = 1; attempt <= 2; attempt++)
                {
                    try
                    {
                        ThrowIfCancelled(isCancelled);
                        if (File.Exists(destination)) File.Delete(destination);
                        log("下载线路：" + source.Name + "（第 " + attempt + " 次尝试）");
                        string checksums = DownloadText(source.ChecksumsUrl);
                        string expected = ParseExpectedHash(checksums, fileName);
                        DownloadFile(source.FileUrl, destination, progress, isCancelled);
                        string actual = ComputeSha256(destination);
                        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException(fileName + " 的 SHA-256 校验失败。");
                        log(fileName + " SHA-256：" + actual);
                        return;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        lastError = ex;
                        if (File.Exists(destination)) File.Delete(destination);
                        log(source.Name + "失败：" + FriendlyNetworkMessage(ex));
                        if (attempt < 2) System.Threading.Thread.Sleep(1000);
                    }
                }
            }
            throw new InvalidOperationException("所有官方下载线路均失败；未安装 " + fileName + "。" +
                (lastError == null ? "" : " 最后一次错误：" + FriendlyNetworkMessage(lastError)), lastError);
        }

        internal static string FriendlyNetworkMessage(Exception ex)
        {
            var web = ex as WebException;
            if (web != null)
            {
                var response = web.Response as HttpWebResponse;
                if (response != null)
                {
                    int code = (int)response.StatusCode;
                    if (code == 403) return "服务器拒绝访问（HTTP 403，可能是限流）";
                    if (code == 404) return "下载文件不存在（HTTP 404）";
                    return "HTTP " + code + " " + response.StatusDescription;
                }
                if (web.Status == WebExceptionStatus.Timeout) return "连接超时";
                if (web.Status == WebExceptionStatus.NameResolutionFailure) return "域名解析失败";
                if (web.Status == WebExceptionStatus.ConnectFailure) return "无法连接服务器";
            }
            return ex.Message;
        }

        private static string DownloadText(string url)
        {
            var request = CreateRequest(url);
            using (var response = (HttpWebResponse)request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                return reader.ReadToEnd();
        }

        private static void DownloadFile(string url, string destination,
            Action<int> progress, Func<bool> isCancelled)
        {
            ThrowIfCancelled(isCancelled);
            var request = CreateRequest(url);
            using (var response = (HttpWebResponse)request.GetResponse())
            using (Stream input = response.GetResponseStream())
            using (var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                long total = response.ContentLength;
                long received = 0;
                int lastPercent = -1;
                var buffer = new byte[128 * 1024];
                int count;
                while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ThrowIfCancelled(isCancelled);
                    output.Write(buffer, 0, count);
                    received += count;
                    int percent = total > 0 ? (int)Math.Min(100, received * 100L / total) : 0;
                    if (percent != lastPercent)
                    {
                        lastPercent = percent;
                        progress(percent);
                    }
                }
                if (received == 0) throw new InvalidDataException("服务器返回了空文件。");
                progress(100);
            }
        }

        private static void ThrowIfCancelled(Func<bool> isCancelled)
        {
            if (isCancelled != null && isCancelled()) throw new OperationCanceledException("操作已取消。");
        }

        private static HttpWebRequest CreateRequest(string url)
        {
            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("组件只能从 HTTPS 地址下载。");
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.AllowAutoRedirect = true;
            request.UserAgent = "local-subtitle-gui/0.1";
            request.Timeout = 30000;
            request.ReadWriteTimeout = 30000;
            return request;
        }

        private static void InstallFile(string source, string destination)
        {
            string incoming = destination + ".new";
            if (File.Exists(incoming)) File.Delete(incoming);
            File.Copy(source, incoming, true);
            try
            {
                if (File.Exists(destination))
                {
                    string backup = destination + ".backup";
                    if (File.Exists(backup)) File.Delete(backup);
                    File.Replace(incoming, destination, backup, true);
                    if (File.Exists(backup)) File.Delete(backup);
                }
                else File.Move(incoming, destination);
            }
            finally
            {
                if (File.Exists(incoming)) File.Delete(incoming);
            }
        }

        private static void ReplaceDirectory(string source, string destination)
        {
            string parent = Path.GetDirectoryName(destination);
            Directory.CreateDirectory(parent);
            string backup = destination + ".backup";
            if (Directory.Exists(backup)) Directory.Delete(backup, true);
            try
            {
                if (Directory.Exists(destination)) Directory.Move(destination, backup);
                Directory.Move(source, destination);
                if (Directory.Exists(backup)) Directory.Delete(backup, true);
            }
            catch
            {
                if (!Directory.Exists(destination) && Directory.Exists(backup)) Directory.Move(backup, destination);
                throw;
            }
        }
    }
}
