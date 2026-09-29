using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace LocalSubtitleGui
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Version windows = Environment.OSVersion.Version;
            if (IsWindows7OrOlder(Environment.OSVersion.Platform, windows))
            {
                MessageBox.Show(
                    "本程序使用的 whisper.cpp 和 FFmpeg CPU组件面向 Windows 10/11 x64。\n\n" +
                    "Windows 7 不在当前测试和支持范围内。",
                    "系统版本不受支持", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Application.Run(new MainForm());
        }

        internal static bool IsWindows7OrOlder(PlatformID platform, Version windows)
        {
            return platform == PlatformID.Win32NT &&
                   (windows.Major < 6 || (windows.Major == 6 && windows.Minor < 2));
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly TextBox fileBox = new TextBox();
        private readonly Button browseFileButton = new Button();
        private readonly ComboBox languageBox = new ComboBox();
        private readonly ComboBox modelBox = new ComboBox();
        private readonly NumericUpDown threadsBox = new NumericUpDown();
        private readonly CheckBox srtBox = new CheckBox();
        private readonly CheckBox txtBox = new CheckBox();
        private readonly CheckBox vttBox = new CheckBox();
        private readonly CheckBox sameFolderBox = new CheckBox();
        private readonly TextBox folderBox = new TextBox();
        private readonly Button browseFolderButton = new Button();
        private readonly Button startButton = new Button();
        private readonly Button cancelButton = new Button();
        private readonly Button openButton = new Button();
        private readonly Button componentsButton = new Button();
        private readonly Button modelsButton = new Button();
        private readonly ProgressBar progress = new ProgressBar();
        private readonly Label status = new Label();
        private readonly Label savePreview = new Label();
        private readonly TextBox logBox = new TextBox();
        private readonly ToolTip tips = new ToolTip();
        private readonly SynchronizationContext ui;
        private volatile Process currentProcess;
        private volatile bool cancelling;
        private bool busy;
        private string lastOutputFolder;

        private static readonly Regex ProgressPattern =
            new Regex(@"progress\s*=\s*(\d+)%", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public MainForm(bool checkComponentsOnShown = true)
        {
            ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            Text = "本地音视频转字幕 · MVP v0.1";
            Icon = SystemIcons.Application;
            MinimumSize = new Size(820, 740);
            Size = new Size(980, 820);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(236, 242, 248);
            Padding = new Padding(14);
            Font = new Font("Microsoft YaHei UI", 10F);
            AllowDrop = true;
            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
            BuildUi();
            if (checkComponentsOnShown) Shown += (s, e) => CheckComponentsOnStartup();
        }

        private void BuildUi()
        {
            var root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(30, 20, 30, 18);
            root.BackColor = Color.White;
            root.ColumnCount = 1;
            root.RowCount = 14;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 47));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 4));
            Controls.Add(root);

            var title = new Label();
            title.Text = "本地音视频转字幕";
            title.Font = new Font("Microsoft YaHei UI", 20F, FontStyle.Bold);
            title.ForeColor = Color.FromArgb(24, 43, 67);
            title.Dock = DockStyle.Fill;
            root.Controls.Add(title, 0, 0);

            var subtitle = MakeLabel("文件只在本机处理。无需显卡，也不上传音视频内容。");
            subtitle.ForeColor = Color.FromArgb(90, 108, 132);
            root.Controls.Add(subtitle, 0, 1);
            root.Controls.Add(MakeLabel("音视频文件（可点击选择，也可拖放到下方区域）"), 0, 2);

            var fileRow = new TableLayoutPanel();
            fileRow.Dock = DockStyle.Fill;
            fileRow.ColumnCount = 2;
            fileRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            fileRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
            fileBox.Dock = DockStyle.Fill;
            fileBox.Margin = new Padding(0, 2, 0, 4);
            fileBox.BorderStyle = BorderStyle.FixedSingle;
            fileBox.BackColor = Color.FromArgb(250, 252, 255);
            fileBox.Multiline = true;
            fileBox.ScrollBars = ScrollBars.Vertical;
            fileBox.WordWrap = false;
            fileBox.AllowDrop = true;
            fileBox.DragEnter += OnDragEnter;
            fileBox.DragDrop += OnDragDrop;
            fileBox.TextChanged += (s, e) => UpdateSavePreview();
            fileRow.Controls.Add(fileBox, 0, 0);
            ConfigureButton(browseFileButton, "选择文件", Color.FromArgb(231, 238, 248), Color.FromArgb(33, 53, 77));
            browseFileButton.Dock = DockStyle.Fill;
            browseFileButton.Margin = new Padding(10, 18, 0, 20);
            browseFileButton.Click += (s, e) => BrowseInputFile();
            fileRow.Controls.Add(browseFileButton, 1, 0);
            root.Controls.Add(fileRow, 0, 3);

            var options = new TableLayoutPanel();
            options.Dock = DockStyle.Fill;
            options.ColumnCount = 3;
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24));
            options.RowCount = 1;
            options.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            ConfigureCombo(languageBox);
            languageBox.Items.AddRange(new object[] { "自动识别", "中文", "English", "日本語", "한국어", "粤语" });
            languageBox.SelectedIndex = 0;
            options.Controls.Add(CreateOptionField("识别语言", languageBox,
                new Padding(0, 0, 12, 4)), 0, 0);
            ConfigureCombo(modelBox);
            foreach (ModelDefinition model in ComponentInstaller.Models) modelBox.Items.Add(model.DisplayName);
            modelBox.SelectedIndex = 1;
            modelBox.SelectedIndexChanged += (s, e) => UpdateModelTip();
            options.Controls.Add(CreateOptionField("识别模型", modelBox,
                new Padding(4, 0, 12, 4)), 1, 0);
            threadsBox.Minimum = 1;
            threadsBox.Maximum = 64;
            threadsBox.Value = Math.Max(1, Math.Min(8, Environment.ProcessorCount > 2 ? Environment.ProcessorCount - 1 : Environment.ProcessorCount));
            threadsBox.TextAlign = HorizontalAlignment.Center;
            threadsBox.BorderStyle = BorderStyle.None;
            threadsBox.BackColor = Color.White;
            threadsBox.ForeColor = Color.FromArgb(33, 53, 77);
            options.Controls.Add(CreateOptionField("CPU线程", threadsBox,
                new Padding(4, 0, 0, 4)), 2, 0);
            root.Controls.Add(options, 0, 4);

            var formats = new FlowLayoutPanel();
            formats.Dock = DockStyle.Fill;
            formats.WrapContents = false;
            formats.Controls.Add(MakeFlowLabel("输出格式"));
            ConfigureCheckBox(srtBox, "SRT 字幕", true);
            ConfigureCheckBox(txtBox, "TXT 文字", true);
            ConfigureCheckBox(vttBox, "VTT 字幕", false);
            formats.Controls.Add(srtBox);
            formats.Controls.Add(txtBox);
            formats.Controls.Add(vttBox);
            var cpuHint = MakeFlowLabel("仅使用 CPU，不调用显卡");
            cpuHint.ForeColor = Color.FromArgb(29, 111, 131);
            cpuHint.Margin = new Padding(26, 8, 0, 0);
            formats.Controls.Add(cpuHint);
            root.Controls.Add(formats, 0, 5);

            sameFolderBox.Text = "保存到原文件所在目录";
            sameFolderBox.Checked = true;
            sameFolderBox.AutoSize = true;
            sameFolderBox.CheckedChanged += (s, e) => UpdateSaveMode();
            root.Controls.Add(sameFolderBox, 0, 6);

            var folderRow = new TableLayoutPanel();
            folderRow.Dock = DockStyle.Fill;
            folderRow.ColumnCount = 2;
            folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
            folderBox.Dock = DockStyle.Fill;
            folderBox.BorderStyle = BorderStyle.FixedSingle;
            folderBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "本地字幕");
            folderBox.TextChanged += (s, e) => UpdateSavePreview();
            folderRow.Controls.Add(folderBox, 0, 0);
            ConfigureButton(browseFolderButton, "选择目录", Color.FromArgb(231, 238, 248), Color.FromArgb(33, 53, 77));
            browseFolderButton.Margin = new Padding(8, 0, 0, 0);
            browseFolderButton.Click += (s, e) => BrowseOutputFolder();
            folderRow.Controls.Add(browseFolderButton, 1, 0);
            root.Controls.Add(folderRow, 0, 7);

            var buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.WrapContents = false;
            ConfigureButton(startButton, "开始生成字幕", Color.FromArgb(31, 103, 201), Color.White);
            startButton.Size = new Size(150, 38);
            startButton.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            startButton.Click += (s, e) => StartTranscription();
            ConfigureButton(cancelButton, "取消", Color.FromArgb(231, 238, 248), Color.FromArgb(33, 53, 77));
            cancelButton.Enabled = false;
            cancelButton.Click += (s, e) => CancelCurrentOperation();
            ConfigureButton(openButton, "打开保存目录", Color.FromArgb(231, 238, 248), Color.FromArgb(33, 53, 77));
            openButton.Size = new Size(140, 38);
            openButton.Click += (s, e) => OpenOutputFolder();
            ConfigureButton(componentsButton, "检查运行组件", Color.FromArgb(221, 243, 240), Color.FromArgb(16, 103, 96));
            componentsButton.Size = new Size(140, 38);
            componentsButton.Click += (s, e) => InstallComponentsFromButton();
            ConfigureButton(modelsButton, "打开模型目录", Color.FromArgb(245, 239, 224), Color.FromArgb(105, 77, 24));
            modelsButton.Size = new Size(132, 38);
            modelsButton.Click += (s, e) => OpenModelsFolder();
            buttons.Controls.Add(startButton);
            buttons.Controls.Add(cancelButton);
            buttons.Controls.Add(openButton);
            buttons.Controls.Add(componentsButton);
            buttons.Controls.Add(modelsButton);
            root.Controls.Add(buttons, 0, 8);

            progress.Dock = DockStyle.Fill;
            progress.Style = ProgressBarStyle.Continuous;
            root.Controls.Add(progress, 0, 9);
            status.Dock = DockStyle.Fill;
            status.TextAlign = ContentAlignment.MiddleLeft;
            status.ForeColor = Color.FromArgb(53, 73, 98);
            status.Text = "就绪";
            root.Controls.Add(status, 0, 10);
            savePreview.Dock = DockStyle.Fill;
            savePreview.TextAlign = ContentAlignment.MiddleLeft;
            savePreview.ForeColor = Color.FromArgb(29, 111, 131);
            savePreview.AutoEllipsis = true;
            root.Controls.Add(savePreview, 0, 11);

            logBox.Dock = DockStyle.Fill;
            logBox.Multiline = true;
            logBox.ReadOnly = true;
            logBox.ScrollBars = ScrollBars.Both;
            logBox.WordWrap = false;
            logBox.BorderStyle = BorderStyle.FixedSingle;
            logBox.BackColor = Color.FromArgb(249, 251, 254);
            logBox.Font = new Font("Consolas", 9.2F);
            root.Controls.Add(logBox, 0, 12);

            UpdateSaveMode();
            UpdateModelTip();
        }

        private static Label MakeLabel(string text)
        {
            var label = new Label();
            label.Text = text;
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.ForeColor = Color.FromArgb(53, 73, 98);
            return label;
        }

        private static Label MakeFlowLabel(string text)
        {
            var label = new Label();
            label.Text = text;
            label.AutoSize = false;
            label.Size = new Size(94, 36);
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Margin = new Padding(0, 2, 2, 0);
            label.ForeColor = Color.FromArgb(53, 73, 98);
            return label;
        }

        private static void ConfigureCombo(ComboBox box)
        {
            box.DropDownStyle = ComboBoxStyle.DropDownList;
            box.Dock = DockStyle.Fill;
            box.FlatStyle = FlatStyle.Flat;
            box.BackColor = Color.White;
            box.ForeColor = Color.FromArgb(33, 53, 77);
            box.Margin = new Padding(0);
        }

        private static Control CreateOptionField(string title, Control input, Padding margin)
        {
            var field = new TableLayoutPanel();
            field.Dock = DockStyle.Fill;
            field.Margin = margin;
            field.ColumnCount = 1;
            field.RowCount = 2;
            field.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            field.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

            Label label = MakeLabel(title);
            label.Margin = new Padding(0);
            field.Controls.Add(label, 0, 0);

            var border = new Panel();
            border.Dock = DockStyle.Top;
            border.Height = 31;
            border.Margin = new Padding(0);
            border.Padding = new Padding(1);
            border.BackColor = Color.White;
            border.Paint += (s, e) =>
            {
                using (var pen = new Pen(Color.FromArgb(145, 163, 185)))
                    e.Graphics.DrawRectangle(pen, 0, 0, border.ClientSize.Width - 1, border.ClientSize.Height - 1);
            };
            input.Dock = DockStyle.Fill;
            input.Margin = new Padding(0);
            border.Controls.Add(input);
            field.Controls.Add(border, 0, 1);
            return field;
        }

        private static void ConfigureCheckBox(CheckBox box, string text, bool value)
        {
            box.Text = text;
            box.Checked = value;
            box.AutoSize = true;
            box.Margin = new Padding(4, 10, 22, 0);
        }

        private static void ConfigureButton(Button button, string text, Color background, Color foreground)
        {
            button.Text = text;
            button.Size = new Size(112, 38);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = background;
            button.ForeColor = foreground;
            button.UseVisualStyleBackColor = false;
            button.Cursor = Cursors.Hand;
            button.Margin = new Padding(0, 2, 10, 0);
        }

        private void BrowseInputFile()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "选择音视频文件";
                dialog.Filter = "音视频文件|*.mp4;*.mkv;*.mov;*.avi;*.webm;*.mp3;*.wav;*.m4a;*.aac;*.flac;*.ogg|所有文件|*.*";
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog(this) == DialogResult.OK) SetInputFile(dialog.FileName);
            }
        }

        private void BrowseOutputFolder()
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择字幕和文字保存目录";
                if (Directory.Exists(folderBox.Text)) dialog.SelectedPath = folderBox.Text;
                if (dialog.ShowDialog(this) == DialogResult.OK) folderBox.Text = dialog.SelectedPath;
            }
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null && files.Length > 0) SetInputFile(files[0]);
        }

        private void SetInputFile(string path)
        {
            fileBox.Text = path;
            fileBox.SelectionStart = fileBox.TextLength;
            if (sameFolderBox.Checked) UpdateSavePreview();
        }

        private void UpdateSaveMode()
        {
            folderBox.Enabled = !sameFolderBox.Checked && !busy;
            browseFolderButton.Enabled = !sameFolderBox.Checked && !busy;
            UpdateSavePreview();
        }

        private void UpdateSavePreview()
        {
            string folder = ResolveOutputFolder(false);
            savePreview.Text = String.IsNullOrWhiteSpace(folder)
                ? "保存位置：请先选择有效文件或目录"
                : "字幕将保存到：" + folder;
        }

        private void UpdateModelTip()
        {
            if (modelBox.SelectedIndex < 0) return;
            ModelDefinition model = ComponentInstaller.Models[modelBox.SelectedIndex];
            tips.SetToolTip(modelBox, model.DisplayName + "。首次使用时按需下载并校验模型。");
        }

        private string ResolveOutputFolder(bool requireExistingInput)
        {
            if (sameFolderBox.Checked)
            {
                string file = fileBox.Text.Trim().Trim('"');
                if (String.IsNullOrWhiteSpace(file)) return null;
                if (requireExistingInput && !File.Exists(file)) return null;
                try { return Path.GetDirectoryName(Path.GetFullPath(file)); }
                catch { return null; }
            }
            string selected = folderBox.Text.Trim().Trim('"');
            if (String.IsNullOrWhiteSpace(selected)) return null;
            try { return Path.GetFullPath(selected); }
            catch { return null; }
        }

        private ModelDefinition SelectedModel()
        {
            int index = modelBox.SelectedIndex < 0 ? 0 : modelBox.SelectedIndex;
            return ComponentInstaller.Models[index];
        }

        private string SelectedLanguageCode()
        {
            switch (languageBox.SelectedIndex)
            {
                case 1: return "zh";
                case 2: return "en";
                case 3: return "ja";
                case 4: return "ko";
                case 5: return "yue";
                default: return "auto";
            }
        }

        private string[] SelectedExtensions()
        {
            var result = new System.Collections.Generic.List<string>();
            if (srtBox.Checked) result.Add(".srt");
            if (txtBox.Checked) result.Add(".txt");
            if (vttBox.Checked) result.Add(".vtt");
            return result.ToArray();
        }

        private void StartTranscription()
        {
            if (busy) return;
            string input = fileBox.Text.Trim().Trim('"');
            if (!File.Exists(input))
            {
                MessageBox.Show(this, "请先选择一个存在的音频或视频文件。", "缺少文件",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string[] extensions = SelectedExtensions();
            if (extensions.Length == 0)
            {
                MessageBox.Show(this, "请至少选择一种输出格式。", "缺少输出格式",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string outputFolder = ResolveOutputFolder(true);
            if (String.IsNullOrWhiteSpace(outputFolder))
            {
                MessageBox.Show(this, "请选择有效的保存目录。", "保存目录无效",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string appDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            string[] missing = ComponentInstaller.MissingRuntimeComponents(appDir);
            if (missing.Length > 0)
            {
                DialogResult answer = MessageBox.Show(this,
                    "首次使用需要下载 CPU运行组件：\n\n" + String.Join("\n", missing) +
                    "\n\n组件来自项目列出的官方来源，下载后会进行校验。是否继续？",
                    "安装运行组件", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (answer != DialogResult.Yes) return;
            }

            ModelDefinition model = SelectedModel();
            if (!ComponentInstaller.IsModelInstalled(appDir, model))
            {
                DialogResult answer = MessageBox.Show(this,
                    "所选模型尚未安装或需要重新校验：\n\n" + model.DisplayName +
                    "\n\n模型将从 whisper.cpp 官方列出的模型仓库下载。是否继续？",
                    "下载识别模型", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (answer != DialogResult.Yes) return;
            }

            Directory.CreateDirectory(outputFolder);
            lastOutputFolder = outputFolder;
            cancelling = false;
            logBox.Clear();
            progress.Value = 0;
            SetBusy(true);
            status.Text = "正在准备运行组件…";

            int threads = (int)threadsBox.Value;
            string language = SelectedLanguageCode();
            ThreadPool.QueueUserWorkItem(_ => PrepareAndRun(appDir, input, outputFolder,
                extensions, model, language, threads));
        }

        private void PrepareAndRun(string appDir, string input, string outputFolder,
            string[] extensions, ModelDefinition model, string language, int threads)
        {
            string tempDir = Path.Combine(appDir, ".runtime-temp", Guid.NewGuid().ToString("N"));
            try
            {
                if (ComponentInstaller.MissingRuntimeComponents(appDir).Length > 0)
                    ComponentInstaller.InstallRuntime(appDir, ReportInstallProgress, AppendLogThreadSafe, () => cancelling);
                if (!ComponentInstaller.IsModelInstalled(appDir, model))
                    ComponentInstaller.InstallModel(appDir, model, ReportInstallProgress, AppendLogThreadSafe, () => cancelling);
                if (cancelling) throw new OperationCanceledException();

                Directory.CreateDirectory(tempDir);
                string wav = Path.Combine(tempDir, "input.wav");
                string temporaryOutputBase = Path.Combine(tempDir, "result");
                string finalOutputBase = ChooseOutputBase(outputFolder, Path.GetFileNameWithoutExtension(input), extensions);

                PostUi(() => { progress.Value = 3; status.Text = "正在提取并转换音轨…"; });
                AppendLogThreadSafe("输入文件：" + input);
                AppendLogThreadSafe("识别模型：" + model.DisplayName);
                AppendLogThreadSafe("识别语言：" + language + "；CPU线程：" + threads);
                RunFfmpeg(appDir, input, wav);
                if (cancelling) throw new OperationCanceledException();

                PostUi(() => { progress.Value = 10; status.Text = "正在加载模型并识别…"; });
                RunWhisper(appDir, wav, temporaryOutputBase, model, language, threads, extensions);
                if (cancelling) throw new OperationCanceledException();

                string temporarySrt = temporaryOutputBase + ".srt";
                if (File.Exists(temporarySrt))
                {
                    int cueCountBefore;
                    int cueCountAfter;
                    string originalSrt = File.ReadAllText(temporarySrt, Encoding.UTF8);
                    string normalizedSrt = NormalizeSrtContent(originalSrt, 28,
                        out cueCountBefore, out cueCountAfter);
                    if (cueCountAfter > cueCountBefore)
                    {
                        File.WriteAllText(temporarySrt, normalizedSrt, new UTF8Encoding(false));
                        AppendLogThreadSafe("已优化SRT切分：" + cueCountBefore + " 条 → " + cueCountAfter + " 条");
                    }
                }

                foreach (string extension in extensions)
                {
                    string source = temporaryOutputBase + extension;
                    if (!File.Exists(source) || new FileInfo(source).Length == 0)
                        throw new InvalidDataException("whisper.cpp 未生成 " + extension + " 文件。");
                }

                int created = 0;
                foreach (string extension in extensions)
                {
                    string source = temporaryOutputBase + extension;
                    string destination = finalOutputBase + extension;
                    File.Move(source, destination);
                    created++;
                    AppendLogThreadSafe("已生成：" + destination);
                }
                int createdCount = created;
                PostUi(() =>
                {
                    progress.Value = 100;
                    status.Text = "完成！已生成 " + createdCount + " 个文件。";
                    SetBusy(false);
                });
            }
            catch (OperationCanceledException)
            {
                PostUi(() => { status.Text = "操作已取消；不会移动未完成的输出文件。"; SetBusy(false); });
            }
            catch (Exception ex)
            {
                AppendLogThreadSafe("错误：" + ex);
                PostUi(() => { status.Text = "处理失败：" + ex.Message; SetBusy(false); });
            }
            finally
            {
                currentProcess = null;
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); }
                catch { }
            }
        }

        private void RunFfmpeg(string appDir, string input, string wav)
        {
            string ffmpeg = ComponentInstaller.FfmpegExecutable(appDir);
            string args = BuildFfmpegArguments(input, wav);
            int code = RunProcess(ffmpeg, args, Path.GetDirectoryName(ffmpeg), false);
            if (cancelling) throw new OperationCanceledException();
            if (code != 0) throw new InvalidOperationException("FFmpeg 提取音轨失败，请查看运行记录。");
            if (!File.Exists(wav) || new FileInfo(wav).Length < 44)
                throw new InvalidDataException("FFmpeg 没有生成有效的音频文件。");
        }

        private void RunWhisper(string appDir, string wav, string outputBase,
            ModelDefinition model, string language, int threads, string[] extensions)
        {
            string whisper = ComponentInstaller.WhisperExecutable(appDir);
            string whisperDir = ComponentInstaller.WhisperDirectory(appDir);
            // Keep whisper.cpp arguments relative and ASCII-only. This avoids argv code-page
            // problems when the portable package is stored in a folder with Chinese characters.
            string relativeWav = MakeRelativePath(whisperDir, wav);
            string relativeOutputBase = MakeRelativePath(whisperDir, outputBase);
            string relativeModel = Path.Combine("..", "..", "models", model.FileName);
            string args = BuildWhisperArguments(relativeWav, relativeOutputBase,
                relativeModel, language, threads, extensions);
            int code = RunProcess(whisper, args, whisperDir, true);
            if (cancelling) throw new OperationCanceledException();
            if (code != 0) throw new InvalidOperationException("字幕识别失败，请查看运行记录。");
        }

        internal static string MakeRelativePath(string fromDirectory, string targetPath)
        {
            string from = Path.GetFullPath(fromDirectory);
            if (!from.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                from += Path.DirectorySeparatorChar;
            var fromUri = new Uri(from);
            var targetUri = new Uri(Path.GetFullPath(targetPath));
            if (!fromUri.Scheme.Equals(targetUri.Scheme, StringComparison.OrdinalIgnoreCase))
                return targetPath;
            return Uri.UnescapeDataString(fromUri.MakeRelativeUri(targetUri).ToString())
                .Replace('/', Path.DirectorySeparatorChar);
        }

        internal static string BuildFfmpegArguments(string input, string wav)
        {
            return "-hide_banner -loglevel warning -nostdin -y -i " + Quote(input) +
                   " -vn -ac 1 -ar 16000 -c:a pcm_s16le " + Quote(wav);
        }

        internal static string BuildWhisperArguments(string wav, string outputBase,
            string modelPath, string language, int threads, string[] extensions)
        {
            var args = new StringBuilder();
            args.Append("-m ").Append(Quote(modelPath));
            args.Append(" -f ").Append(Quote(wav));
            args.Append(" -of ").Append(Quote(outputBase));
            args.Append(" -l ").Append(String.IsNullOrWhiteSpace(language) ? "auto" : language);
            args.Append(" -t ").Append(Math.Max(1, threads));
            // English can be split safely at word boundaries by whisper.cpp. For
            // languages without spaces between every word, a fixed -ml value can cut
            // in the middle of a Chinese word or leave punctuation at the next cue.
            // Their SRT output is reflowed at punctuation after recognition instead.
            if (String.Equals(language, "en", StringComparison.OrdinalIgnoreCase))
                args.Append(" -ml 28 -sow");
            args.Append(" -pp -ng");
            foreach (string extension in extensions)
            {
                if (extension.Equals(".srt", StringComparison.OrdinalIgnoreCase)) args.Append(" -osrt");
                else if (extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)) args.Append(" -otxt");
                else if (extension.Equals(".vtt", StringComparison.OrdinalIgnoreCase)) args.Append(" -ovtt");
            }
            return args.ToString();
        }

        internal static string NormalizeSrtContent(string content, int maxChars,
            out int cueCountBefore, out int cueCountAfter)
        {
            cueCountBefore = 0;
            cueCountAfter = 0;
            if (String.IsNullOrWhiteSpace(content)) return content;
            maxChars = Math.Max(8, maxChars);

            string[] blocks = Regex.Split(content.Trim(), @"\r?\n\s*\r?\n");
            var output = new StringBuilder();
            int outputIndex = 1;
            foreach (string block in blocks)
            {
                string[] lines = block.Trim().Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                if (lines.Length < 3) continue;
                Match timeMatch = Regex.Match(lines[1],
                    @"^(\d{2,}):(\d{2}):(\d{2}),(\d{3})\s+-->\s+(\d{2,}):(\d{2}):(\d{2}),(\d{3})$");
                if (!timeMatch.Success) continue;

                long startMs = ParseSrtMilliseconds(timeMatch, 1);
                long endMs = ParseSrtMilliseconds(timeMatch, 5);
                if (endMs <= startMs) continue;
                string text = String.Join(" ", lines, 2, lines.Length - 2).Trim();
                if (text.Length == 0) continue;

                cueCountBefore++;
                string[] parts = SplitSubtitleText(text, maxChars);
                int totalWeight = 0;
                foreach (string part in parts) totalWeight += SubtitleWeight(part);
                int consumedWeight = 0;
                for (int i = 0; i < parts.Length; i++)
                {
                    long partStart = startMs + (endMs - startMs) * consumedWeight / totalWeight;
                    consumedWeight += SubtitleWeight(parts[i]);
                    long partEnd = i == parts.Length - 1
                        ? endMs
                        : startMs + (endMs - startMs) * consumedWeight / totalWeight;
                    if (output.Length > 0) output.Append("\r\n");
                    output.Append(outputIndex++).Append("\r\n")
                        .Append(FormatSrtMilliseconds(partStart)).Append(" --> ")
                        .Append(FormatSrtMilliseconds(partEnd)).Append("\r\n")
                        .Append(parts[i]).Append("\r\n");
                    cueCountAfter++;
                }
            }
            return output.Length == 0 ? content : output.ToString();
        }

        private static string[] SplitSubtitleText(string text, int maxChars)
        {
            var result = new System.Collections.Generic.List<string>();
            var current = new StringBuilder();
            string[] clauses = Regex.Split(text, @"(?<=[，。！？；：,.!?;:])");
            foreach (string rawClause in clauses)
            {
                string clause = rawClause.Trim();
                if (clause.Length == 0) continue;
                if (clause.Length <= maxChars)
                {
                    if (current.Length > 0 && current.Length + clause.Length > maxChars)
                    {
                        result.Add(current.ToString().Trim());
                        current.Clear();
                    }
                    current.Append(clause);
                    continue;
                }

                if (current.Length > 0)
                {
                    result.Add(current.ToString().Trim());
                    current.Clear();
                }
                int position = 0;
                while (position < clause.Length)
                {
                    int take = Math.Min(maxChars, clause.Length - position);
                    if (position + take < clause.Length)
                    {
                        int lastSpace = clause.LastIndexOf(' ', position + take - 1, take);
                        if (lastSpace >= position + maxChars / 2) take = lastSpace - position + 1;
                        if (take > 0 && Char.IsHighSurrogate(clause[position + take - 1])) take--;
                    }
                    string piece = clause.Substring(position, take).Trim();
                    if (piece.Length > 0) result.Add(piece);
                    position += take;
                }
            }
            if (current.Length > 0) result.Add(current.ToString().Trim());
            if (result.Count == 0) result.Add(text.Trim());
            return result.ToArray();
        }

        private static int SubtitleWeight(string text)
        {
            int count = Regex.Replace(text, @"\s+", "").Length;
            return Math.Max(1, count);
        }

        private static long ParseSrtMilliseconds(Match match, int groupOffset)
        {
            long hours = Int64.Parse(match.Groups[groupOffset].Value);
            long minutes = Int64.Parse(match.Groups[groupOffset + 1].Value);
            long seconds = Int64.Parse(match.Groups[groupOffset + 2].Value);
            long milliseconds = Int64.Parse(match.Groups[groupOffset + 3].Value);
            return (((hours * 60L) + minutes) * 60L + seconds) * 1000L + milliseconds;
        }

        private static string FormatSrtMilliseconds(long value)
        {
            value = Math.Max(0L, value);
            long hours = value / 3600000L;
            value %= 3600000L;
            long minutes = value / 60000L;
            value %= 60000L;
            long seconds = value / 1000L;
            long milliseconds = value % 1000L;
            return hours.ToString("00") + ":" + minutes.ToString("00") + ":" +
                   seconds.ToString("00") + "," + milliseconds.ToString("000");
        }

        private int RunProcess(string executable, string arguments, string workingDirectory, bool whisperProcess)
        {
            AppendLogThreadSafe(Path.GetFileName(executable) + " " + arguments);
            var info = new ProcessStartInfo(executable, arguments);
            info.WorkingDirectory = workingDirectory;
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.StandardOutputEncoding = Encoding.UTF8;
            info.StandardErrorEncoding = Encoding.UTF8;
            using (var process = new Process())
            {
                process.StartInfo = info;
                process.OutputDataReceived += (s, e) => HandleProcessLine(e.Data, whisperProcess);
                process.ErrorDataReceived += (s, e) => HandleProcessLine(e.Data, whisperProcess);
                if (cancelling) throw new OperationCanceledException();
                if (!process.Start()) throw new InvalidOperationException(Path.GetFileName(executable) + " 无法启动。");
                currentProcess = process;
                if (cancelling) KillProcessTree(process);
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();
                int code = process.ExitCode;
                currentProcess = null;
                return code;
            }
        }

        private void HandleProcessLine(string line, bool whisperProcess)
        {
            if (String.IsNullOrWhiteSpace(line)) return;
            AppendLogThreadSafe(line);
            if (!whisperProcess) return;
            Match match = ProgressPattern.Match(line);
            if (!match.Success) return;
            int value;
            if (!Int32.TryParse(match.Groups[1].Value, out value)) return;
            value = Math.Max(0, Math.Min(100, value));
            int mapped = 10 + (int)Math.Round(value * 0.9);
            PostUi(() =>
            {
                progress.Value = Math.Max(0, Math.Min(100, mapped));
                status.Text = "正在识别 " + value + "%";
            });
        }

        internal static int ParseWhisperProgress(string line)
        {
            if (String.IsNullOrWhiteSpace(line)) return -1;
            Match match = ProgressPattern.Match(line);
            int value;
            return match.Success && Int32.TryParse(match.Groups[1].Value, out value)
                ? Math.Max(0, Math.Min(100, value)) : -1;
        }

        internal static string ChooseOutputBase(string folder, string baseName, string[] extensions)
        {
            string safeName = SanitizeFileName(baseName);
            if (String.IsNullOrWhiteSpace(safeName)) safeName = "字幕";
            string candidate = Path.Combine(folder, safeName);
            int index = 2;
            while (AnyOutputExists(candidate, extensions))
            {
                candidate = Path.Combine(folder, safeName + " (" + index + ")");
                index++;
            }
            return candidate;
        }

        private static bool AnyOutputExists(string outputBase, string[] extensions)
        {
            foreach (string extension in extensions)
                if (File.Exists(outputBase + extension)) return true;
            return false;
        }

        internal static string SanitizeFileName(string value)
        {
            if (value == null) return "";
            foreach (char invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
            return value.Trim().TrimEnd('.');
        }

        internal static string Quote(string value)
        {
            if (value == null) return "\"\"";
            var result = new StringBuilder();
            result.Append('"');
            int slashCount = 0;
            foreach (char ch in value)
            {
                if (ch == '\\') { slashCount++; continue; }
                if (ch == '"')
                {
                    result.Append('\\', slashCount * 2 + 1);
                    result.Append('"');
                    slashCount = 0;
                    continue;
                }
                result.Append('\\', slashCount);
                slashCount = 0;
                result.Append(ch);
            }
            result.Append('\\', slashCount * 2);
            result.Append('"');
            return result.ToString();
        }

        private void CancelCurrentOperation()
        {
            if (!busy) return;
            cancelling = true;
            cancelButton.Enabled = false;
            status.Text = "正在取消…";
            Process process = currentProcess;
            if (process != null) KillProcessTree(process);
        }

        private static void KillProcessTree(Process process)
        {
            try
            {
                if (process.HasExited) return;
                var info = new ProcessStartInfo("taskkill.exe", "/PID " + process.Id + " /T /F");
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                using (Process killer = Process.Start(info))
                    if (killer != null) killer.WaitForExit(5000);
            }
            catch
            {
                try { if (!process.HasExited) process.Kill(); }
                catch { }
            }
        }

        private void InstallComponentsFromButton()
        {
            if (busy) return;
            string appDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            string[] missing = ComponentInstaller.MissingRuntimeComponents(appDir);
            if (missing.Length == 0)
            {
                MessageBox.Show(this, "whisper.cpp CPU运行库和 FFmpeg 已经就绪。\n\n识别模型会在首次使用对应模型时按需下载。",
                    "运行组件完整", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            DialogResult answer = MessageBox.Show(this,
                "需要安装：\n\n" + String.Join("\n", missing) + "\n\n是否从项目列出的官方来源下载并校验？",
                "检查运行组件", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;
            cancelling = false;
            progress.Value = 0;
            SetBusy(true);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    ComponentInstaller.InstallRuntime(appDir, ReportInstallProgress, AppendLogThreadSafe, () => cancelling);
                    PostUi(() => { progress.Value = 100; status.Text = "运行组件安装完成。"; SetBusy(false); });
                }
                catch (OperationCanceledException)
                {
                    PostUi(() => { status.Text = "组件安装已取消。"; SetBusy(false); });
                }
                catch (Exception ex)
                {
                    AppendLogThreadSafe("错误：" + ex);
                    PostUi(() => { status.Text = "组件安装失败：" + ex.Message; SetBusy(false); });
                }
            });
        }

        private void CheckComponentsOnStartup()
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            string[] missing = ComponentInstaller.MissingRuntimeComponents(appDir);
            if (missing.Length == 0) return;
            status.Text = "缺少运行组件，可点击“检查运行组件”安装。";
        }

        private void OpenOutputFolder()
        {
            string folder = lastOutputFolder ?? ResolveOutputFolder(false);
            if (String.IsNullOrWhiteSpace(folder)) return;
            Directory.CreateDirectory(folder);
            Process.Start("explorer.exe", Quote(folder));
        }

        private void OpenModelsFolder()
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            string folder = ComponentInstaller.ModelsDirectory(appDir);
            Directory.CreateDirectory(folder);
            Process.Start("explorer.exe", Quote(folder));
        }

        private void ReportInstallProgress(int value, string message)
        {
            PostUi(() =>
            {
                progress.Value = Math.Max(0, Math.Min(100, value));
                status.Text = message;
            });
        }

        private void SetBusy(bool value)
        {
            busy = value;
            startButton.Enabled = !value;
            browseFileButton.Enabled = !value;
            fileBox.Enabled = !value;
            languageBox.Enabled = !value;
            modelBox.Enabled = !value;
            threadsBox.Enabled = !value;
            srtBox.Enabled = !value;
            txtBox.Enabled = !value;
            vttBox.Enabled = !value;
            sameFolderBox.Enabled = !value;
            folderBox.Enabled = !value && !sameFolderBox.Checked;
            browseFolderButton.Enabled = !value && !sameFolderBox.Checked;
            componentsButton.Enabled = !value;
            modelsButton.Enabled = !value;
            cancelButton.Enabled = value;
        }

        private void AppendLogThreadSafe(string message)
        {
            if (String.IsNullOrWhiteSpace(message)) return;
            PostUi(() =>
            {
                logBox.AppendText(message + Environment.NewLine);
                logBox.SelectionStart = logBox.TextLength;
                logBox.ScrollToCaret();
            });
        }

        private void PostUi(Action action)
        {
            if (IsDisposed) return;
            ui.Post(_ => { if (!IsDisposed) action(); }, null);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (busy)
            {
                DialogResult answer = MessageBox.Show(this, "任务仍在运行，确定要取消并退出吗？",
                    "确认退出", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes) { e.Cancel = true; return; }
                cancelling = true;
                Process process = currentProcess;
                if (process != null) KillProcessTree(process);
            }
            base.OnFormClosing(e);
        }
    }
}
