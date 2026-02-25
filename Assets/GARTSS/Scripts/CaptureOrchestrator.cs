// Assets/GARTSS/Scripts/CaptureOrchestrator.cs

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using RealityLog.Depth;
using RealityLog.Camera;
using System.IO;

namespace GARTSS
{
    public class CaptureOrchestrator : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GARTSSClient client;
        [SerializeField] private ImageReaderSurfaceProvider imageReaderProvider;
        [SerializeField] private CameraSessionManager cameraSessionManager;
        [SerializeField] private RGBCameraCapture rgbCamera;

        [Header("Capture Settings")]
        [Tooltip("キャプチャ間隔 (秒)")]
        [SerializeField] private float captureInterval = 0.5f;
        [Tooltip("最大キャプチャ枚数")]
        [SerializeField] private int maxCaptures = 3;

        [Header("Camera Info (SessionInit時に自動設定)")]
        [SerializeField] private float[] poseTranslation;
        [SerializeField] private float[] poseRotation;
        [SerializeField] private float fx, fy, cx, cy;
        [SerializeField] private int imageWidth = 1280;
        [SerializeField] private int imageHeight = 1280;

        [Header("Status")]
        [SerializeField, ReadOnlyInInspector] private int captureCount = 0;
        [SerializeField, ReadOnlyInInspector] private bool isCapturing = false;

        // Depth
        private DepthDataExtractor depthDataExtractor;
        private ComputeShader copyDepthMapShader;

        // Time conversion
        private double baseOvrTimeSec;
        private long baseUnixTimeMs;

        // HMDポーズバッファ
        private readonly List<HMDPoseData> poseBuffer = new();
        private const int POSE_BUFFER_SIZE = 50;
        private double latestPoseTimestamp;

        // OnBeforeRender用: Depthフレームを一時保持
        private bool depthCaptureRequested = false;
        private bool depthCaptureReady = false;
        private RenderTexture latestDepthRT;
        private DepthFrameDesc latestDepthDesc;
        private int latestDepthWidth;
        private int latestDepthHeight;

        private void Start()
        {
            baseOvrTimeSec = OVRPlugin.GetTimeInSeconds();
            baseUnixTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            depthDataExtractor = new DepthDataExtractor();

            // Scene permission をリクエスト（Depth API に必要）
            UnityEngine.Android.Permission.RequestUserPermission("com.oculus.permission.USE_SCENE");

            Debug.Log($"[CaptureOrch] Base OVR Time: {baseOvrTimeSec}, Base Unix Time: {baseUnixTimeMs}");
        }

        private void OnEnable()
        {
            Application.onBeforeRender += OnBeforeRender;
        }

        private void OnDisable()
        {
            Application.onBeforeRender -= OnBeforeRender;
        }

        private void FixedUpdate()
        {
            BufferHMDPose();
        }

        /// <summary>
        /// レンダリングループ内でDepthフレームを取得。
        /// xrAcquireEnvironmentDepthImage はこのタイミングでしか呼べない。
        /// </summary>
        [BeforeRenderOrder(100)]
        private void OnBeforeRender()
        {
            if (!depthCaptureRequested || depthDataExtractor == null)
                return;

            if (depthDataExtractor.TryGetUpdatedDepthTexture(out var depthRT, out var frameDescs))
            {
                latestDepthRT = depthRT;
                latestDepthDesc = frameDescs[0]; // left eye
                latestDepthWidth = depthRT.width;
                latestDepthHeight = depthRT.height;
                depthCaptureReady = true;
            }
        }

        // =============================================================
        //  Public API
        // =============================================================

        public void InitializeSession(
            float[] camPoseTranslation,
            float[] camPoseRotation,
            float camFx, float camFy, float camCx, float camCy,
            int imgWidth, int imgHeight)
        {
            try
            {
                Debug.Log($"[GARTSS] Orchestrator.InitializeSession called");

                poseTranslation = camPoseTranslation;
                poseRotation = camPoseRotation;
                fx = camFx; fy = camFy; cx = camCx; cy = camCy;
                imageWidth = imgWidth; imageHeight = imgHeight;

                Debug.Log($"[GARTSS] Calling client.InitSession, client={client}, url={client?.ServerUrl}");

                client.InitSession(
                    poseTranslation, poseRotation,
                    fx, fy, cx, cy,
                    imageWidth, imageHeight);

                Debug.Log($"[GARTSS] client.InitSession called");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[GARTSS] InitializeSession EXCEPTION: {e}");
            }
        }

