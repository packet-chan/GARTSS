// Assets/GARTSS/Scripts/CaptureOrchestrator.cs
// QuestRealityCapture の既存コンポーネントからデータを集めてサーバーに送信する
// 
// 使い方:
//   1. シーンに空のGameObjectを作成し、このスクリプトをアタッチ
//   2. Inspector で各参照を設定
//   3. スタートボタン押下 → StartCapture() を呼ぶ
//
// 既存の DepthMapExporter, PoseLogger はファイル保存用にそのまま残してもよいし、
// 不要なら無効化してもよい。このスクリプトは独立して動く。

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using RealityLog.Depth;

namespace GARTSS
{
    public class CaptureOrchestrator : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GARTSSClient client;

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

        // Time conversion (PoseLoggerと同じロジック)
        private double baseOvrTimeSec;
        private long baseUnixTimeMs;

        // HMDポーズバッファ (直近N個を保持)
        private readonly List<HMDPoseData> poseBuffer = new();
        private const int POSE_BUFFER_SIZE = 50;
        private double latestPoseTimestamp;

        private void Start()
        {
            baseOvrTimeSec = OVRPlugin.GetTimeInSeconds();
            baseUnixTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            depthDataExtractor = new DepthDataExtractor();

            Debug.Log($"[CaptureOrch] Base OVR Time: {baseOvrTimeSec}, Base Unix Time: {baseUnixTimeMs}");
        }

        private void FixedUpdate()
        {
            // HMDポーズを常時バッファリング (PoseLoggerと同じ取得方法)
            BufferHMDPose();
        }

        // =============================================================
        //  Public API
        // =============================================================

        /// <summary>
        /// camera_characteristics.json の内容でセッション初期化。
        /// CameraPermissionManager の CameraMetaData から値を取得して呼ぶ。
        /// </summary>
        public void InitializeSession(
            float[] camPoseTranslation,
            float[] camPoseRotation,
            float camFx, float camFy, float camCx, float camCy,
            int imgWidth, int imgHeight)
        {
            poseTranslation = camPoseTranslation;
            poseRotation = camPoseRotation;
            fx = camFx; fy = camFy; cx = camCx; cy = camCy;
            imageWidth = imgWidth; imageHeight = imgHeight;

            client.InitSession(
                poseTranslation, poseRotation,
                fx, fy, cx, cy,
                imageWidth, imageHeight);
        }

        /// <summary>
        /// キャプチャ開始。ユーザーがスタートボタンを押した時に呼ぶ。
        /// </summary>
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

        /// <summary>
        /// キャプチャ停止
        /// </summary>
        public void StopCapture()
        {
            isCapturing = false;
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
            yield return new WaitForSeconds(0.3f);

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
            depthDataExtractor?.SetDepthEnabled(false);
            Debug.Log($"[CaptureOrch] Capture sequence complete: {captureCount} frames");
        }

        private IEnumerator CaptureOneFrame()
        {
            // --- 1. Depthフレーム取得 ---
            if (!depthDataExtractor.TryGetUpdatedDepthTexture(
                    out var depthRT, out var frameDescs))
            {
                Debug.LogWarning("[CaptureOrch] No depth frame available");
                yield break;
            }

            var leftDesc = frameDescs[0]; // left eye
            int depthWidth = depthRT.width;
            int depthHeight = depthRT.height;

            // タイムスタンプ変換
            long depthUnixMs = ConvertTimestampNsToUnixTimeMs(leftDesc.timestampNs);

            // DepthDescriptor作成
            var depthDescriptor = DepthDescriptorData.FromDepthFrameDesc(
                leftDesc, depthUnixMs, depthWidth, depthHeight);

            // --- 2. Depth NDCバッファをGPUから読み出し ---
            byte[] depthRawBytes = null;
            bool readbackDone = false;

            // ComputeShader で左目のDepthを取得
            // DepthRenderTextureExporterと同様のロジックだが、ファイル保存ではなくバイト配列に
            var pixelCount = depthWidth * depthHeight;
            var buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, pixelCount, sizeof(float));

            // CopyDepthMap compute shader を使用
            if (copyDepthMapShader == null)
            {
                copyDepthMapShader = Resources.Load<ComputeShader>("CopyDepthMap");
                if (copyDepthMapShader == null)
                {
                    Debug.LogError("[CaptureOrch] CopyDepthMap compute shader not found");
                    buffer.Dispose();
                    yield break;
                }
            }

            int kernel = copyDepthMapShader.FindKernel("CopyRT");
            copyDepthMapShader.SetTexture(kernel, "InputTex", depthRT);
            copyDepthMapShader.SetBuffer(kernel, "LeftEyeDepth", buffer);
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
                    Buffer.BlockCopy(data.ToArray(), 0, depthRawBytes, 0, depthRawBytes.Length);
                }
                else
                {
                    Debug.LogError("[CaptureOrch] GPU readback failed");
                }
                readbackDone = true;
            });

            // Readback完了を待つ
            while (!readbackDone)
                yield return null;

            buffer.Dispose();

            if (depthRawBytes == null)
            {
                Debug.LogError("[CaptureOrch] Failed to read depth data");
                yield break;
            }

            // --- 3. RGB画像取得 ---
            // TODO: CameraSessionManager の ImageReader から取得する実装
            // 暫定: パススルーカメラのスクリーンショットまたは null
            byte[] rgbPng = null;
            // rgbPng = GetRGBImageFromCamera(); // Phase3で実装

            // --- 4. HMDポーズ (バッファから直近のものを取得) ---
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

            // TrackingSpaceがある場合はワールド座標に変換
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

            // バッファサイズ制限
            while (poseBuffer.Count > POSE_BUFFER_SIZE)
            {
                poseBuffer.RemoveAt(0);
            }
        }

        /// <summary>
        /// 指定タイムスタンプ前後のHMDポーズを取得。
        /// サーバー側のPoseInterpolatorが補間するので、前後数個あれば十分。
        /// </summary>
        private HMDPoseData[] GetRecentPoses(long targetTimestampMs)
        {
            if (poseBuffer.Count == 0)
                return Array.Empty<HMDPoseData>();

            // targetの前後を含む範囲を返す (最大20個)
            const int MAX_POSES = 20;
            const long MARGIN_MS = 200; // ±200ms

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

            // 範囲内のポーズがなければバッファ全体から最も近いものを返す
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
