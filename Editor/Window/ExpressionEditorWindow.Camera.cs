using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    internal partial class ExpressionEditorWindow
    {
        [SerializeField] private float _startPreviewHeight = 0.03f;
        [SerializeField] private float _startPreviewZoom = 1f;
        private FacePreview _cameraPreview;
        private Texture2D _cameraTexture;
        private float _cameraHeight;
        private float _cameraZoom;

        private void DrawSetupCamera(GameObject root, ref float height, ref float zoom)
        {
            if (root == null) return;
            EditorGUILayout.LabelField("プレビューのカメラ", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                var rect = GUILayoutUtility.GetRect(160, 160, GUILayout.Width(160), GUILayout.Height(160));
                using (new EditorGUILayout.VerticalScope())
                {
                    height = EditorGUILayout.Slider("高さ（m）", height, -0.25f, 0.3f);
                    zoom = EditorGUILayout.Slider("寄り具合", zoom, 0.5f, 2f);
                    EditorGUILayout.LabelField("一覧・詳細・メニューアイコンで同じ構図を使います。", EditorStyles.wordWrappedMiniLabel);
                    if (GUILayout.Button("標準に戻す")) { height = 0.03f; zoom = 1f; }
                }
                if (Event.current.type != EventType.Repaint) return;
                if (_cameraPreview == null || _cameraPreview.Source != root) { DisposeCameraPreview(); _cameraPreview = new FacePreview(root); }
                if (_cameraTexture == null || _cameraHeight != height || _cameraZoom != zoom)
                {
                    if (_cameraTexture != null) DestroyImmediate(_cameraTexture);
                    _cameraPreview.HeightOffset = height; _cameraPreview.Zoom = zoom;
                    _cameraPreview.Apply(null);
                    _cameraTexture = _cameraPreview.RenderStatic(256);
                    _cameraTexture.hideFlags = HideFlags.HideAndDontSave;
                    _cameraHeight = height; _cameraZoom = zoom;
                }
                GUI.DrawTexture(rect, _cameraTexture, ScaleMode.ScaleToFit);
            }
        }

        private void DisposeCameraPreview()
        {
            _cameraPreview?.Dispose(); _cameraPreview = null;
            if (_cameraTexture != null) DestroyImmediate(_cameraTexture);
            _cameraTexture = null;
        }
    }
}
