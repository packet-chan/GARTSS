// Assets/GARTSS/Scripts/CaptureButton.cs
// Aボタン: キャプチャ → Analyze → AR配置
// Bボタン: ARコンテンツをクリア

using UnityEngine;

namespace GARTSS
{
    public class CaptureButton : MonoBehaviour
    {
        [SerializeField] private CaptureOrchestrator orchestrator;
        [SerializeField] private GARTSSClient client;
        [SerializeField] private ARContentPlacer placer;

        private bool waitingForCapture = false;

        private void Update()
        {
            if (OVRInput.GetDown(OVRInput.Button.One))
            {
                if (!client.IsInitialized)
                {
                    Debug.LogWarning("[CaptureButton] Session not yet initialized");
                    return;
                }

                Debug.Log("[CaptureButton] Starting capture");
                waitingForCapture = true;
                orchestrator.StartCapture();
            }

            if (OVRInput.GetDown(OVRInput.Button.Two))
            {
                if (placer != null)
                {
                    placer.ClearAll();
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
            if (waitingForCapture)
            {
                waitingForCapture = false;
                Debug.Log("[CaptureButton] Capture done, requesting analyze");
                client.RequestAnalyze();
            }
        }

        private void OnAnalyzeComplete(AnalyzeResponse response)
        {
            Debug.Log($"[CaptureButton] Analyze: {response.objects?.Length ?? 0} objects");

            if (response.objects != null && response.objects.Length > 0 && placer != null)
            {
                placer.PlaceDetectedObjects(response);
            }
        }

        private void OnError(string error)
        {
            Debug.LogError($"[CaptureButton] {error}");
            waitingForCapture = false;
        }
    }
}