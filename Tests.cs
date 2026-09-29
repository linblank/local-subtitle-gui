using System;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace LocalSubtitleGui
{
    internal static class ArgumentTests
    {
        private static int failures;

        [STAThread]
        private static int Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Check(Program.IsWindows7OrOlder(PlatformID.Win32NT, new Version(6, 1)), "Windows 7 检测");
            Check(!Program.IsWindows7OrOlder(PlatformID.Win32NT, new Version(10, 0)), "Windows 10 检测");

            Check(ComponentInstaller.Models.Length == 3, "提供三档模型");
            Check(ComponentInstaller.Models[0].FileName == "ggml-base.bin", "Base 模型映射");
            Check(ComponentInstaller.Models[1].FileName == "ggml-small.bin", "Small 模型映射");
            Check(ComponentInstaller.Models[2].FileName == "ggml-medium.bin", "Medium 模型映射");
            foreach (ModelDefinition model in ComponentInstaller.Models)
            {
                Check(model.Url.StartsWith("https://huggingface.co/ggerganov/whisper.cpp/", StringComparison.Ordinal), "模型使用项目列出的 HTTPS 仓库");
                Check(model.Sha1.Length == 40, "模型 SHA-1 长度");
            }

            string whisperArgs = MainForm.BuildWhisperArguments(
                @"C:\temp path\input.wav", @"C:\output path\result",
                @"C:\models\ggml-small.bin", "zh", 6,
                new[] { ".srt", ".txt", ".vtt" });
            Check(whisperArgs.Contains("-m \"C:\\models\\ggml-small.bin\""), "模型参数加引号");
            Check(whisperArgs.Contains("-f \"C:\\temp path\\input.wav\""), "输入参数加引号");
            Check(whisperArgs.Contains("-l zh") && whisperArgs.Contains("-t 6"), "语言和线程参数");
            Check(!whisperArgs.Contains("-ml") && !whisperArgs.Contains("-sow"), "中文不使用硬字符切分");
            Check(whisperArgs.Contains("-pp") && whisperArgs.Contains("-ng"), "进度和强制 CPU 参数");
            Check(whisperArgs.Contains("-osrt") && whisperArgs.Contains("-otxt") && whisperArgs.Contains("-ovtt"), "三种输出参数");
            Check(!whisperArgs.Contains("cuda") && !whisperArgs.Contains("vulkan"), "不包含未测试的显卡参数");

            string englishArgs = MainForm.BuildWhisperArguments(
                "input.wav", "result", "model.bin", "en", 4, new[] { ".srt" });
            Check(englishArgs.Contains("-ml 28") && englishArgs.Contains("-sow"), "英文按单词切分");

            string longSrt = "1\r\n00:00:00,000 --> 00:00:26,000\r\n" +
                "那么有几个注意点给大家讲一下,我们这个网址的话它每次会进行改变,每次都不一样,所以每次生成的话都会是一个新的网址。\r\n";
            int cuesBefore;
            int cuesAfter;
            string normalized = MainForm.NormalizeSrtContent(longSrt, 28, out cuesBefore, out cuesAfter);
            Check(cuesBefore == 1 && cuesAfter >= 3, "程序端兜底拆分超长SRT");
            Check(normalized.Contains("00:00:00,000 -->") && normalized.Contains("--> 00:00:26,000"), "兜底切分保留首尾时间");
            Check(!normalized.Contains("注意点给大家讲一下,我们这个网址"), "优先在标点位置切分");
            Check(!Regex.IsMatch(normalized, @"(?m)^[，。！？；：,.!?;:]"), "切分后标点不出现在字幕开头");

            string ffmpegArgs = MainForm.BuildFfmpegArguments(@"D:\中文 视频.mp4", @"C:\temp\input.wav");
            Check(ffmpegArgs.Contains("-vn -ac 1 -ar 16000 -c:a pcm_s16le"), "FFmpeg 转换为 16kHz 单声道 PCM");
            Check(ffmpegArgs.Contains("\"D:\\中文 视频.mp4\""), "FFmpeg 中文空格路径加引号");

            Check(MainForm.ParseWhisperProgress("whisper_print_progress_callback: progress =  37%") == 37, "识别进度解析");
            Check(MainForm.ParseWhisperProgress("普通日志") == -1, "非进度日志忽略");
            Check(MainForm.ParseWhisperProgress("progress = 999%") == 100, "进度上限");
            Check(MainForm.SanitizeFileName("a:b") == "a_b", "文件名清理");
            string relative = MainForm.MakeRelativePath(@"E:\中文目录\runtime\whisper", @"E:\中文目录\models\ggml-base.bin");
            Check(relative == @"..\..\models\ggml-base.bin", "中文安装路径转换为 ASCII 相对参数");

            string temp = Path.Combine(Path.GetTempPath(), "local-subtitle-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                string first = MainForm.ChooseOutputBase(temp, "采访", new[] { ".srt", ".txt" });
                Check(first == Path.Combine(temp, "采访"), "首次输出沿用原文件名");
                File.WriteAllText(first + ".srt", "test", Encoding.UTF8);
                string second = MainForm.ChooseOutputBase(temp, "采访", new[] { ".srt", ".txt" });
                Check(second == Path.Combine(temp, "采访 (2)"), "已有文件时避免覆盖");

                string hashFile = Path.Combine(temp, "abc.txt");
                File.WriteAllBytes(hashFile, Encoding.ASCII.GetBytes("abc"));
                Check(ComponentInstaller.ComputeSha256(hashFile) == "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD", "SHA-256 计算");
                Check(ComponentInstaller.ComputeSha1(hashFile) == "A9993E364706816ABA3E25717850C26C9CD0D89D", "SHA-1 计算");

                string checksum = "1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef *" + ComponentInstaller.FfmpegArchiveName;
                Check(ComponentInstaller.ParseExpectedHash(checksum, ComponentInstaller.FfmpegArchiveName).StartsWith("123456"), "官方校验清单解析");

                string zipPath = Path.Combine(temp, "runtime.zip");
                using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                {
                    WriteEntry(zip, "Release/whisper-cli.exe", new byte[70000]);
                    WriteEntry(zip, "Release/whisper.dll", new byte[] { 1, 2, 3 });
                    WriteEntry(zip, "README.txt", new byte[] { 4 });
                }
                string extracted = Path.Combine(temp, "runtime");
                ComponentInstaller.ExtractWhisperRuntime(zipPath, extracted);
                Check(File.Exists(Path.Combine(extracted, "whisper-cli.exe")), "提取 whisper-cli");
                Check(File.Exists(Path.Combine(extracted, "whisper.dll")), "提取运行库 DLL");
                Check(!File.Exists(Path.Combine(extracted, "README.txt")), "只提取运行目录文件");
            }
            finally
            {
                try { Directory.Delete(temp, true); }
                catch { }
            }

            using (var form = new MainForm(false))
            {
                Check(form.Text.Contains("v0.1"), "窗口版本号");
                Button start = GetField<Button>(form, "startButton");
                Button components = GetField<Button>(form, "componentsButton");
                ComboBox models = GetField<ComboBox>(form, "modelBox");
                Check(start.Text == "开始生成字幕", "主按钮文字明确");
                Check(components.Text == "检查运行组件", "组件按钮文字明确");
                Check(models.Items.Count == 3 && models.SelectedIndex == 1, "默认推荐 Small 模型");
                Check(models.Parent is Panel && models.Parent.Padding.All == 1, "模型下拉框具有可见边框");
                Check(GetField<ComboBox>(form, "languageBox").Parent is Panel, "语言下拉框具有可见边框");
                Check(GetField<NumericUpDown>(form, "threadsBox").Parent is Panel, "CPU线程输入框具有可见边框");
                Check(GetField<TextBox>(form, "fileBox").Multiline && GetField<TextBox>(form, "fileBox").Height >= 50, "音视频拖放框高度充足");
                Check(GetField<CheckBox>(form, "srtBox").Checked, "默认生成 SRT");
                Check(GetField<CheckBox>(form, "txtBox").Checked, "默认生成 TXT");
                Check(!GetField<CheckBox>(form, "vttBox").Checked, "VTT 默认不勾选");

                form.Show();
                Application.DoEvents();
                form.PerformLayout();
                using (var bitmap = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                    bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui-preview-v0.1.png"));
                }
                form.Hide();
            }

            if (failures == 0)
            {
                Console.WriteLine("参数、模型、目录、防覆盖、校验和界面测试通过");
                return 0;
            }
            Console.Error.WriteLine("共有 " + failures + " 项测试失败");
            return 1;
        }

        private static T GetField<T>(object instance, string name) where T : class
        {
            FieldInfo field = instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            return field == null ? null : field.GetValue(instance) as T;
        }

        private static void WriteEntry(ZipArchive zip, string name, byte[] data)
        {
            ZipArchiveEntry entry = zip.CreateEntry(name);
            using (Stream stream = entry.Open()) stream.Write(data, 0, data.Length);
        }

        private static void Check(bool condition, string name)
        {
            if (condition) return;
            failures++;
            Console.Error.WriteLine("失败：" + name);
        }
    }
}