        public void StartCapture()
        {
            if (!client.IsInitialized)
            {
                Debug.LogError("[CaptureOrch] Session not initialized");
                return;
            }

            if (isCapturing)
            {
                Debug.LogWarning("[CaptureOrch] Already capturing");
                return;
            }

            depthDataExtractor?.SetDepthEnabled(true);
            StartCoroutine(CaptureSequence());
        }

        public void StopCapture()
        {
            isCapturing = false;
            depthCaptureRequested = false;
            depthDataExtractor?.SetDepthEnabled(false);
        }

        // =============================================================
        //  Capture Sequence
        // =============================================================

        private IEnumerator CaptureSequence()
        {
            isCapturing = true;
            captureCount = 0;

            // Depth APIが安定するまで少し待つ
            yield return new WaitForSeconds(0.5f);

            while (isCapturing && captureCount < maxCaptures)
            {
                yield return StartCoroutine(CaptureOneFrame());
                captureCount++;

                if (captureCount < maxCaptures)
                {
                    yield return new WaitForSeconds(captureInterval);
                }
            }

            isCapturing = false;
            depthCaptureRequested = false;
            depthDataExtractor?.SetDepthEnabled(false);
            Debug.Log($"[CaptureOrch] Capture sequence complete: {captureCount} frames");
        }

        private IEnumerator CaptureOneFrame()
        {
            // --- 1. OnBeforeRenderでDepthフレームを取得するようリクエスト ---
            depthCaptureReady = false;
            depthCaptureRequested = true;

            // フレームが取得できるまで待つ (最大1秒)
            float timeout = 1.0f;
            float elapsed = 0f;
            while (!depthCaptureReady && elapsed < timeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            depthCaptureRequested = false;

            if (!depthCaptureReady || latestDepthRT == null)
            {
                Debug.LogWarning("[CaptureOrch] No depth frame available");
                yield break;
            }

            var depthDesc = latestDepthDesc;
            int depthWidth = latestDepthWidth;
            int depthHeight = latestDepthHeight;
            var depthRT = latestDepthRT;

            // タイムスタンプ変換
            long depthUnixMs = ConvertTimestampNsToUnixTimeMs(depthDesc.timestampNs);

            // DepthDescriptor作成
            var depthDescriptor = DepthDescriptorData.FromDepthFrameDesc(
                depthDesc, depthUnixMs, depthWidth, depthHeight);

            // --- 2. Depth NDCバッファをGPUから読み出し ---
            byte[] depthRawBytes = null;
            bool readbackDone = false;

            var pixelCount = depthWidth * depthHeight;
            var buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, pixelCount, sizeof(float));
            var dummyBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, pixelCount, sizeof(float));

            if (copyDepthMapShader == null)
            {
                copyDepthMapShader = Resources.Load<ComputeShader>("CopyDepthMap");
                if (copyDepthMapShader == null)
                {
                    Debug.LogError("[CaptureOrch] CopyDepthMap compute shader not found");
                    buffer.Dispose();
                    dummyBuffer.Dispose();
                    yield break;
                }
            }

            int kernel = copyDepthMapShader.FindKernel("CopyRT");
            copyDepthMapShader.SetTexture(kernel, "InputTex", depthRT);
            copyDepthMapShader.SetBuffer(kernel, "LeftEyeDepth", buffer);
            copyDepthMapShader.SetBuffer(kernel, "RightEyeDepth", dummyBuffer);
            copyDepthMapShader.SetInt("_Width", depthWidth);
            copyDepthMapShader.SetInt("_Height", depthHeight);

            int groupsX = Mathf.CeilToInt(depthWidth / 8f);
            int groupsY = Mathf.CeilToInt(depthHeight / 8f);
            copyDepthMapShader.Dispatch(kernel, groupsX, groupsY, 1);

            AsyncGPUReadback.Request(buffer, request =>
            {
                if (!request.hasError)
                {
                    var data = request.GetData<float>();
                    depthRawBytes = new byte[data.Length * sizeof(float)];
                    System.Buffer.BlockCopy(data.ToArray(), 0, depthRawBytes, 0, depthRawBytes.Length);
                }
                else
                {
                    Debug.LogError("[CaptureOrch] GPU readback failed");
                }
                readbackDone = true;
            });

            while (!readbackDone)
                yield return null;

            buffer.Dispose();
            dummyBuffer.Dispose();

            if (depthRawBytes == null)
            {
                Debug.LogError("[CaptureOrch] Failed to read depth data");
                yield break;
            }

