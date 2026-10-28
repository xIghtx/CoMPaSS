#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;

namespace CoMPaSS.Editor
{
    public static class MapfBuild
    {
        [MenuItem("CoMPaSS/Build Windows")]
        public static void BuildWindows()
        {
            string root = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
            string destination = Environment.GetEnvironmentVariable("COMPASS_BUILD_OUTPUT");
            if (string.IsNullOrEmpty(destination)) destination = Path.Combine(root, "Builds", "Windows", "CoMPaSS.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled && File.Exists(s.path)).Select(s => s.path).ToArray();
            if (scenes.Length == 0) scenes = AssetDatabase.FindAssets("t:Scene").Select(AssetDatabase.GUIDToAssetPath).Where(s=>s.StartsWith("Assets/",StringComparison.Ordinal)).Take(1).ToArray();
            if (scenes.Length == 0)
            {
                Directory.CreateDirectory("Assets/Scenes");
                var scene=EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects,NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene,"Assets/Scenes/CoMPaSS.unity");scenes=new[]{"Assets/Scenes/CoMPaSS.unity"};
            }
            PlayerSettings.defaultScreenWidth=1600;PlayerSettings.defaultScreenHeight=1000;
            PlayerSettings.resizableWindow=true;PlayerSettings.fullScreenMode=UnityEngine.FullScreenMode.Windowed;
            PlayerSettings.runInBackground=true;
            BuildReport result = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            { scenes=scenes,locationPathName=destination,target=BuildTarget.StandaloneWindows64,options=BuildOptions.None });
            if (result.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("CoMPaSS build failed: " + result.summary.result);
            string toolsSource=Path.Combine(root,"Tools","Primal"),toolsTarget=Path.Combine(Path.GetDirectoryName(destination),"Tools","Primal");
            Directory.CreateDirectory(toolsTarget);
            foreach(string name in new[]{"primal_adapter.py","setup_primal.ps1","README.md","config.example.json","environment.yml"})
                if(File.Exists(Path.Combine(toolsSource,name)))File.Copy(Path.Combine(toolsSource,name),Path.Combine(toolsTarget,name),true);
            UnityEngine.Debug.Log("COMPASS_BUILD_SUCCESS " + destination);
        }
    }
}
#endif
