// Assets/GARTSS/Scripts/GARTSSClient.cs
// サーバーとの HTTP 通信を担当するクライアント
// QuestRealityCapture の既存ロジックはそのまま、このスクリプトを追加する

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace GARTSS
{
    public class GARTSSClient : MonoBehaviour
    {
        [Header("Server Settings")]
        [SerializeField] private string serverUrl = "http://192.168.1.100:8000";

        [Header("Status")]
        [SerializeField, ReadOnlyInInspector] private string sessionId = "";
        [SerializeField, ReadOnlyInInspector] private bool isInitialized = false;
        [SerializeField, ReadOnlyInInspector] private string lastStatus = "";

        public string SessionId => sessionId;
        public bool IsInitialized => isInitialized;
        public string ServerUrl
        {
            get => serverUrl;
            set => serverUrl = value;
        }

        public event Action<string> OnSessionInitialized;
        public event Action<CaptureResponse> OnCaptureComplete;
        public event Action<AnalyzeResponse> OnAnalyzeComplete;
        public event Action<string> OnError;

        // =============================================================
        //  Session Init
        // =============================================================

        /// <summary>
        /// カメラパラメータをサーバーに送りセッションを開始する。
        /// CameraPermissionManager からカメラ情報が取れた後に呼ぶ。
        /// </summary>
        public void InitSession(
            float[] poseTranslation,
            float[] poseRotation,
            float fx, float fy, float cx, float cy,
            int imageWidth, int imageHeight)
        {
            Debug.Log($"[GARTSS] InitSession called. URL={serverUrl}");
            var request = new SessionInitRequest
            {
                camera_characteristics = new CameraCharacteristicsData
                {
                    pose = new CameraPoseData
                    {
                        translation = poseTranslation,
                        rotation = poseRotation,
                    },
                    intrinsics = new CameraIntrinsicsData
                    {
                        fx = fx, fy = fy, cx = cx, cy = cy,
                    },
                },
                image_format = new ImageFormatData
                {
                    width = imageWidth,
                    height = imageHeight,
                },
            };

            StartCoroutine(PostJson<SessionInitResponse>(
                $"{serverUrl}/session/init",
                JsonUtility.ToJson(request),
                response =>
                {
                    sessionId = response.session_id;
                    isInitialized = true;
                    lastStatus = $"Session: {sessionId}";
                    Debug.Log($"[GARTSS] Session initialized: {sessionId}");
                    OnSessionInitialized?.Invoke(sessionId);
                }));
        }

        // =============================================================
        //  Capture
        // =============================================================

        /// <summary>
        /// RGB + Depth + HMDポーズをサーバーに送信してアライメントを実行。
        /// </summary>
        /// <param name="rgbPng">RGB画像のPNGバイト列 (Texture2D.EncodeToPNG())</param>
        /// <param name="depthRawBytes">Depth NDCバッファのfloat32バイナリ</param>
        /// <param name="depthDescriptor">Depth descriptor</param>
        /// <param name="hmdPoses">Depthタイムスタンプ前後のHMDポーズ配列</param>
        public void SendCapture(
            byte[] rgbPng,
            byte[] depthRawBytes,
            DepthDescriptorData depthDescriptor,
            HMDPoseData[] hmdPoses)
        {
            if (!isInitialized)
            {
                Debug.LogError("[GARTSS] Session not initialized");
                OnError?.Invoke("Session not initialized");
                return;
            }

            StartCoroutine(PostCapture(rgbPng, depthRawBytes, depthDescriptor, hmdPoses));
        }

        private IEnumerator PostCapture(
            byte[] rgbPng,
            byte[] depthRawBytes,
            DepthDescriptorData depthDescriptor,
            HMDPoseData[] hmdPoses)
        {
            var url = $"{serverUrl}/session/{sessionId}/capture";

            var form = new List<IMultipartFormSection>
            {
                // depth_raw (binary)
                new MultipartFormFileSection("depth_raw", depthRawBytes, "depth.raw", "application/octet-stream"),

                // depth_descriptor (JSON string)
                new MultipartFormDataSection("depth_descriptor", JsonUtility.ToJson(depthDescriptor)),

                // hmd_poses (JSON array string)
                // JsonUtility can't serialize arrays directly, so we wrap or build manually
                new MultipartFormDataSection("hmd_poses", SerializeHMDPoses(hmdPoses)),
            };

            // rgb_image (optional PNG)
            if (rgbPng != null && rgbPng.Length > 0)
            {
                form.Add(new MultipartFormFileSection("rgb_image", rgbPng, "rgb.png", "image/png"));
            }

            var request = UnityWebRequest.Post(url, form);
            request.timeout = 30;

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                var error = $"Capture failed: {request.error} - {request.downloadHandler?.text}";
                Debug.LogError($"[GARTSS] {error}");
                lastStatus = error;
                OnError?.Invoke(error);
                yield break;
            }

            var response = JsonUtility.FromJson<CaptureResponse>(request.downloadHandler.text);
            lastStatus = $"Aligned: coverage={response.coverage}%";
            Debug.Log($"[GARTSS] {lastStatus}");
            OnCaptureComplete?.Invoke(response);
        }

        // =============================================================
        //  Analyze (Phase 3)
        // =============================================================

        /// <summary>
        /// LLM + SAM による画像解析を要求 (Phase 3)
        /// </summary>
        public void RequestAnalyze()
        {
            if (!isInitialized) return;

            var body = "{}";
            StartCoroutine(PostJson<AnalyzeResponse>(
                $"{serverUrl}/session/{sessionId}/analyze",
                body,
                response =>
                {
                    Debug.Log($"[GARTSS] Analyze: {response.objects?.Length ?? 0} objects found");
                    OnAnalyzeComplete?.Invoke(response);
                }));
        }

        // =============================================================
        //  Depth Query
        // =============================================================

        /// <summary>
        /// 指定ピクセルのDepthと3D座標を取得
        /// </summary>
        public void QueryDepth(float u, float v, Action<DepthQueryResponse> onResult)
        {
            if (!isInitialized) return;
            StartCoroutine(GetDepth(u, v, onResult));
        }

        private IEnumerator GetDepth(float u, float v, Action<DepthQueryResponse> onResult)
        {
            var url = $"{serverUrl}/session/{sessionId}/depth?u={u}&v={v}";
            var request = UnityWebRequest.Get(url);
            request.timeout = 10;

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[GARTSS] Depth query failed: {request.error}");
                yield break;
            }

            var response = JsonUtility.FromJson<DepthQueryResponse>(request.downloadHandler.text);
            onResult?.Invoke(response);
        }

        // =============================================================
        //  Utilities
        // =============================================================

        /// <summary>
        /// HMDPoseData[] をJSON配列文字列にシリアライズ。
        /// JsonUtility は配列の直接シリアライズをサポートしないため手動で構築。
        /// </summary>
        private static string SerializeHMDPoses(HMDPoseData[] poses)
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < poses.Length; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(JsonUtility.ToJson(poses[i]));
            }
            sb.Append("]");
            return sb.ToString();
        }

        private IEnumerator PostJson<TResponse>(string url, string jsonBody, Action<TResponse> onSuccess)
        {
            Debug.Log($"[GARTSS] POST {url} body={jsonBody.Substring(0, Mathf.Min(200, jsonBody.Length))}");
            var request = new UnityWebRequest(url, "POST");
            var bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyBytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = 30;

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                var error = $"POST {url} failed: {request.error} - {request.downloadHandler?.text}";
                Debug.LogError($"[GARTSS] {error}");
                lastStatus = error;
                OnError?.Invoke(error);
                request.Dispose();
                yield break;
            }

            Debug.Log($"[GARTSS] POST {url} success: {request.downloadHandler.text}");
            var response = JsonUtility.FromJson<TResponse>(request.downloadHandler.text);
            onSuccess?.Invoke(response);
            request.Dispose();
        }
    }

    // Inspector上でreadonlyにするためのAttribute
    public class ReadOnlyInInspectorAttribute : PropertyAttribute { }

#if UNITY_EDITOR
    [UnityEditor.CustomPropertyDrawer(typeof(ReadOnlyInInspectorAttribute))]
    public class ReadOnlyDrawer : UnityEditor.PropertyDrawer
    {
        public override void OnGUI(Rect position, UnityEditor.SerializedProperty property, GUIContent label)
        {
            GUI.enabled = false;
            UnityEditor.EditorGUI.PropertyField(position, property, label, true);
            GUI.enabled = true;
        }
    }
#endif
}