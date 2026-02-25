# GARTSS — Unity セットアップガイド

QuestRealityCapture リポジトリをベースに、**新しいシーン**を作って GARTSS を構築する手順。
既存の保存ロジック (DepthMapExporter, PoseLogger) は使わず、メモリ上で即サーバー送信する設計。

---

## 0. 前提

| 項目 | バージョン |
|------|-----------|
| Unity | 6000.0.30f1 (元リポジトリと同じ) |
| Meta XR SDK | com.meta.xr.sdk.interaction.ovr 76.0.0 |
| OpenXR | com.unity.xr.openxr 1.13.2 + com.unity.xr.meta-openxr 2.1.0 |
| Quest 3 | v76以降 |

---

## 1. リポジトリのクローンと Unity で開く

```bash
git clone https://github.com/t-34400/QuestRealityCapture.git
cd QuestRealityCapture
```

Unity Hub → **Add project from disk** → クローンしたフォルダを選択。
Unity 6000.0.30f1 で開く（バージョンが違う場合はインストール）。

初回オープン時は Meta XR SDK のインポートに数分かかる。
**パッケージエラーが出たら**:
- Window → Package Manager → 左上の + → Add by name
- `com.meta.xr.sdk.interaction.ovr` version `76.0.0` を確認

---

## 2. Project Settings の確認

リポジトリの設定がそのまま使えるが、念のため確認:

### 2.1 XR Plug-in Management
Edit → Project Settings → XR Plug-in Management → **Android タブ**:
- [x] OpenXR にチェック
- Meta Quest Feature Group が有効になっていること

### 2.2 OpenXR Features (Android)
Edit → Project Settings → XR Plug-in Management → OpenXR → **Android タブ**:
- [x] **Meta Quest: Passthrough** — 有効
- [x] **Meta Quest: Occlusion** — 有効 (Depth API用)
- [x] Meta Quest Touch Plus Controller Profile — 有効
- Render Mode: **Multi-pass** (Depthのステレオテクスチャ用)

### 2.3 Player Settings (Android)
Edit → Project Settings → Player → **Android タブ**:
- Minimum API Level: **32**
- Scripting Backend: **IL2CPP**
- Target Architectures: **ARM64** のみ

### 2.4 AndroidManifest.xml の確認
`Assets/Plugins/Android/AndroidManifest.xml` に以下が含まれていること:
```xml
<uses-permission android:name="android.permission.CAMERA"/>
<uses-permission android:name="horizonos.permission.HEADSET_CAMERA"/>
```

**INTERNET permission の追加** (HTTP通信用):
```xml
<uses-permission android:name="android.permission.INTERNET"/>
<uses-permission android:name="android.permission.ACCESS_NETWORK_STATE"/>
```
元リポジトリにはINTERNET permissionが無いので、**これは手動で追加が必要**。

---

## 3. スクリプトの配置

ダウンロードした4つのC#ファイルを以下の場所に配置:

```
Assets/
├── RealityLog/                  ← 既存 (一切変更しない)
│   ├── Scripts/Runtime/
│   │   ├── Camera/
│   │   │   ├── CameraPermissionManager.cs   ← 使う (カメラ情報取得)
│   │   │   ├── CameraSessionManager.cs      ← 使わない (ファイル保存系)
│   │   │   └── ...
│   │   ├── Depth/
│   │   │   ├── Meta/
│   │   │   │   ├── DepthDataExtractor.cs    ← 使う (Depth取得)
│   │   │   │   └── DepthFrameDesc.cs        ← 使う (型定義)
│   │   │   └── DepthMapExporter.cs          ← 使わない (ファイル保存)
│   │   ├── OVR/
│   │   │   └── PoseLogger.cs                ← 使わない (ファイル保存)
│   │   └── IO/
│   │       └── DepthRenderTextureExporter.cs ← 参考 (GPU Readbackロジック)
│   └── ComputeShaders/
│       └── CopyDepthMap.compute             ← 使う (Depth読み出し)
│
├── GARTSS/                ← 新規作成
│   └── Scripts/
│       ├── ServerModels.cs
│       ├── GARTSSClient.cs
│       ├── CaptureOrchestrator.cs
│       └── ARContentPlacer.cs
│
└── Plugins/Android/
    ├── AndroidManifest.xml      ← INTERNET permission 追加
    └── questcameralib.aar       ← 既存 (Camera2 APIラッパー)
```

---

## 4. ComputeShader を Resources に配置

`CaptureOrchestrator.cs` は `Resources.Load<ComputeShader>("CopyDepthMap")` で
ComputeShaderをロードする。以下のいずれかで対応:

**方法A (推奨): Resources フォルダにコピー**
```
Assets/
└── Resources/
    └── CopyDepthMap.compute   ← RealityLog/ComputeShaders/ からコピー
```

**方法B: Inspector で直接参照**
`CaptureOrchestrator` に `[SerializeField]` フィールドを追加して
Inspector でドラッグ＆ドロップ。

---

## 5. 新しいシーンを作成

### 5.1 シーン作成
File → New Scene → **Basic** → Save As: `Assets/GARTSS/Scenes/GARTSSScene.unity`

