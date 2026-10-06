using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Samon.FacialExpressionEditor.Editor
{
    /// <summary>
    /// サムネイルのキャッシュ。メモリと、プロジェクトの Library/ 以下のファイルに保存する（Assets/ は散らかさない）。
    /// 無いものは要求を溜めておき、ProcessPending で少しずつ描画する。
    /// </summary>
    internal sealed class ThumbnailCache : IDisposable
    {
        public const int Size = 128;

        // メモリに持つ枚数の上限。超えたらメモリ上だけ捨てる（ファイルからすぐ読み直せる）。
        private const int MaxInMemory = 400;

        private static readonly string Directory = Path.Combine("Library", "FacialExpressionEditor", "Thumbnails");

        private readonly Dictionary<string, Texture2D> _memory = new Dictionary<string, Texture2D>();
        private readonly List<(string key, Func<Texture2D> render)> _pending = new List<(string, Func<Texture2D>)>();
        private readonly HashSet<string> _pendingKeys = new HashSet<string>();

        public bool HasPending => _pending.Count > 0;

        /// <summary>
        /// キャッシュにあれば返す。無ければ描画を予約して null を返す。
        /// </summary>
        public Texture2D Get(string key, Func<Texture2D> render)
        {
            if (_memory.TryGetValue(key, out var texture) && texture != null) return texture;

            texture = LoadFromDisk(key);
            if (texture != null)
            {
                texture.hideFlags = HideFlags.HideAndDontSave;
                _memory[key] = texture;
                return texture;
            }

            if (_pendingKeys.Add(key)) _pending.Add((key, render));
            return null;
        }

        /// <summary>
        /// 保存済みのサムネイルを読み込む。無ければ null（呼び出し側で破棄する）。
        /// </summary>
        public static Texture2D LoadFromDisk(string key)
        {
            var file = PathFor(key);
            if (!File.Exists(file)) return null;

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (texture.LoadImage(File.ReadAllBytes(file))) return texture;

            Object.DestroyImmediate(texture);
            return null;
        }

        /// <summary>
        /// 予約されたサムネイルを最大 max 件描画する。戻り値は、まだ残りがあるかどうか。
        /// </summary>
        public bool ProcessPending(int max)
        {
            // 描画中に捨てると表示中の画像が消えるので、GUIの外で呼ばれるここで上限を見る。
            if (_memory.Count >= MaxInMemory) ClearMemory();

            for (var i = 0; i < max && _pending.Count > 0; i++)
            {
                var (key, render) = _pending[0];
                _pending.RemoveAt(0);
                _pendingKeys.Remove(key);

                Texture2D texture;
                try
                {
                    texture = render();
                }
                catch (Exception e)
                {
                    // 描けなかったものは今回は諦める（次に表示を更新したときにもう一度試す）。
                    Debug.LogWarning($"[FacialExpressionEditor] サムネイルを描けませんでした：{e.Message}");
                    continue;
                }
                if (texture == null) continue;

                texture.hideFlags = HideFlags.HideAndDontSave;
                _memory[key] = texture;
                SaveToDisk(key, texture);
            }
            return _pending.Count > 0;
        }

        public static bool ExistsOnDisk(string key) => File.Exists(PathFor(key));

        public static void SaveToDisk(string key, Texture2D texture)
        {
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                File.WriteAllBytes(PathFor(key), texture.EncodeToPNG());
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FacialExpressionEditor] サムネイルを保存できませんでした：{e.Message}");
            }
        }

        /// <summary>
        /// メモリ上のキャッシュと予約を捨てる（ファイルはキーが変わらない限り再利用する）。
        /// </summary>
        public void Clear()
        {
            ClearMemory();
            _pending.Clear();
            _pendingKeys.Clear();
        }

        private void ClearMemory()
        {
            foreach (var texture in _memory.Values)
            {
                if (texture != null) Object.DestroyImmediate(texture);
            }
            _memory.Clear();
        }

        public void Dispose() => Clear();

        private static string PathFor(string key)
        {
            return Path.Combine(Directory, Hash128.Compute(key) + ".png");
        }
    }
}
