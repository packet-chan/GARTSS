// Assets/GARTSS/Scripts/CaptureButton.cs
// Aボタン: キャプチャ → Analyze → Cube配置
// Bボタン: ARコンテンツをクリア

using UnityEngine;

namespace GARTSS
{
    public class CaptureButton : MonoBehaviour
    {
        [SerializeField] private CaptureOrchestrator orchestrator;
        [SerializeField] private GARTSSClient client;
        [SerializeField] private ARContentPlacer placer;

        [Header("Analyze Settings")]
        [Tooltip("Gemini APIに送るタスク指示")]
        [SerializeField] private string analyzeTask = "Detect the coffee machine tray";

        [Header("Debug UI (Optional)")]
        [SerializeField] private TMPro.TextMeshProUGUI statusText;

        private bool waitingForCapture = false;

        private void Update()
        {
            // Aボタン: キャプチャ開始
            if (OVRInput.GetDown(OVRInput.Button.One))
            {
                if (!client.IsInitialized)
                {
                    Debug.LogWarning("[CaptureButton] Session not yet initialized");
                    SetStatus("Waiting for session...");
                    return;
                }

                Debug.Log("[CaptureButton] Starting capture");
                SetStatus("Capturing...");
                waitingForCapture = true;
                orchestrator.StartCapture();
            }

            // Bボタン: ARコンテンツをクリア
            if (OVRInput.GetDown(OVRInput.Button.Two))
            {
                if (placer != null)
                {
                    placer.ClearAll();
                    SetStatus("Cleared");
                }
            }
        }

        private void OnEnable()
        {
            if (client != null)
            {
                client.OnCaptureComplete += OnCaptureComplete;
                client.OnAnalyzeComplete += OnAnalyzeComplete;
                client.OnError += OnError;
            }
        }

        private void OnDisable()
        {
            if (client != null)
            {
                client.OnCaptureComplete -= OnCaptureComplete;
                client.OnAnalyzeComplete -= OnAnalyzeComplete;
                client.OnError -= OnError;
            }
        }

        private void OnCaptureComplete(CaptureResponse response)
        {
            SetStatus($"Coverage: {response.coverage}%");

            // 最後のキャプチャが完了したら自動的にAnalyzeを実行
            if (waitingForCapture)
            {
                waitingForCapture = false;
                Debug.Log($"[CaptureButton] Capture done, starting analyze: {analyzeTask}");
                SetStatus("Analyzing...");
                client.RequestAnalyze(analyzeTask);
            }
        }

        private void OnAnalyzeComplete(AnalyzeResponse response)
        {
            Debug.Log($"[CaptureButton] Analyze result: {response.objects?.Length ?? 0} objects");

            if (response.objects != null && response.objects.Length > 0)
            {
                if (placer != null)
                {
                    placer.PlaceDetectedObjects(response);
                }

                var obj = response.objects[0];
                if (obj.center_3d != null && obj.center_3d.Length == 3)
                {
                    SetStatus($"Found: {obj.name} at ({obj.center_3d[0]:F2}, {obj.center_3d[1]:F2}, {obj.center_3d[2]:F2})");
                }
                else
                {
                    SetStatus($"Found: {obj.name} (no 3D)");
                }
            }
            else
            {
                SetStatus("No objects found");
            }
        }

        private void OnError(string error)
        {
            SetStatus($"Error: {error}");
            waitingForCapture = false;
        }

        private void SetStatus(string text)
        {
            Debug.Log($"[CaptureButton] Status: {text}");
            if (statusText != null)
            {
                statusText.text = text;
            }
        }
    }
}