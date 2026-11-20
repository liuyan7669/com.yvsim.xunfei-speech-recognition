using System;
using System.Collections;
using Yvsim.XunfeiSpeechRecognition;
using UnityEngine;
using UnityEngine.UI;

namespace Yvsim.XunfeiSpeechRecognition.Samples
{
    /// <summary>
    /// 可通过 Package Manager 导入的独立 UGUI 示例。
    /// 示例只消费调用方提供的 WAV、PCM16 或 AudioClip，不依赖录音包或文件包。
    /// </summary>
    public sealed class XunfeiSpeechRecognitionUGUIUsage : MonoBehaviour
    {
        private const int ProtocolFrameBytes = 1280;
        private const float ProtocolFrameIntervalSeconds = 0.04f;

        [Header("Runtime Credentials")]
        [Tooltip("运行时填写讯飞应用 APP ID 的输入框。")]
        [SerializeField] private InputField appIdInput;
        [Tooltip("运行时填写讯飞 API Key 的输入框。")]
        [SerializeField] private InputField apiKeyInput;
        [Tooltip("运行时填写讯飞 API Secret 的输入框。")]
        [SerializeField] private InputField apiSecretInput;
        [Tooltip("读取三个凭据输入框并应用到当前运行会话的按钮。")]
        [SerializeField] private Button applyCredentialsButton;
        [Tooltip("清除当前会话凭据并清空三个凭据输入框的按钮。")]
        [SerializeField] private Button clearCredentialsButton;

        [Header("Input Samples")]
        [Tooltip("供 WAV、PCM16 和流式识别示例使用的 WAV 字节资源；需为 16 kHz、单声道、16 位 PCM 格式。")]
        [SerializeField] private TextAsset pcm16WavSample;
        [Tooltip("供 AudioClip 识别示例使用的音频资源，识别前会转换为协议要求的音频格式。")]
        [SerializeField] private AudioClip audioClipSample;
        [Tooltip("将示例 WAV 文件作为完整音频提交识别的按钮。")]
        [SerializeField] private Button recognizeWavButton;
        [Tooltip("提取示例 WAV 中的 PCM16 数据并提交完整音频识别的按钮。")]
        [SerializeField] private Button recognizePcm16Button;
        [Tooltip("将所绑定的 AudioClip 转换并提交识别的按钮。")]
        [SerializeField] private Button recognizeAudioClipButton;
        [Tooltip("把示例音频按 40 毫秒音频块逐步送入流式识别会话的按钮。")]
        [SerializeField] private Button streamPcm16Button;
        [Tooltip("通知当前流式识别会话音频输入结束，并等待最终识别结果的按钮。")]
        [SerializeField] private Button completeStreamButton;
        [Tooltip("取消当前由本示例发起的识别会话的按钮。")]
        [SerializeField] private Button cancelButton;
        [Tooltip("清空界面中的识别文本和结果详情的按钮，不修改输入音频。")]
        [SerializeField] private Button clearOutputButton;

        [Header("Output")]
        [Tooltip("显示识别器当前连接、发送音频或等待结果等状态的文本。")]
        [SerializeField] private Text stateText;
        [Tooltip("显示识别操作提示和错误信息的文本。")]
        [SerializeField] private Text statusText;
        [Tooltip("显示识别过程中持续更新的临时文本结果。")]
        [SerializeField] private Text partialText;
        [Tooltip("显示识别完成后的最终文本结果。")]
        [SerializeField] private Text finalText;
        [Tooltip("显示最终识别结果的会话和音频信息的文本。")]
        [SerializeField] private Text resultDetailsText;

        private Coroutine streamSampleCoroutine;
        private byte[] cachedWavBytes;
        private bool isStartingOwnedSession;
        private bool startRequestObservedStarted;
        private bool startRequestObservedTerminal;
        private bool ownsRecognitionSession;

        private void Awake()
        {
            cachedWavBytes = pcm16WavSample != null ? pcm16WavSample.bytes : null;
            ClearOutput();
        }

        private void OnEnable()
        {
            BindControls();
            XunfeiSpeechRecognizer.RecognitionStarted += HandleRecognitionStarted;
            XunfeiSpeechRecognizer.StatusChanged += HandleStatusChanged;
            XunfeiSpeechRecognizer.PartialTextChanged += HandlePartialTextChanged;
            XunfeiSpeechRecognizer.RecognitionCompleted += HandleRecognitionCompleted;
            XunfeiSpeechRecognizer.RecognitionCanceled += HandleRecognitionCanceled;
            XunfeiSpeechRecognizer.RecognitionFailed += HandleRecognitionFailed;

            SetStatus(XunfeiSpeechRecognizer.IsAvailable
                ? "Enter runtime credentials, then choose an audio input."
                : "The current platform is outside this package's support range.");
            UpdateControls();
        }

