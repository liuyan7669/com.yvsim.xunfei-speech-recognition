# Xunfei Speech Recognition

通过讯飞 IAT WebSocket 接口将调用方提供的音频转为文字。唯一业务入口是静态类
`Yvsim.XunfeiSpeechRecognition.XunfeiSpeechRecognizer`；不启动麦克风，不依赖录音或文件包。

## 安装与依赖

当前源码版本为 **1.0.0**。Unity 最低版本为 **2021.3**。安装本地源码时：

1. 在 Package Manager 选择 **Add package from disk**，选择本包 `com.yvsim.xunfei-speech-recognition/package.json`。
2. 等待 Unity 完成依赖解析和编译。也可将本包完整目录放入工程的 `Packages/`，作为 Embedded Package。

本包不依赖 Inspector Groups 或其他 Yvsim 包。所需 Unity 依赖由 `package.json` 声明并由 UPM 解析。
本包提供本地源码、Git 和 OpenUPM 安装方式；固定版本使用 `1.0.0`，具体步骤见下文。

依赖 Newtonsoft Json **3.2.2**、Unity Audio 与 uGUI。uGUI 用于示例；运行时业务仅需调用静态 API。

## 凭据与最小调用

```csharp
using Yvsim.XunfeiSpeechRecognition;
using UnityEngine;

public sealed class SpeechExample : MonoBehaviour
{
    private void OnEnable()
    {
        XunfeiSpeechRecognizer.RecognitionCompleted += OnCompleted;
        XunfeiSpeechRecognizer.RecognitionFailed += OnFailed;
    }

    private void OnDisable()
    {
        XunfeiSpeechRecognizer.RecognitionCompleted -= OnCompleted;
        XunfeiSpeechRecognizer.RecognitionFailed -= OnFailed;
    }

    public bool Recognize(byte[] wav, string appId, string apiKey, string apiSecret)
    {
        XunfeiSpeechRecognizer.SetRuntimeCredentials(appId, apiKey, apiSecret);
        return XunfeiSpeechRecognizer.RecognizeWav(wav);
    }

    private void OnCompleted(XunfeiRecognitionResult result) => Debug.Log(result.Text);
    private void OnFailed(XunfeiRecognitionError error) => Debug.LogError(error.Message);
}
```

必须提供已启用相应服务的 APPID、APIKey 和 APISecret。凭据只保存在进程内存，`ClearRuntimeCredentials()` 清除；
默认语言 `zh_cn`、口音 `mandarin`，可通过 SetRuntimeCredentials 后两个参数设置。
请勿把长期 APISecret 写入场景、资源、日志或发布给不可信客户端。当前包在客户端签名，未实现后端短时凭据接口；
面向公开客户端的生产方案应由项目增加可信后端鉴权流程。

## 输入方式

| API | 输入 |
| --- | --- |
| `RecognizeWav(byte[])` | 完整 PCM16 RIFF/WAVE |
| `RecognizePcm16(byte[], sampleRate, channels, bitsPerSample)` | 不带文件头的 PCM16 Little Endian |
| `RecognizeAudioClip(clip, sourceFramesPerUpdate)` | 可加载且支持 GetData 的 AudioClip，分帧降混/重采样 |
| `StartStreaming(...)` + `WritePcm16(...)` + `CompleteStreaming()` | 外部连续 PCM16 块 |

WAV、裸 PCM 和流式输入格式必须为 **16 kHz、单声道、16-bit**；每个 PCM 块字节数是 2 的倍数。
单次最多 **60 秒 / 1,920,000 字节** PCM。AudioClip 入口可转换常见源采样率/声道，每帧读取量默认 8192，范围 256～16384。
AudioClip 数据需要可读且未销毁，压缩/流式资源应先确认 GetData 支持。

```csharp
if (XunfeiSpeechRecognizer.StartStreaming())
{
    XunfeiSpeechRecognizer.WritePcm16(firstChunk);
    XunfeiSpeechRecognizer.WritePcm16(lastChunk, isLast: true);
}
```

也可以在所有块写入后单独调用 `CompleteStreaming()`。流式会话等待首块后才连接；空会话不能正常完成。
WritePcm16 在返回前复制指定数据，可随后复用源数组。已写入数据总量同样受 60 秒上限限制。
发送器按 40ms/1280 字节分帧；连接后避免长时间断供，服务端可能按静音或会话策略提前结束。

