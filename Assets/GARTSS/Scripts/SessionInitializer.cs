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

        private bool initialized = false;

        private void Update()
        {
            if (initialized) return;
            if (!cameraPermissionManager.HasCameraManager) return;

            var metaData = cameraPermissionManager.LeftCameraMetaData;
            if (metaData == null) return;

            Debug.Log($"[SessionInit] Camera ready: {metaData}");

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

            initialized = true;
        }
    }
}