        private void OnDisable()
        {
            UnbindControls();
            StopStreamCoroutine();
            bool ownsInFlightStart = isStartingOwnedSession &&
                startRequestObservedStarted && !startRequestObservedTerminal;
            if ((ownsRecognitionSession || ownsInFlightStart) &&
                XunfeiSpeechRecognizer.IsRecognizing)
            {
                XunfeiSpeechRecognizer.Cancel();
            }
            ownsRecognitionSession = false;
            isStartingOwnedSession = false;
            startRequestObservedStarted = false;
            startRequestObservedTerminal = false;

            XunfeiSpeechRecognizer.RecognitionStarted -= HandleRecognitionStarted;
            XunfeiSpeechRecognizer.StatusChanged -= HandleStatusChanged;
            XunfeiSpeechRecognizer.PartialTextChanged -= HandlePartialTextChanged;
            XunfeiSpeechRecognizer.RecognitionCompleted -= HandleRecognitionCompleted;
            XunfeiSpeechRecognizer.RecognitionCanceled -= HandleRecognitionCanceled;
            XunfeiSpeechRecognizer.RecognitionFailed -= HandleRecognitionFailed;
        }

        private void OnDestroy()
        {
            // Defensive fallback for unusual editor teardown ordering.
            UnbindControls();
        }

        private void Update()
        {
            UpdateControls();
        }

        public void ApplyCredentials()
        {
            try
            {
                XunfeiSpeechRecognizer.SetRuntimeCredentials(
                    appIdInput != null ? appIdInput.text : string.Empty,
                    apiKeyInput != null ? apiKeyInput.text : string.Empty,
                    apiSecretInput != null ? apiSecretInput.text : string.Empty);
            }
            catch (Exception exception)
            {
                SetStatus("Credentials were not accepted: " + exception.Message);
            }
        }

        public void ClearCredentials()
        {
            XunfeiSpeechRecognizer.ClearRuntimeCredentials();
            if (appIdInput != null) appIdInput.SetTextWithoutNotify(string.Empty);
            if (apiKeyInput != null) apiKeyInput.SetTextWithoutNotify(string.Empty);
            if (apiSecretInput != null) apiSecretInput.SetTextWithoutNotify(string.Empty);
        }

        public void RecognizeBundledWav()
        {
            ClearRecognitionText();
            if (!TryGetSampleWav(out byte[] wavBytes))
            {
                return;
            }

            if (!TryStartOwnedSession(() => XunfeiSpeechRecognizer.RecognizeWav(wavBytes)))
            {
                SetStatus("The complete WAV request was not accepted. Check credentials, format, and session state.");
            }
        }

        public void RecognizeBundledPcm16()
        {
            ClearRecognitionText();
            if (!TryGetSamplePcm(out byte[] wavBytes, out int dataOffset, out int dataCount))
            {
                return;
            }

            byte[] pcmBytes = new byte[dataCount];
            Buffer.BlockCopy(wavBytes, dataOffset, pcmBytes, 0, dataCount);
            if (!TryStartOwnedSession(() => XunfeiSpeechRecognizer.RecognizePcm16(pcmBytes)))
            {
                SetStatus("The complete PCM16 request was not accepted. Check credentials, format, and session state.");
            }
        }

        public void RecognizeAssignedAudioClip()
        {
            ClearRecognitionText();
            if (audioClipSample == null)
            {
                SetStatus("No AudioClip is assigned. Assign a readable, loaded clip to the sample component.");
                return;
            }

            if (!TryStartOwnedSession(() =>
                    XunfeiSpeechRecognizer.RecognizeAudioClip(audioClipSample)))
            {
                SetStatus("The AudioClip request was not accepted. Check credentials and session state.");
            }
        }

