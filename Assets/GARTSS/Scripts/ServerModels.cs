// Assets/GARTSS/Scripts/ServerModels.cs
// サーバーAPI のリクエスト/レスポンス JSON モデル
// models/schemas.py と完全に対応

using System;
using UnityEngine;

namespace GARTSS
{
    // =================================================================
    //  Session Init
    // =================================================================

    [Serializable]
    public class CameraPoseData
    {
        public float[] translation; // [tx, ty, tz]
        public float[] rotation;    // [qx, qy, qz, qw]
    }

    [Serializable]
    public class CameraIntrinsicsData
    {
        public float fx;
        public float fy;
        public float cx;
        public float cy;
    }

    [Serializable]
    public class CameraCharacteristicsData
    {
        public CameraPoseData pose;
        public CameraIntrinsicsData intrinsics;
    }

    [Serializable]
    public class ImageFormatData
    {
        public int width = 1280;
        public int height = 1280;
    }

    [Serializable]
    public class SessionInitRequest
    {
        public CameraCharacteristicsData camera_characteristics;
        public ImageFormatData image_format;
    }

    [Serializable]
    public class SessionInitResponse
    {
        public string session_id;
    }

    // =================================================================
    //  Capture
    // =================================================================

    [Serializable]
    public class DepthDescriptorData
    {
        public long timestamp_ms;
        public float create_pose_location_x;
        public float create_pose_location_y;
        public float create_pose_location_z;
        public float create_pose_rotation_x;
        public float create_pose_rotation_y;
        public float create_pose_rotation_z;
        public float create_pose_rotation_w;
        public float fov_left_angle_tangent;
        public float fov_right_angle_tangent;
        public float fov_top_angle_tangent;
        public float fov_down_angle_tangent;
        public float near_z;
        public string far_z; // "Infinity" or float string
        public int width;
        public int height;

        /// <summary>
        /// DepthFrameDesc + タイムスタンプ変換から生成
        /// </summary>
        public static DepthDescriptorData FromDepthFrameDesc(
            RealityLog.Depth.DepthFrameDesc desc,
            long unixTimeMs,
            int width,
            int height)
        {
            return new DepthDescriptorData
            {
                timestamp_ms = unixTimeMs,
                create_pose_location_x = desc.createPoseLocation.x,
                create_pose_location_y = desc.createPoseLocation.y,
                create_pose_location_z = desc.createPoseLocation.z,
                create_pose_rotation_x = desc.createPoseRotation.x,
                create_pose_rotation_y = desc.createPoseRotation.y,
                create_pose_rotation_z = desc.createPoseRotation.z,
                create_pose_rotation_w = desc.createPoseRotation.w,
                fov_left_angle_tangent = desc.fovLeftAngleTangent,
                fov_right_angle_tangent = desc.fovRightAngleTangent,
                fov_top_angle_tangent = desc.fovTopAngleTangent,
                fov_down_angle_tangent = desc.fovDownAngleTangent,
                near_z = desc.nearZ,
                far_z = float.IsInfinity(desc.farZ) ? "Infinity" : desc.farZ.ToString(),
                width = width,
                height = height,
            };
        }
    }

    [Serializable]
    public class HMDPoseData
    {
        // サーバーのHMDPoseスキーマと同じフィールド名
        public long timestamp_ms;
        public float pos_x;
        public float pos_y;
        public float pos_z;
        public float rot_x;
        public float rot_y;
        public float rot_z;
        public float rot_w;
    }

    [Serializable]
    public class HMDPoseArrayWrapper
    {
        public HMDPoseData[] items;
    }

    [Serializable]
    public class CaptureResponse
    {
        public bool aligned;
        public float coverage;
        public string message;
    }

    // =================================================================
    //  Depth Query
    // =================================================================

    [Serializable]
    public class DepthQueryResponse
    {
        public float? depth_m;
        public float[] point_3d_unity;
        public string message;
    }

    // =================================================================
    //  Analyze
    // =================================================================

    [Serializable]
    public class AnalyzeRequest
    {
        public string task;
    }

    [Serializable]
    public class ARPlacementData
    {
        public float[] world_position;
        public float[] world_rotation;
    }

    [Serializable]
    public class DetectedObjectData
    {
        public string name;
        public float[] center_2d;
        public float[] center_3d;
        public float? depth_m;
        public ARPlacementData ar_placement;
    }

    [Serializable]
    public class AnalyzeResponse
    {
        public DetectedObjectData[] objects;
        public string message;
    }
}
