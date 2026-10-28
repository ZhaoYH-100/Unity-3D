using System;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace UnityInspection.EditorTools
{
    public static class InspectionBuild
    {
        public static void BuildWindows()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(InspectionSceneBuilder.ScenePath) == null)
            {
                throw new InvalidOperationException(
                    $"Scene not found at {InspectionSceneBuilder.ScenePath}. Run the scene builder first.");
            }

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = new[] { InspectionSceneBuilder.ScenePath },
                locationPathName = "Builds/Windows/UnityInspection.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Windows build failed: {report.summary.result}, {report.summary.totalErrors} error(s).");
            }

            Debug.Log(
                $"Windows build succeeded: {report.summary.outputPath}, " +
                $"{report.summary.totalSize} bytes, {report.summary.totalTime}.");
        }
    }
}
