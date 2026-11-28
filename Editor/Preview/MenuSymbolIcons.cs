using System;
using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    // 共通の記号画像はパッケージに同梱し、全Prefabで同じアセットを参照する。
    internal static class MenuSymbolIcons
    {
        internal const string AssetFolder = "Packages/jp.samon.facial-expression-editor/Runtime/Icons/Menu";
        public static readonly string[] Labels = { "表情（入口）", "表情セット", "表情選択", "パーツ", "視線操作・目線", "設定", "固定解除・すべてOFF", "まばたきOFF", "次へ", "分類フォルダ", "セットの選択", "ダンスギミック", "手動操作ON", "Base（写真がない場合）" };

        public static Texture2D Get(ExpressionSet set, MenuIconKind kind)
        {
            var custom = set.menuIconOverrides?.Find(i => i != null && i.kind == kind)?.texture;
            if (custom) return custom;
            var path = AssetFolder + "/NSL_" + kind + ".png";
            var asset = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (!asset) throw new InvalidOperationException("共通メニューアイコンが見つかりません。FEEパッケージを確認してください：" + path);
            return asset;
        }

        // パッケージの画像を制作・更新するためだけに使用。通常のメニュー生成では呼ばない。
        internal static Texture2D Render(MenuIconKind kind)
        {
            const int size = 128, samples = 3;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { name = "FEE Symbol " + kind, hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var count = 0;
                for (var sy = 0; sy < samples; sy++)
                for (var sx = 0; sx < samples; sx++)
                    if (Shape(kind, new Vector2((x + (sx + .5f) / samples) / size, (y + (sy + .5f) / samples) / size))) count++;
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(255 * count / (samples * samples)));
            }
            texture.SetPixels32(pixels); texture.Apply();
            return texture;
        }

        private static bool Disk(Vector2 p, float x, float y, float radius) => (p - new Vector2(x, y)).sqrMagnitude <= radius * radius;
        private static bool Triangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float Cross(Vector2 u, Vector2 v) => u.x * v.y - u.y * v.x;
            var x = Cross(b - a, p - a); var y = Cross(c - b, p - b); var z = Cross(a - c, p - c);
            return x >= 0 && y >= 0 && z >= 0 || x <= 0 && y <= 0 && z <= 0;
        }
        private static bool Line(Vector2 p, float ax, float ay, float bx, float by, float radius = .035f)
        {
            var a = new Vector2(ax, ay); var v = new Vector2(bx, by) - a;
            return (p - a - v * Mathf.Clamp01(Vector2.Dot(p - a, v) / v.sqrMagnitude)).sqrMagnitude <= radius * radius;
        }
        private static bool Face(Vector2 p)
        {
            return Disk(p, .5f, .5f, .33f) && !Disk(p, .39f, .59f, .045f) && !Disk(p, .61f, .59f, .045f)
                && !(p.y < .44f && Disk(p, .5f, .46f, .17f) && !Disk(p, .5f, .5f, .17f));
        }
        private static bool Arrow(Vector2 p) => Line(p, .5f, .76f, .5f, .91f, .022f) || Line(p, .5f, .91f, .43f, .84f, .022f) || Line(p, .5f, .91f, .57f, .84f, .022f);
        private static Vector2 Rotate(Vector2 p, float angle)
        {
            p -= Vector2.one * .5f; var c = Mathf.Cos(angle); var s = Mathf.Sin(angle);
            return new Vector2(p.x * c - p.y * s, p.x * s + p.y * c) + Vector2.one * .5f;
        }
        private static bool Shape(MenuIconKind kind, Vector2 p)
        {
            switch (kind)
            {
                case MenuIconKind.Root: case MenuIconKind.Base: return Face(p);
                case MenuIconKind.Sets:
                    return Face((p - new Vector2(-.12f, .12f) - Vector2.one * .5f) / .78f + Vector2.one * .5f) && !Disk(p, .6f, .4f, .29f)
                        || Face((p - new Vector2(.12f, -.12f) - Vector2.one * .5f) / .78f + Vector2.one * .5f);
                case MenuIconKind.Selection:
                    return Face(p) && !(p.x > .55f && p.y < .5f)
                        || p.y > .13f && p.y < .48f && p.x > .6f && p.x < .6f + (.48f - p.y) * .65f
                        || Line(p, .68f, .24f, .77f, .12f, .035f);
                case MenuIconKind.Parts:
                    var star = Mathf.Sqrt(Mathf.Abs(p.x - .78f) / .17f) + Mathf.Sqrt(Mathf.Abs(p.y - .77f) / .17f) < 1;
                    return Face(p) && !Disk(p, .78f, .77f, .19f) || star;
                case MenuIconKind.EyeLook:
                    var eye = Mathf.Pow((p.x - .5f) / .29f, 2) + Mathf.Pow((p.y - .5f) / .16f, 2) < 1;
                    return eye && (!Disk(p, .5f, .5f, .10f) || Disk(p, .5f, .5f, .045f))
                        || Arrow(p) || Arrow(Rotate(p, Mathf.PI / 2)) || Arrow(Rotate(p, Mathf.PI)) || Arrow(Rotate(p, -Mathf.PI / 2));
                case MenuIconKind.Settings:
                    var v = p - Vector2.one * .5f; var r = v.magnitude;
                    return r > .13f && (r < .27f || r < .36f && Mathf.Cos(Mathf.Atan2(v.y, v.x) * 8) > .3f);
                case MenuIconKind.Reset:
                    var q = p - new Vector2(.5f, .45f);
                    return q.magnitude > .24f && q.magnitude < .32f && !(q.x < 0 && q.y > 0) && Mathf.Atan2(q.y, q.x) > -2.35f
                        || Line(p, .5f, .73f, .29f, .73f, .04f)
                        || Triangle(p, new Vector2(.13f, .73f), new Vector2(.35f, .91f), new Vector2(.35f, .55f));
                case MenuIconKind.BlinkOff:
                    return Line(p, .22f, .57f, .5f, .43f) || Line(p, .5f, .43f, .78f, .57f)
                        || Line(p, .3f, .52f, .25f, .39f, .025f) || Line(p, .7f, .52f, .75f, .39f, .025f)
                        || Line(p, .23f, .23f, .77f, .77f, .04f);
                case MenuIconKind.Next: return Line(p, .38f, .23f, .65f, .5f, .065f) || Line(p, .65f, .5f, .38f, .77f, .065f);
                case MenuIconKind.Folder: return p.x > .17f && p.x < .83f && p.y > .25f && p.y < .68f || p.x > .17f && p.x < .45f && p.y >= .68f && p.y < .77f;
                case MenuIconKind.Check: return Line(p, .22f, .5f, .42f, .3f, .06f) || Line(p, .42f, .3f, .78f, .72f, .06f);
                case MenuIconKind.Dance: return Disk(p, .32f, .28f, .11f) || Disk(p, .69f, .38f, .11f) || Line(p, .4f, .28f, .4f, .73f) || Line(p, .77f, .38f, .77f, .83f) || Line(p, .4f, .73f, .77f, .83f, .055f);
                case MenuIconKind.Power: return Disk(p, .5f, .48f, .3f) && !Disk(p, .5f, .48f, .22f) && !(p.y > .62f && Mathf.Abs(p.x - .5f) < .12f) || Line(p, .5f, .5f, .5f, .83f, .045f);
                default: return false;
            }
        }
    }
}
