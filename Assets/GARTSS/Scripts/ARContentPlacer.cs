// Assets/GARTSS/Scripts/ARContentPlacer.cs
// サーバーから返された3D座標にARコンテンツを配置する
// Phase 4 で本格的に作り込むが、Phase 2 の時点でデバッグ表示に使える

using System.Collections.Generic;
using UnityEngine;

namespace GARTSS
{
    public class ARContentPlacer : MonoBehaviour
    {
        [Header("Prefabs")]
        [Tooltip("デバッグ用の3Dマーカー (Sphere等)")]
        [SerializeField] private GameObject debugMarkerPrefab;
        [Tooltip("テキストラベル用プレハブ")]
        [SerializeField] private GameObject textLabelPrefab;

        [Header("Settings")]
        [SerializeField] private float markerScale = 0.02f;

        private readonly List<GameObject> placedObjects = new();

        /// <summary>
        /// Unityワールド座標に3Dマーカーを配置
        /// </summary>
        public GameObject PlaceMarker(Vector3 worldPosition, string label = "")
        {
            var prefab = debugMarkerPrefab;
            if (prefab == null)
            {
                // プレハブ未設定の場合はSphereを自動生成
                prefab = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                prefab.transform.localScale = Vector3.one * markerScale;
                var renderer = prefab.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.material.color = Color.red;
                }
                // テンプレートとして使うので非アクティブに
                prefab.SetActive(false);
            }

            var marker = Instantiate(prefab, worldPosition, Quaternion.identity);
            marker.transform.localScale = Vector3.one * markerScale;
            marker.SetActive(true);
            marker.name = string.IsNullOrEmpty(label) ? "ARMarker" : $"AR_{label}";

            placedObjects.Add(marker);

            Debug.Log($"[ARContentPlacer] Placed '{marker.name}' at {worldPosition}");
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
                if (obj.ar_placement?.world_position == null) continue;

                var pos = new Vector3(
                    obj.ar_placement.world_position[0],
                    obj.ar_placement.world_position[1],
                    obj.ar_placement.world_position[2]);

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
        }
    }
}
