using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    internal static class LipSyncPortraits
    {
        public static Texture2D Capture(GameObject root, LipSyncTarget target)
        {
            if (!root || !target.mesh) throw new InvalidOperationException("顔メッシュを登録してください。");
            using (var preview = new FacePreview(root))
            {
                preview.ConfigurePortrait(target);
                return preview.RenderStatic(256);
            }
        }

        public static Texture2D Compose(Texture2D[] photos)
        {
            if (photos.Length == 0) return Symbol(false);
            var rt = RenderTexture.GetTemporary(256,256,0,RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active=rt;GL.Clear(true,true,Color.clear);
                GL.PushMatrix();
                try {
                    GL.LoadPixelMatrix(0,256,256,0);
                    int cols=Mathf.CeilToInt(Mathf.Sqrt(photos.Length)),rows=Mathf.CeilToInt((float)photos.Length/cols);
                    float size=256f/cols;
                    for(int i=0;i<photos.Length;i++)if(photos[i])Graphics.DrawTexture(new Rect(i%cols*size,(256-rows*size)/2+i/cols*size,size,size),photos[i]);
                } finally { GL.PopMatrix(); }
                var image=new Texture2D(256,256,TextureFormat.RGBA32,false);
                image.ReadPixels(new Rect(0,0,256,256),0,0);image.Apply();return image;
            } finally { RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt); }
        }

        public static Texture2D Symbol(bool auto)
        {
            var image=new Texture2D(256,256,TextureFormat.RGBA32,false);
            var pixels=new Color[256*256];
            for(int y=0;y<256;y++)for(int x=0;x<256;x++)
            {
                float dx=x-128,dy=y-128,r=Mathf.Sqrt(dx*dx+dy*dy);
                bool ink=auto ? (r>65&&r<82&&Mathf.Abs(dy)>15)||(x>184&&x<218&&y>112&&y<145)||(x>38&&x<72&&y>111&&y<144)
                    : (Mathf.Abs(dy)<10&&Mathf.Abs(dx)<64)||(r>83&&r<96);
                pixels[y*256+x]=ink?Color.white:Color.clear;
            }
            image.SetPixels(pixels);image.Apply();return image;
        }

        public static Texture2D Save(Texture2D image,string label)
        {
            var folder=AssetPathUtility.EnsureFolder("Assets", "NSL_FacialExpressionEditor", "Icons", "LipSync");
            var safe=new string(label.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c).ToArray());
            var path=AssetPathUtility.UniqueAssetPath(folder+"/"+(string.IsNullOrWhiteSpace(safe)?"Icon":safe)+".png");
            File.WriteAllBytes(path,image.EncodeToPNG());AssetDatabase.ImportAsset(path);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.alphaIsTransparency=true;importer.mipmapEnabled=false;importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.maxTextureSize=256;importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
