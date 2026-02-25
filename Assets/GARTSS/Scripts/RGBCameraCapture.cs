// Assets/GARTSS/Scripts/RGBCameraCapture.cs
// Camera2 API 経由で 1280x1280 RGB 画像を取得する
// 元リポジトリの CameraSessionManager + ImageReaderSurfaceProvider の仕組みを
// ファイル保存→読み込み→削除 の形で活用

using System;
using System.Collections;
using System.IO;
using UnityEngine;
using RealityLog.Camera;

namespace GARTSS
{
    public class RGBCameraCapture : MonoBehaviour
    {
        private const string IMAGE_READER_CLASS = "com.t34400.questcamera.io.ImageReaderSurfaceProvider";
        private const string SESSION_MANAGER_CLASS = "com.t34400.questcamera.core.CameraSessionManager";

        [SerializeField] private CameraPermissionManager cameraPermissionManager;
        [SerializeField] private int bufferPoolSize = 5;

        private AndroidJavaObject sessionManagerInstance;
        private AndroidJavaObject imageReaderInstance;

        private string tempImageDir;
        private string tempFormatInfoPath;
        private bool isReady = false;

        public bool IsReady => isReady;

        /// <summary>
        /// CameraPermissionManager のカメラ情報が取れた後に呼ぶ
        /// </summary>
        public void StartCamera()
        {
#if UNITY_ANDROID
            var cameraManager = cameraPermissionManager.CameraManagerJavaInstance;
            if (cameraManager == null)
            {
                Debug.LogError("[RGBCapture] CameraManager not available");
                return;
            }

            var metaData = cameraPermissionManager.LeftCameraMetaData;
            if (metaData == null)
            {
                Debug.LogError("[RGBCapture] LeftCameraMetaData not available");
                return;
            }

            // 一時ディレクトリ作成
            tempImageDir = Path.Combine(Application.persistentDataPath, "_gartss_temp", "rgb");
            tempFormatInfoPath = Path.Combine(Application.persistentDataPath, "_gartss_temp", "format.json");
            Directory.CreateDirectory(tempImageDir);

            var size = metaData.sensor.pixelArraySize;

            // ImageReaderSurfaceProvider のJavaインスタンスを作成
            imageReaderInstance = new AndroidJavaObject(
                IMAGE_READER_CLASS,
                size.width,
                size.height,
                tempImageDir,
                tempFormatInfoPath,
                bufferPoolSize
            );
            imageReaderInstance.Call("setShouldSaveFrame", true);

            // CameraSessionManager でカメラを開く
            sessionManagerInstance = new AndroidJavaObject(SESSION_MANAGER_CLASS);
            sessionManagerInstance.Call("registerSurfaceProvider", imageReaderInstance);
            sessionManagerInstance.Call("setCaptureTemplateFromString", "STILL_CAPTURE");

            using (var unityPlayerClazz = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var currentActivity = unityPlayerClazz.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                sessionManagerInstance.Call("openCamera", currentActivity, cameraManager, metaData.cameraId);
            }

            isReady = true;
            Debug.Log($"[RGBCapture] Camera started: {size.width}x{size.height}");
#endif
        }

        /// <summary>
        /// 最新のRGB画像をバイト配列として取得。
        /// 一時ファイルから読み込み後、ファイルを削除。
        /// </summary>
        public byte[] GetLatestImageAsPng()
        {
            if (!isReady || string.IsNullOrEmpty(tempImageDir))
                return null;

            try
            {
                // 一時ディレクトリ内の最新ファイルを取得
                var dirInfo = new DirectoryInfo(tempImageDir);
                if (!dirInfo.Exists) return null;

                FileInfo latest = null;
                foreach (var file in dirInfo.GetFiles())
                {
                    if (latest == null || file.LastWriteTime > latest.LastWriteTime)
                    {
                        latest = file;
                    }
                }

                if (latest == null || !latest.Exists)
                    return null;

                // YUV or PNG として読み込み
                byte[] rawBytes = File.ReadAllBytes(latest.FullName);
                
                // format.json を読んで画像形式を確認
                int width = 1280;
                int height = 1280;
                
                if (File.Exists(tempFormatInfoPath))
                {
                    try
                    {
                        var formatJson = File.ReadAllText(tempFormatInfoPath);
                        var format = JsonUtility.FromJson<ImageFormatInfo>(formatJson);
                        width = format.width;
                        height = format.height;
                    }
                    catch (Exception) { }
                }

                byte[] pngBytes;

                if (latest.Extension.ToLower() == ".png" || latest.Extension.ToLower() == ".jpg")
                {
                    pngBytes = rawBytes;
                }
                else
                {
                    // YUV_420_888 → Texture2D → PNG
                    // YUVの場合、Y plane は width * height バイト
                    if (rawBytes.Length >= width * height)
                    {
                        var tex = new Texture2D(width, height, TextureFormat.R8, false);
                        // Y plane のみ使用 (グレースケール)
                        var yPlane = new byte[width * height];
                        Array.Copy(rawBytes, yPlane, yPlane.Length);
                        tex.LoadRawTextureData(yPlane);
                        tex.Apply();
                        pngBytes = tex.EncodeToPNG();
                        UnityEngine.Object.Destroy(tex);
                    }
                    else
                    {
                        Debug.LogWarning($"[RGBCapture] Unexpected file size: {rawBytes.Length}");
                        return null;
                    }
                }

                // 読み込み後に一時ファイルを全て削除
                foreach (var file in dirInfo.GetFiles())
                {
                    try { file.Delete(); } catch { }
                }

                Debug.Log($"[RGBCapture] Got image: {pngBytes.Length} bytes from {latest.Name}");
                return pngBytes;
            }
            catch (Exception e)
            {
                Debug.LogError($"[RGBCapture] Error reading image: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// 一時ファイルをすべてクリア
        /// </summary>
        public void ClearTempFiles()
        {
            if (string.IsNullOrEmpty(tempImageDir)) return;
            try
            {
                var dirInfo = new DirectoryInfo(tempImageDir);
                if (dirInfo.Exists)
                {
                    foreach (var file in dirInfo.GetFiles())
                    {
                        try { file.Delete(); } catch { }
                    }
                }
            }
            catch { }
        }

        private void OnDestroy()
        {
#if UNITY_ANDROID
            sessionManagerInstance?.Call("close");
            sessionManagerInstance?.Dispose();
            sessionManagerInstance = null;

            imageReaderInstance?.Call("close");
            imageReaderInstance?.Dispose();
            imageReaderInstance = null;
#endif
            // 一時ディレクトリ削除
            try
            {
                var tempBase = Path.Combine(Application.persistentDataPath, "_gartss_temp");
                if (Directory.Exists(tempBase))
                    Directory.Delete(tempBase, true);
            }
            catch { }
        }

        [Serializable]
        private class ImageFormatInfo
        {
            public int width;
            public int height;
            public string format;
        }
    }
}