        public void StreamBundledPcm16()
        {
            ClearRecognitionText();
            StopStreamCoroutine();
            if (!TryGetSamplePcm(out byte[] wavBytes, out int dataOffset, out int dataCount))
            {
                return;
            }

            bool accepted = TryStartOwnedSession(() => XunfeiSpeechRecognizer.StartStreaming());
            if (!accepted)
            {
                SetStatus("The streaming PCM16 session was not accepted. Check credentials and session state.");
                return;
            }

            if (!isActiveAndEnabled || !ownsRecognitionSession ||
                !XunfeiSpeechRecognizer.IsStreaming)
            {
                return;
            }

            streamSampleCoroutine = StartCoroutine(
                FeedSamplePcm(wavBytes, dataOffset, dataCount));
        }

        public void CompleteStreaming()
        {
            StopStreamCoroutine();
            if (!ownsRecognitionSession || !XunfeiSpeechRecognizer.IsStreaming)
            {
                SetStatus("This sample does not own a streaming session to complete.");
                return;
            }

            if (!XunfeiSpeechRecognizer.CompleteStreaming() && ownsRecognitionSession)
            {
                SetStatus("There is no non-empty streaming PCM16 input to complete.");
            }
        }

        public void CancelRecognition()
        {
            StopStreamCoroutine();
            if (!ownsRecognitionSession)
            {
                SetStatus("This sample does not own a recognition session to cancel.");
                return;
            }

            if (!XunfeiSpeechRecognizer.Cancel())
            {
                SetStatus("There is no recognition session to cancel.");
            }
        }

        public void ClearOutput()
        {
            SetStatus("Recognition has not started. All outcomes are returned through static events.");
            if (partialText != null) partialText.text = "Partial text:\n(none)";
            if (finalText != null) finalText.text = "Final text:\n(none)";
            if (resultDetailsText != null)
            {
                resultDetailsText.text =
                    "Result details: input kind, PCM byte count, duration, and service session ID.";
            }
        }

        private IEnumerator FeedSamplePcm(byte[] wavBytes, int dataOffset, int dataCount)
        {
            int written = 0;
            while (written < dataCount)
            {
                int count = Mathf.Min(ProtocolFrameBytes, dataCount - written);
                if (!XunfeiSpeechRecognizer.WritePcm16(
                        wavBytes,
                        dataOffset + written,
                        count))
                {
                    if (ownsRecognitionSession)
                    {
                        SetStatus("The streaming PCM16 write was not accepted. Sample delivery stopped.");
                    }
                    streamSampleCoroutine = null;
                    yield break;
                }

                written += count;
                yield return new WaitForSecondsRealtime(ProtocolFrameIntervalSeconds);
            }

            streamSampleCoroutine = null;
            if (!XunfeiSpeechRecognizer.CompleteStreaming() && ownsRecognitionSession)
            {
                SetStatus("PCM was delivered, but the complete-input request was not accepted.");
            }
        }

        private void BindControls()
        {
            if (applyCredentialsButton != null) applyCredentialsButton.onClick.AddListener(ApplyCredentials);
            if (clearCredentialsButton != null) clearCredentialsButton.onClick.AddListener(ClearCredentials);
            if (recognizeWavButton != null) recognizeWavButton.onClick.AddListener(RecognizeBundledWav);
            if (recognizePcm16Button != null) recognizePcm16Button.onClick.AddListener(RecognizeBundledPcm16);
            if (recognizeAudioClipButton != null) recognizeAudioClipButton.onClick.AddListener(RecognizeAssignedAudioClip);
            if (streamPcm16Button != null) streamPcm16Button.onClick.AddListener(StreamBundledPcm16);
            if (completeStreamButton != null) completeStreamButton.onClick.AddListener(CompleteStreaming);
            if (cancelButton != null) cancelButton.onClick.AddListener(CancelRecognition);
            if (clearOutputButton != null) clearOutputButton.onClick.AddListener(ClearOutput);
        }

        private void UnbindControls()
        {
            if (applyCredentialsButton != null) applyCredentialsButton.onClick.RemoveListener(ApplyCredentials);
            if (clearCredentialsButton != null) clearCredentialsButton.onClick.RemoveListener(ClearCredentials);
            if (recognizeWavButton != null) recognizeWavButton.onClick.RemoveListener(RecognizeBundledWav);
            if (recognizePcm16Button != null) recognizePcm16Button.onClick.RemoveListener(RecognizeBundledPcm16);
            if (recognizeAudioClipButton != null) recognizeAudioClipButton.onClick.RemoveListener(RecognizeAssignedAudioClip);
            if (streamPcm16Button != null) streamPcm16Button.onClick.RemoveListener(StreamBundledPcm16);
            if (completeStreamButton != null) completeStreamButton.onClick.RemoveListener(CompleteStreaming);
            if (cancelButton != null) cancelButton.onClick.RemoveListener(CancelRecognition);
            if (clearOutputButton != null) clearOutputButton.onClick.RemoveListener(ClearOutput);
        }

