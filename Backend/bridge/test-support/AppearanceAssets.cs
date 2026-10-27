using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class AppearanceAssets
{
    public static string CreateMaterials()
    {
        const string folder="Assets/Seyra/Materials";
        if(!AssetDatabase.IsValidFolder(folder))AssetDatabase.CreateFolder("Assets/Seyra","Materials");
        foreach(var name in new[]{"MatteOpaque","ColoredTranslucent","Unlit","UiUnlit","Plastic","Transparent"})
        {
            var shader=Resources.Load<Shader>("Seyra/Appearance/"+name);
            if(shader==null)throw new Exception("Missing shader: "+name);
            var path=folder+"/"+name+".mat";
            if(AssetDatabase.LoadAssetAtPath<Material>(path)!=null)continue;
            var material=new Material(shader){name=name};
            material.SetColor("_BaseColor",new Color(1,1,1,name=="Transparent"?0:name=="ColoredTranslucent"||name=="UiUnlit"?.5f:1));
            AssetDatabase.CreateAsset(material,path);
        }
        AssetDatabase.SaveAssets();
        return "Created six material assets in "+folder;
    }
    public static string BuildAndroidShaders()
    {
        if(Application.isPlaying)throw new Exception("Exit Play Mode before validating Android shaders.");
        if(!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android,BuildTarget.Android))throw new Exception("Android build support is not installed.");
        var directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.utmp/appearance-android"));
        Directory.CreateDirectory(directory);
        var assets=AssetDatabase.FindAssets("t:Shader",new[]{"Assets/Seyra/Resources/Seyra/Appearance"}).Select(AssetDatabase.GUIDToAssetPath).ToArray();
        if(assets.Length!=6)throw new Exception("Expected six shader assets.");
        var builds=new[]{new AssetBundleBuild{assetBundleName="seyra-appearance",assetNames=assets}};
        var manifest=BuildPipeline.BuildAssetBundles(directory,builds,BuildAssetBundleOptions.ForceRebuildAssetBundle|BuildAssetBundleOptions.StrictMode,BuildTarget.Android);
        if(manifest==null)throw new Exception("Android shader asset bundle compilation failed.");
        foreach(var shader in Resources.LoadAll<Shader>("Seyra/Appearance"))
            if(ShaderUtil.ShaderHasError(shader))throw new Exception(string.Join("\n",ShaderUtil.GetShaderMessages(shader).Select(m=>m.message)));
        return "PASS: six shaders compiled into Android asset bundle. APIs: "+string.Join(",",PlayerSettings.GetGraphicsAPIs(BuildTarget.Android).Select(a=>a.ToString()))+". Output: "+directory;
    }
}
