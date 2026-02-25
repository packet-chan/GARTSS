// Assets/GARTSS/Scripts/ARContentPlacer.cs
// サーバーから返された3D座標にARコンテンツを配置する

using System.Collections.Generic;
using UnityEngine;

namespace GARTSS
{
    public class ARContentPlacer : MonoBehaviour
    {
        [Header("Prefabs")]
        [Tooltip("デバッグ用の3Dマーカー (未設定ならCubeを自動生成)")]
        [SerializeField] private GameObject debugMarkerPrefab;

        [Header("Settings")]
        [SerializeField] private float markerScale = 0.05f;

        private readonly List<GameObject> placedObjects = new();

        /// <summary>
        /// Unityワールド座標にCubeを配置
        /// </summary>
        public GameObject PlaceMarker(Vector3 worldPosition, string label = "")
        {
            GameObject marker;

            if (debugMarkerPrefab != null)
            {
                marker = Instantiate(debugMarkerPrefab, worldPosition, Quaternion.identity);
            }
            else
            {
                // Cubeを自動生成
                marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                marker.transform.position = worldPosition;
                var renderer = marker.GetComponent<Renderer>();
                if (renderer != null)
                {
                    // URP対応のマテリアルを作成
                    var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    mat.color = new Color(0f, 1f, 0f, 0.7f);
                    renderer.material = mat;
                }
            }

            marker.transform.localScale = Vector3.one * markerScale;
            marker.SetActive(true);
            marker.name = string.IsNullOrEmpty(label) ? "ARMarker" : $"AR_{label}";

            placedObjects.Add(marker);

            Debug.Log($"[ARPlacer] Placed '{marker.name}' at {worldPosition}");
            return marker;
        }

        /// <summary>
        /// AnalyzeResponseから検出されたオブジェクトを配置
        /// </summary>
        public void PlaceDetectedObjects(AnalyzeResponse response)
        {
            if (response.objects == null) return;

            foreach (var obj in response.objects)
            {
                if (obj.center_3d == null || obj.center_3d.Length < 3)
                {
                    Debug.LogWarning($"[ARPlacer] Object '{obj.name}' has no 3D coordinate");
                    continue;
                }

                var pos = new Vector3(
                    obj.center_3d[0],
                    obj.center_3d[1],
                    obj.center_3d[2]);

                PlaceMarker(pos, obj.name);
            }
        }

        /// <summary>
        /// 配置したオブジェクトをすべて削除
        /// </summary>
        public void ClearAll()
        {
            foreach (var obj in placedObjects)
            {
                if (obj != null) Destroy(obj);
            }
            placedObjects.Clear();
            Debug.Log("[ARPlacer] All objects cleared");
        }
    }
}