        private void UpdateControls()
        {
            bool busy = XunfeiSpeechRecognizer.IsRecognizing;
            bool ready = XunfeiSpeechRecognizer.IsAvailable &&
                XunfeiSpeechRecognizer.HasRuntimeCredentials;

            if (applyCredentialsButton != null) applyCredentialsButton.interactable = !busy;
            if (clearCredentialsButton != null) clearCredentialsButton.interactable = !busy;
            if (appIdInput != null) appIdInput.interactable = !busy;
            if (apiKeyInput != null) apiKeyInput.interactable = !busy;
            if (apiSecretInput != null) apiSecretInput.interactable = !busy;
            if (recognizeWavButton != null) recognizeWavButton.interactable = ready && !busy;
            if (recognizePcm16Button != null) recognizePcm16Button.interactable = ready && !busy;
            if (recognizeAudioClipButton != null) recognizeAudioClipButton.interactable = ready && !busy;
            if (streamPcm16Button != null) streamPcm16Button.interactable = ready && !busy;
            if (completeStreamButton != null)
            {
                completeStreamButton.interactable = ownsRecognitionSession &&
                    XunfeiSpeechRecognizer.IsStreaming;
            }
            if (cancelButton != null)
            {
                cancelButton.interactable = ownsRecognitionSession && busy;
            }

            if (stateText != null)
            {
                stateText.text = string.Format(
                    "State: {0}    Platform: {1}    Runtime credentials: {2}",
                    XunfeiSpeechRecognizer.State,
                    XunfeiSpeechRecognizer.IsAvailable ? "available" : "unavailable",
                    XunfeiSpeechRecognizer.HasRuntimeCredentials ? "set" : "not set");
            }
        }

        private void HandleRecognitionStarted()
        {
            if (isStartingOwnedSession)
            {
                startRequestObservedStarted = true;
            }

            SetStatus("Recognition started. The bool only means accepted; wait for a terminal event.");
        }

        private void HandleStatusChanged(string message)
        {
            SetStatus(message);
        }

        private void HandlePartialTextChanged(string text)
        {
            if (partialText != null)
            {
                partialText.text = "Partial text:\n" +
                    (string.IsNullOrEmpty(text) ? "(none)" : text);
            }
        }

        private void HandleRecognitionCompleted(XunfeiRecognitionResult result)
        {
            if (isStartingOwnedSession)
            {
                startRequestObservedTerminal = true;
            }

            ownsRecognitionSession = false;
            StopStreamCoroutine();
            if (finalText != null)
            {
                finalText.text = "Final text:\n" +
                    (result == null || string.IsNullOrEmpty(result.Text)
                        ? "(the service returned empty text)"
                        : result.Text);
            }

            if (resultDetailsText != null && result != null)
            {
                resultDetailsText.text = string.Format(
                    "Result: {0} | {1:N0} PCM bytes | {2:0.00}s | SID {3}",
                    result.InputKind,
                    result.PcmByteCount,
                    result.AudioDurationSeconds,
                    string.IsNullOrEmpty(result.ServiceSessionId)
                        ? "(none)"
                        : result.ServiceSessionId);
            }

            SetStatus("Recognition completed; RecognitionCompleted was received.");
        }

        private void HandleRecognitionCanceled()
        {
            if (isStartingOwnedSession)
            {
                startRequestObservedTerminal = true;
            }

            ownsRecognitionSession = false;
            StopStreamCoroutine();
            SetStatus("Recognition was canceled; RecognitionCanceled was received.");
        }

        private void HandleRecognitionFailed(XunfeiRecognitionError error)
        {
            if (isStartingOwnedSession)
            {
                startRequestObservedTerminal = true;
            }

            ownsRecognitionSession = false;
            StopStreamCoroutine();
            SetStatus(string.Format(
                "Recognition failed: code={0}, {1}",
                error != null ? error.ServiceCode : -1,
                error != null ? error.Message : "unknown error"));
            if (resultDetailsText != null && error != null)
            {
                resultDetailsText.text = "Failure SID: " +
                    (string.IsNullOrEmpty(error.ServiceSessionId)
                        ? "(none)"
                        : error.ServiceSessionId);
            }
        }

