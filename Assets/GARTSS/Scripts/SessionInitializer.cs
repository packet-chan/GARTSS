// Assets/GARTSS/Scripts/SessionInitializer.cs
// CameraPermissionManager のカメラ情報が取れたらセッション初期化を呼ぶ

using UnityEngine;
using RealityLog.Camera;

namespace GARTSS
{
    public class SessionInitializer : MonoBehaviour
    {
        [SerializeField] private CameraPermissionManager cameraPermissionManager;
        [SerializeField] private CaptureOrchestrator orchestrator;
        [SerializeField] private RGBCameraCapture rgbCamera;
        [SerializeField] private ImageReaderSurfaceProvider imageReaderProvider;

        private bool initialized = false;

        private void Update()
        {
            if (initialized) return;

            if (cameraPermissionManager == null)
            {
                Debug.LogError("[GARTSS] SessionInitializer: cameraPermissionManager is NULL!");
                return;
            }

            if (!cameraPermissionManager.HasCameraManager)
            {
                return;  // ここは毎フレーム来るのでログは出さない
            }

            var metaData = cameraPermissionManager.LeftCameraMetaData;
            if (metaData == null)
            {
                Debug.LogWarning("[GARTSS] SessionInitializer: LeftCameraMetaData is null");
                return;
            }

            Debug.Log($"[GARTSS] Camera ready, calling InitializeSession...");

            if (imageReaderProvider != null)
            {
                imageReaderProvider.DataDirectoryName = "_gartss_rgb";
            }
            
            orchestrator.InitializeSession(
                camPoseTranslation: metaData.pose.translation,
                camPoseRotation: metaData.pose.rotation,
                camFx: metaData.intrinsics.fx,
                camFy: metaData.intrinsics.fy,
                camCx: metaData.intrinsics.cx,
                camCy: metaData.intrinsics.cy,
                imgWidth: metaData.sensor.pixelArraySize.width,
                imgHeight: metaData.sensor.pixelArraySize.height
            );

            if (rgbCamera != null)
            {
                rgbCamera.StartCamera();
            }

            Debug.Log($"[GARTSS] InitializeSession called successfully");
            initialized = true;
        }
    }
}
