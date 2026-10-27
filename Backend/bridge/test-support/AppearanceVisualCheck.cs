using System;
using System.IO;
using System.Threading.Tasks;
using Seyra.SceneTools;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;

public static class AppearanceVisualCheck
{
    public static async Task<string> Run()
    {
        var root=new GameObject("__AppearanceVisualCheck");
        var origin=new Vector3(1000,1000,1000);root.transform.position=origin;
        var previousBackground=Application.runInBackground;Application.runInBackground=true;
        var target=new RenderTexture(1440,720,24,RenderTextureFormat.ARGB32);
        var previous=RenderTexture.active;
        Texture2D image=null;
        try
        {
            GameObject Child(string name,Vector3 position)
            {
                var go=new GameObject(name);go.transform.SetParent(root.transform,false);go.transform.localPosition=position;go.layer=31;return go;
            }
            var camera=Child("Camera",new Vector3(0,0,-12)).AddComponent<Camera>();
            camera.orthographic=true;camera.orthographicSize=3;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.055f,.065f,.085f,1);camera.cullingMask=1<<31;camera.targetTexture=target;
            var light=Child("Light",Vector3.zero).AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.4f;light.cullingMask=1<<31;light.transform.rotation=Quaternion.Euler(35,-25,0);
            var names=new[]{"MatteOpaque","ColoredTranslucent","Unlit","UiUnlit","Plastic","Transparent"};
            GameObject transparentObject=null;
            for(int i=0;i<6;i++)
            {
                int col=i%3,row=i/3;float x=(col-1)*3.7f,y=row==0?1.4f:-1.4f;
                for(int n=0;n<6;n++)
                {
                    var stripe=GameObject.CreatePrimitive(PrimitiveType.Quad);stripe.name="Background stripe";stripe.layer=31;stripe.transform.SetParent(root.transform,false);
                    stripe.transform.localPosition=new Vector3(x-1.05f+n*.42f,y,1.2f);stripe.transform.localScale=new Vector3(.42f,1.8f,1);
                    stripe.AddComponent<ManagedAppearance>().SetAppearance(AppearanceStyle.Unlit,n%2==0?new Color(.22f,.26f,.32f,1):new Color(.65f,.68f,.73f,1));
                }
                var sphere=GameObject.CreatePrimitive(PrimitiveType.Sphere);sphere.name=names[i];sphere.layer=31;sphere.transform.SetParent(root.transform,false);sphere.transform.localPosition=new Vector3(x,y,0);sphere.transform.localScale=Vector3.one*1.65f;
                if(i==5)transparentObject=sphere;
                var alpha=i==1||i==3||i==5?.35f:1;
                sphere.AddComponent<ManagedAppearance>().SetAppearance((AppearanceStyle)i,new Color(.15f,.55f,.9f,alpha));
                // UiUnlit is exercised on a real Canvas Graphic below; retain the swatch as a quad-compatible surface.
                var label=Child("Label "+names[i],new Vector3(x,y-1.12f,0)).AddComponent<TextMesh>();label.text=names[i];label.anchor=TextAnchor.MiddleCenter;label.alignment=TextAlignment.Center;label.characterSize=.12f;label.fontSize=36;label.color=Color.white;
            }
            var canvas=Child("WorldCanvas",new Vector3(4.75f,-1.2f,-.5f)).AddComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.worldCamera=camera;canvas.GetComponent<RectTransform>().sizeDelta=new Vector2(100,160);canvas.transform.localScale=Vector3.one*.01f;
            var mask=new GameObject("SoftRectMask",typeof(RectTransform),typeof(RectMask2D));mask.layer=31;mask.transform.SetParent(canvas.transform,false);mask.GetComponent<RectTransform>().sizeDelta=new Vector2(80,130);mask.GetComponent<RectMask2D>().softness=new Vector2Int(8,8);
            var panel=new GameObject("ClippedGraphic",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image),typeof(ManagedAppearance));panel.layer=31;panel.transform.SetParent(mask.transform,false);panel.GetComponent<RectTransform>().sizeDelta=new Vector2(160,200);panel.GetComponent<ManagedAppearance>().SetAppearance(AppearanceStyle.UiUnlit,new Color(1,.3f,.1f,.7f));
            Canvas.ForceUpdateCanvases();
            var initialFrame=Time.frameCount;await Task.Delay(300);
            if(Time.frameCount<=initialFrame)throw new Exception("Visual check cannot run: player frames are not advancing.");
            camera.Render();RenderTexture.active=target;
            image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();
            var path=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Backend/validation/03-appearance-preview.png"));Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,image.EncodeToPNG());
            transparentObject.GetComponent<ManagedAppearance>().SetAppearance(AppearanceStyle.Transparent,new Color(.15f,.55f,.9f,0));
            camera.Render();image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();var invisible=image.GetPixels32();
            transparentObject.GetComponent<Renderer>().enabled=false;
            camera.Render();image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();var hidden=image.GetPixels32();
            for(int pixel=0;pixel<invisible.Length;pixel++)if(!invisible[pixel].Equals(hidden[pixel]))throw new Exception("Alpha zero still changes rendered pixels.");
            return "PASS: alpha zero matches an undrawn object pixel-for-pixel; advancing Play Mode rendered six style swatches and a soft-clipped world Canvas. Preview: "+path;
        }
        finally
        {
            RenderTexture.active=previous;
            Object.DestroyImmediate(root);target.Release();Object.DestroyImmediate(target);if(image!=null)Object.DestroyImmediate(image);
            Application.runInBackground=previousBackground;
        }
    }
}