        private void ClearRecognitionText()
        {
            if (partialText != null) partialText.text = "Partial text:\n(waiting for service)";
            if (finalText != null) finalText.text = "Final text:\n(waiting for terminal event)";
            if (resultDetailsText != null) resultDetailsText.text = "Result details: waiting.";
        }

        private bool TryStartOwnedSession(Func<bool> startRequest)
        {
            isStartingOwnedSession = true;
            startRequestObservedStarted = false;
            startRequestObservedTerminal = false;
            bool accepted;
            try
            {
                accepted = startRequest();
            }
            finally
            {
                isStartingOwnedSession = false;
            }

            if (accepted && startRequestObservedStarted &&
                !startRequestObservedTerminal && isActiveAndEnabled &&
                XunfeiSpeechRecognizer.IsRecognizing)
            {
                ownsRecognitionSession = true;
            }

            return accepted;
        }

        private bool TryGetSampleWav(out byte[] wavBytes)
        {
            wavBytes = cachedWavBytes;
            if (wavBytes != null && wavBytes.Length > 0)
            {
                return true;
            }

            SetStatus("The sample scene has no PCM16 WAV TextAsset assigned.");
            return false;
        }

        private bool TryGetSamplePcm(
            out byte[] wavBytes,
            out int dataOffset,
            out int dataCount)
        {
            dataOffset = 0;
            dataCount = 0;
            if (!TryGetSampleWav(out wavBytes))
            {
                return false;
            }

            if (TryFindPcm16Data(wavBytes, out dataOffset, out dataCount))
            {
                return true;
            }

            SetStatus("The sample WAV must be 16 kHz, 16-bit, mono PCM WAV.");
            return false;
        }

        private void StopStreamCoroutine()
        {
            if (streamSampleCoroutine == null)
            {
                return;
            }

            StopCoroutine(streamSampleCoroutine);
            streamSampleCoroutine = null;
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = "Runtime information:\n" + (message ?? string.Empty);
            }
        }

        private static bool TryFindPcm16Data(
            byte[] wavBytes,
            out int dataOffset,
            out int dataCount)
        {
            dataOffset = 0;
            dataCount = 0;
            if (wavBytes == null || wavBytes.Length < 44 ||
                !Matches(wavBytes, 0, "RIFF") ||
                !Matches(wavBytes, 8, "WAVE"))
            {
                return false;
            }

            bool validFormat = false;
            int offset = 12;
            while (offset <= wavBytes.Length - 8)
            {
                int chunkLength = ReadInt32LittleEndian(wavBytes, offset + 4);
                int chunkDataOffset = offset + 8;
                if (chunkLength < 0 ||
                    (long)chunkDataOffset + chunkLength > wavBytes.Length)
                {
                    return false;
                }

                if (Matches(wavBytes, offset, "fmt ") && chunkLength >= 16)
                {
                    validFormat = ReadInt16LittleEndian(wavBytes, chunkDataOffset) == 1 &&
                        ReadInt16LittleEndian(wavBytes, chunkDataOffset + 2) ==
                            XunfeiSpeechRecognizer.RequiredChannels &&
                        ReadInt32LittleEndian(wavBytes, chunkDataOffset + 4) ==
                            XunfeiSpeechRecognizer.RequiredSampleRate &&
                        ReadInt16LittleEndian(wavBytes, chunkDataOffset + 14) ==
                            XunfeiSpeechRecognizer.RequiredBitsPerSample;
                }
                else if (Matches(wavBytes, offset, "data") && chunkLength > 0)
                {
                    dataOffset = chunkDataOffset;
                    dataCount = chunkLength;
                }

                offset = chunkDataOffset + chunkLength + (chunkLength & 1);
            }

            return validFormat && dataCount > 0 && (dataCount & 1) == 0;
        }

        private static bool Matches(byte[] data, int offset, string value)
        {
            if (offset < 0 || offset > data.Length - value.Length)
            {
                return false;
            }

            for (int index = 0; index < value.Length; index++)
            {
                if (data[offset + index] != (byte)value[index])
                {
                    return false;
                }
            }

            return true;
        }

        private static short ReadInt16LittleEndian(byte[] data, int offset)
        {
            return (short)(data[offset] | (data[offset + 1] << 8));
        }

        private static int ReadInt32LittleEndian(byte[] data, int offset)
        {
            return data[offset] |
                (data[offset + 1] << 8) |
                (data[offset + 2] << 16) |
                (data[offset + 3] << 24);
        }
    }
}
