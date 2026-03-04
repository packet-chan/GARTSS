// Assets/GARTSS/Scripts/ARContentPlacer.cs

using System.Collections.Generic;
using UnityEngine;

namespace GARTSS
{
    public class ARContentPlacer : MonoBehaviour
    {
        [Header("Prefabs")]
        [Tooltip("配置するARオブジェクトのPrefab")]
        [SerializeField] private GameObject markerPrefab;

        private readonly List<GameObject> placedObjects = new();

        public GameObject PlaceMarker(Vector3 worldPosition, string label = "")
        {
            if (markerPrefab == null)
            {
                Debug.LogError("[ARPlacer] Marker Prefab is not assigned in Inspector");
                return null;
            }

            var marker = Instantiate(markerPrefab, worldPosition, Quaternion.identity);
            marker.name = string.IsNullOrEmpty(label) ? "ARMarker" : $"AR_{label}";

            placedObjects.Add(marker);
            Debug.Log($"[ARPlacer] Placed '{marker.name}' at {worldPosition}");
            return marker;
        }

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