### 5.2 XR Origin のセットアップ
Hierarchy で右クリック → **XR → XR Origin (VR)** を追加。
これで `XR Origin`, `Main Camera`, `Left/Right Controller` が作られる。

### 5.3 OVRManager の追加
XR Origin (または空のGameObject) に **OVR Manager** コンポーネントを追加:
- Tracking Origin Type: **Floor Level**
- Passthrough Support: **Required**
- Anchor Support: **Enabled** (任意)

### 5.4 Passthrough の有効化
1. Hierarchy で空の GameObject「PassthroughLayer」を作成
2. **OVR Passthrough Layer** コンポーネントを追加:
   - Placement: **Underlay**
   - Projection Surface: **Reconstructed** (デフォルト)
3. Main Camera の **Clear Flags** を **Solid Color**、Background を **黒 (0,0,0,0)** に設定

### 5.5 CameraPermissionManager の配置
1. 空の GameObject「CameraManager」を作成
2. **CameraPermissionManager** コンポーネントをアタッチ
   - これは `RealityLog.Camera` namespace にある既存スクリプト

### 5.6 GARTSS Manager の配置
1. 空の GameObject「GARTSSManager」を作成
2. 以下のコンポーネントをアタッチ:

| コンポーネント | 設定 |
|--------------|------|
| **GARTSSClient** | Server URL: `http://<PCのIP>:8000` |
| **CaptureOrchestrator** | Client: 上記GARTSSClientへの参照 |
| **ARContentPlacer** | (デフォルトのまま) |

### 5.7 初期化スクリプトの作成

カメラ許可が下りた後にセッションを初期化するブリッジスクリプト:

```csharp
// Assets/GARTSS/Scripts/SessionInitializer.cs
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
```

このスクリプトも「GARTSSManager」にアタッチし、参照を設定。

### 5.8 スタートボタンの作成

コントローラーのボタンで `orchestrator.StartCapture()` を呼ぶ。
最もシンプルな方法:

```csharp
// Assets/GARTSS/Scripts/CaptureButton.cs
using UnityEngine;

namespace GARTSS
{
    public class CaptureButton : MonoBehaviour
    {
        [SerializeField] private CaptureOrchestrator orchestrator;

        private void Update()
        {
            // 右コントローラーのAボタン
            if (OVRInput.GetDown(OVRInput.Button.One))
            {
                orchestrator.StartCapture();
            }
        }
    }
}
```

これも「GARTSSManager」にアタッチ。

---

## 6. 最終的なシーン階層

```
GARTSSScene
├── XR Origin
│   ├── Camera Offset
│   │   ├── Main Camera
│   │   ├── Left Controller
│   │   └── Right Controller
│   └── OVR Manager
├── PassthroughLayer          [OVR Passthrough Layer]
├── CameraManager             [CameraPermissionManager]
└── GARTSSManager       [GARTSSClient]
                              [CaptureOrchestrator]
                              [ARContentPlacer]
                              [SessionInitializer]
                              [CaptureButton]
```

---

## 7. ビルド & 実行

### 7.1 Build Settings
File → Build Settings:
- Platform: **Android**
- Scenes In Build: `GARTSSScene` のみチェック
- Development Build: チェック (デバッグ用)

### 7.2 サーバー起動 (PC側)
```bash
cd gartss-server
conda activate ar-assist
PYTHONPATH=. uvicorn server:app --host 0.0.0.0 --port 8000
```

### 7.3 IPアドレスの確認
PC側のローカルIPを確認して、Unity側の ServerUrl に設定:
```bash
# Windows
ipconfig

# Mac/Linux
ifconfig | grep "inet "
```

### 7.4 Quest 3 にデプロイ
Build and Run → Quest 3 を USB接続 → 自動インストール

---

## 8. 動作確認チェックリスト

1. [ ] アプリ起動 → パススルーが見える
2. [ ] 「Camera ready」ログが出る
3. [ ] 「Session initialized: xxxxxxxx」ログが出る
4. [ ] Aボタン押下 → 「Sending frame 0: ...」ログ
5. [ ] PC側サーバーに POST /capture が届く
6. [ ] 「Aligned: coverage=XX%」が返る

### トラブルシューティング

| 症状 | 原因 | 対処 |
|------|------|------|
| Camera ready が出ない | カメラ許可が未承認 | 初回起動時にダイアログで許可 |
| Session initialized が出ない | サーバーに接続できない | IPアドレス/ポート確認、同一Wi-Fi確認 |
| coverage が 0% | Depth APIが無効 | Project Settings → OpenXR → Occlusion 有効化 |
| HTTP timeout | ファイアウォール | PC側のポート8000を開放 |

---

## 9. データフロー (ローカル保存なし)

```
[Quest 3]
  DepthDataExtractor → RenderTexture (GPU)
       ↓ AsyncGPUReadback
  byte[] (メモリ上のみ)
       ↓ UnityWebRequest POST
  [サーバー] → alignment → response
       ↓
  ARContentPlacer → GameObjectを3D空間に配置
```

**ローカルストレージへの書き込みはゼロ。**
既存の DepthMapExporter, PoseLogger, CsvWriter は一切シーンに配置しない。
