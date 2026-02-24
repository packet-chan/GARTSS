// Assets/GARTSS/Scripts/CaptureButton.cs
// コントローラーのボタンでキャプチャを開始する

using UnityEngine;

namespace GARTSS
{
    public class CaptureButton : MonoBehaviour
    {
        [SerializeField] private CaptureOrchestrator orchestrator;
        [SerializeField] private GARTSSClient client;

        [Header("Debug UI (Optional)")]
        [SerializeField] private TMPro.TextMeshProUGUI statusText;

        private void Update()
        {
            // 右コントローラーの A ボタン
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
                orchestrator.StartCapture();
            }
        }

        private void OnEnable()
        {
            if (client != null)
            {
                client.OnCaptureComplete += OnCaptureComplete;
                client.OnError += OnError;
            }
        }

        private void OnDisable()
        {
            if (client != null)
            {
                client.OnCaptureComplete -= OnCaptureComplete;
                client.OnError -= OnError;
            }
        }

        private void OnCaptureComplete(CaptureResponse response)
        {
            SetStatus($"Coverage: {response.coverage}%");
        }

        private void OnError(string error)
        {
            SetStatus($"Error: {error}");
        }

        private void SetStatus(string text)
        {
            if (statusText != null)
            {
                statusText.text = text;
            }
        }
    }
}