## 状态、事件和线程

所有公开 API 在 Unity 主线程调用。全局只接受一个会话；bool 只表示请求接受情况，不等于识别成功。
未接受的请求会返回 false/报告日志；参数错误可能抛出 ArgumentException，业务应先验证输入。

状态依次包含 `Idle`、`WaitingForAudio`、`PreparingAudio`、`Connecting`、`SendingAudio`、`AwaitingFinalResult`，实际路径由输入方式决定。
`IsRecognizing`、`IsStreaming`、`State`、`CurrentText` 和 `LastResult` 用于展示状态。
`RecognitionStarted`、`StatusChanged`、`PartialTextChanged` 报告过程；`RecognitionCompleted`、`RecognitionCanceled`、`RecognitionFailed` 报告终态。
`Cancel()` 取消当前会话，并使迟到的网络回调失效。对象停用时解除自身静态事件订阅；不要替其他调用方取消会话。

结果含 Text、ServiceSessionId、InputKind、PcmByteCount 和 AudioDurationSeconds。
错误含 Message、ServiceSessionId 和 ServiceCode；`-1` 表示本地转换、超时或传输错误，其余为服务端码。
连接超时为 20 秒，最终结果等待为 30 秒；实际服务能力、配额和网络由运行环境决定。

## 平台与示例

后端覆盖 Editor、Windows、Android 与 WebGL。原生使用 ClientWebSocket，WebGL 使用包内 `.jslib` 与浏览器 WebSocket。
`IsAvailable` 表示有对应实现，不代表网络、账号或平台权限已经验证。

从 Package Manager 导入 **UGUI Usage**，打开 `Xunfei Speech Recognition UGUI.unity`。
场景没有预置真实凭据；输入运行时凭据后可测试 WAV、PCM、AudioClip 和 40ms 实时块，包含取消和统一终态展示。
内置音频为程序生成的 2 秒测试音，只验证格式/请求链路，通常没有有意义的识别文字；真实转写请换成有授权的语音。
示例仅依赖本包与其 Unity 依赖；legacy Text 已绑定示例自带的完整 Noto 中文字体，支持中文识别结果和运行状态显示。
字体按示例 `Fonts/OFL-Noto-CJK.txt` 的 SIL Open Font License 1.1 分发，不依赖 Noto TMP 包。

与录音包组合时，在业务层把录音结果 Data 传给 RecognizeWav，或把 PCM 块传给 WritePcm16；
不要让两个业务包形成生产程序集依赖。

## 测试与许可

包内 EditMode 测试覆盖协议签名/响应、WAV/PCM 校验、分帧转换、并发/取消及终态事件。
安装 Test Framework 并将 `com.yvsim.xunfei-speech-recognition` 加入项目 manifest 的 `testables` 后运行。
测试不需要真实服务凭据，也不代表真实账号识别、网络或设备验收通过。
代码使用 MIT License。

## 从 GitHub 安装

仓库：[com.yvsim.xunfei-speech-recognition](https://github.com/liuyan7669/com.yvsim.xunfei-speech-recognition)。仓库名与 UPM 包名一致。

在 Unity Package Manager 选择 Add package from git URL，输入：

```text
https://github.com/liuyan7669/com.yvsim.xunfei-speech-recognition.git#main
```

此地址跟随 main 分支。需要固定 1.0.0 源码时，将 `#main` 改为 `#v1.0.0`；Unity 官方依赖仍按 package.json 解析。

## 从 OpenUPM 安装

在 Unity 的 Project Settings > Package Manager 中添加 Scoped Registry：

- Name：OpenUPM
- URL：`https://package.openupm.com`
- Scope：`com.yvsim`

然后在 Package Manager 中通过 Add package by name 输入 `com.yvsim.xunfei-speech-recognition`，版本填 `1.0.0`。也可从 My Registries 中选择本包。声明的 Yvsim 依赖由 OpenUPM 解析，Unity 官方依赖仍由 Unity 注册表提供。

如果已安装 OpenUPM CLI，可在工程根目录执行：

```sh
openupm add com.yvsim.xunfei-speech-recognition@1.0.0
```

[OpenUPM 包页面](https://openupm.com/packages/com.yvsim.xunfei-speech-recognition/)