            // --- 3. RGB画像 ---
            byte[] rgbPng = null;
            if (imageReaderProvider != null)
            {
                var tempDir = Path.Combine(Application.persistentDataPath,
                    imageReaderProvider.DataDirectoryName, "left_camera_raw");

                // 既存ファイル削除
                if (Directory.Exists(tempDir))
                {
                    foreach (var f in new DirectoryInfo(tempDir).GetFiles())
                    {
                        try { f.Delete(); } catch { }
                    }
                }

                // 保存オン → 1フレーム待つ → 保存オフ
                imageReaderProvider.SetSaveEnabled(true);
                yield return new WaitForSeconds(0.15f);
                imageReaderProvider.SetSaveEnabled(false);

                // 保存されたファイルを読む
                if (Directory.Exists(tempDir))
                {
                    var files = new DirectoryInfo(tempDir).GetFiles();
                    FileInfo latest = null;
                    foreach (var f in files)
                    {
                        if (latest == null || f.LastWriteTime > latest.LastWriteTime)
                            latest = f;
                    }

                    if (latest != null && latest.Exists)
                    {
                        rgbPng = File.ReadAllBytes(latest.FullName);
                        Debug.Log($"[CaptureOrch] RGB loaded: {rgbPng.Length} bytes");
                    }

                    // 全ファイル削除
                    foreach (var f in files)
                    {
                        try { f.Delete(); } catch { }
                    }
                }
            }

            // --- 4. HMDポーズ ---
            var hmdPoses = GetRecentPoses(depthUnixMs);

            // --- 5. サーバーに送信 ---
            Debug.Log($"[CaptureOrch] Sending frame {captureCount}: " +
                      $"depth={depthWidth}x{depthHeight}, " +
                      $"poses={hmdPoses.Length}, ts={depthUnixMs}");

            client.SendCapture(rgbPng, depthRawBytes, depthDescriptor, hmdPoses);
        }

        // =============================================================
        //  HMD Pose Buffering
        // =============================================================

        private void BufferHMDPose()
        {
            var poseState = OVRPlugin.GetNodePoseStateImmediate(OVRPlugin.Node.Head);
            var timestamp = poseState.Time;

            if (timestamp <= latestPoseTimestamp)
                return;

            latestPoseTimestamp = timestamp;

            var pose = poseState.Pose.ToOVRPose();
            var position = pose.position;
            var orientation = pose.orientation;

            var trackingSpace = OVRManager.instance?.transform;
            if (trackingSpace != null)
            {
                position = trackingSpace.TransformPoint(position);
                orientation = trackingSpace.rotation * orientation;
            }

            long unixTimeMs = ConvertOvrSecToUnixTimeMs(timestamp);

            poseBuffer.Add(new HMDPoseData
            {
                timestamp_ms = unixTimeMs,
                pos_x = position.x,
                pos_y = position.y,
                pos_z = position.z,
                rot_x = orientation.x,
                rot_y = orientation.y,
                rot_z = orientation.z,
                rot_w = orientation.w,
            });

            while (poseBuffer.Count > POSE_BUFFER_SIZE)
            {
                poseBuffer.RemoveAt(0);
            }
        }

        private HMDPoseData[] GetRecentPoses(long targetTimestampMs)
        {
            if (poseBuffer.Count == 0)
                return Array.Empty<HMDPoseData>();

            const int MAX_POSES = 20;
            const long MARGIN_MS = 200;

            long tMin = targetTimestampMs - MARGIN_MS;
            long tMax = targetTimestampMs + MARGIN_MS;

            var result = new List<HMDPoseData>();
            foreach (var p in poseBuffer)
            {
                if (p.timestamp_ms >= tMin && p.timestamp_ms <= tMax)
                {
                    result.Add(p);
                    if (result.Count >= MAX_POSES)
                        break;
                }
            }

            if (result.Count == 0)
            {
                return poseBuffer.ToArray();
            }

            return result.ToArray();
        }

        // =============================================================
        //  Time Conversion
        // =============================================================

        private long ConvertOvrSecToUnixTimeMs(double ovrTimeSec)
        {
            var deltaMs = (long)((ovrTimeSec - baseOvrTimeSec) * 1000.0);
            return baseUnixTimeMs + deltaMs;
        }

        private long ConvertTimestampNsToUnixTimeMs(long timestampNs)
        {
            var deltaMs = (long)(timestampNs / 1.0e6 - baseOvrTimeSec * 1000.0);
            return baseUnixTimeMs + deltaMs;
        }
    }
}