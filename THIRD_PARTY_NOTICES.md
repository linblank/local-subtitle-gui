# 第三方运行组件

本项目的轻量 Release 不包含下列第三方二进制。用户确认后，程序从列出的 HTTPS 地址下载独立组件并进行校验。

## whisper.cpp

- 项目：<https://github.com/ggml-org/whisper.cpp>
- CPU运行库：<https://github.com/ggml-org/whisper.cpp/releases>
- 当前固定构建：`v1.9.4` 对应 Windows 构建 `b5130`
- Windows x64 文件：`whisper-bin-x64.zip`
- 许可证：MIT License
- 许可证文件：<https://github.com/ggml-org/whisper.cpp/blob/master/LICENSE>

程序安装前核对代码中固定记录的 SHA-256。v0.1 不下载或分发 CUDA 运行库。

## Whisper GGML 模型

- whisper.cpp模型说明：<https://github.com/ggml-org/whisper.cpp/blob/master/models/README.md>
- 项目列出的模型仓库：<https://huggingface.co/ggerganov/whisper.cpp>
- 上游 Whisper：<https://github.com/openai/whisper>

v0.1提供 Base、Small、Medium 多语言模型。程序根据 whisper.cpp 官方模型表公布的 SHA-1 验证文件：

| 文件 | SHA-1 |
| --- | --- |
| `ggml-base.bin` | `465707469ff3a37a2b9b8d8f89f2f99de7299dac` |
| `ggml-small.bin` | `55356645c2b361a969dfd0ef2c5a50d530afd8d5` |
| `ggml-medium.bin` | `fd9727b6e1217c2f614f9b698455c4ffd82463b4` |

模型文件不包含在轻量 Release 中，用户选择模型后按需下载。

## FFmpeg

- yt-dlp FFmpeg构建项目：<https://github.com/yt-dlp/FFmpeg-Builds>
- 上游构建项目：<https://github.com/BtbN/FFmpeg-Builds>
- FFmpeg官方网站：<https://ffmpeg.org/>
- FFmpeg法律与许可说明：<https://ffmpeg.org/legal.html>

程序下载 Windows x64 GPL静态构建，并使用发布方提供的 SHA-256 校验清单。FFmpeg以及构建中包含的库拥有各自许可证。

## 免责声明

本项目与 ggml-org、OpenAI、Hugging Face、FFmpeg、yt-dlp 或 BtbN 的维护者没有隶属或官方合作关系。各项目名称只用于说明兼容性、来源和许可证。

