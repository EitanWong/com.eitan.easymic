using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

// Unity 2021 predates the scene lifecycle required by current iOS SDKs.
public static class EasyMicScenePostprocessor
{
    private const string Marker = "// EasyMic UIScene bridge for the Unity 2021.3 trampoline.";
    private const string SupportedUnityVersion = "2021.3.45f2";

    [PostProcessBuild(1000)]
    public static void OnPostprocessBuild(BuildTarget target, string path)
    {
        if (target != BuildTarget.iOS || !string.Equals(Application.unityVersion, SupportedUnityVersion, StringComparison.Ordinal))
            return;

        var controllerPath = Path.Combine(path, "Classes/UnityAppController.mm");
        var plistPath = Path.Combine(path, "Info.plist");
        var source = File.ReadAllText(controllerPath).Replace("\r\n", "\n");
        var plist = new PlistDocument();
        plist.ReadFromFile(plistPath);
        PlistElement manifest;
        // Existing scene setups belong to the project or another plugin; leave them untouched.
        if (plist.root.values.TryGetValue("UIApplicationSceneManifest", out manifest))
            return;

        if (!source.Contains(Marker))
        {
            const string launchAnchor = "    [self initUnityWithApplication: application];\n    return YES;\n}";
            const string initAnchor = "- (void)initUnityWithApplication:(UIApplication*)application\n{\n";
            const string windowAnchor = "#if (PLATFORM_IOS && defined(__IPHONE_13_0)) || (PLATFORM_TVOS && defined(__TVOS_13_0))\n    if (@available(iOS 13, tvOS 13, *))\n        _window = [[UIWindow alloc] initWithWindowScene: [self pickStartupWindowScene: application.connectedScenes]];\n    else\n#endif\n    _window = [[UIWindow alloc] initWithFrame: [UIScreen mainScreen].bounds];";
            if (!HasSingleOccurrence(source, launchAnchor) || !HasSingleOccurrence(source, initAnchor) ||
                !HasSingleOccurrence(source, windowAnchor))
            {
                Debug.LogWarning("EasyMic UIScene bridge skipped: generated UnityAppController does not match the verified Unity 2021.3.45f2 trampoline.");
                return;
            }

            source = ReplaceOnce(source,
                launchAnchor,
                "    // Scene connection supplies the UIWindowScene before graphics startup.\n" +
                "    if ([NSBundle.mainBundle objectForInfoDictionaryKey:@\"UIApplicationSceneManifest\"] == nil)\n" +
                "        [self initUnityWithApplication: application];\n    return YES;\n}");

            source = ReplaceOnce(source,
                initAnchor,
                "#if PLATFORM_IOS\n" +
                "- (void)easymicConnectWindowScene:(UIWindowScene*)scene\n{\n" +
                "    if (_window == nil)\n        _window = [[UIWindow alloc] initWithWindowScene:scene];\n" +
                "    else\n        _window.windowScene = scene;\n}\n#endif\n\n" +
                "- (void)initUnityWithApplication:(UIApplication*)application\n{\n" +
                "    // Background launches must wait for a scene/window connection.\n" +
                "    if ([NSBundle.mainBundle objectForInfoDictionaryKey:@\"UIApplicationSceneManifest\"] != nil && _window == nil)\n" +
                "        return;\n");

            source = ReplaceOnce(source, windowAnchor,
                "    if (_window == nil)\n    {\n" + windowAnchor + "\n    }");

            var scriptPath = AssetDatabase.GUIDToAssetPath(FindScriptGuid());
            var delegatePath = Path.Combine(Path.GetDirectoryName(scriptPath), "EasyMicSceneDelegate.mm.txt");
            source += "\n" + File.ReadAllText(delegatePath);
        }

        var sceneManifest = plist.root.CreateDict("UIApplicationSceneManifest");
        sceneManifest.SetBoolean("UIApplicationSupportsMultipleScenes", false);
        var configuration = sceneManifest.CreateDict("UISceneConfigurations")
            .CreateArray("UIWindowSceneSessionRoleApplication").AddDict();
        configuration.SetString("UISceneConfigurationName", "EasyMic");
        configuration.SetString("UISceneDelegateClassName", "EasyMicSceneDelegate");
        configuration.SetString("UISceneClassName", "UIWindowScene");
        File.WriteAllText(controllerPath, source);
        plist.WriteToFile(plistPath);
    }

    private static string FindScriptGuid()
    {
        foreach (var guid in AssetDatabase.FindAssets("EasyMicScenePostprocessor t:MonoScript"))
            if (Path.GetFileName(AssetDatabase.GUIDToAssetPath(guid)) == "EasyMicScenePostprocessor.cs")
                return guid;
        throw new BuildFailedException("Cannot locate the EasyMic UIScene bridge template.");
    }

    private static string ReplaceOnce(string source, string before, string after)
    {
        var index = source.IndexOf(before, StringComparison.Ordinal);
        if (index < 0 || source.IndexOf(before, index + before.Length, StringComparison.Ordinal) >= 0)
            throw new BuildFailedException("Unexpected Unity 2021 iOS trampoline. UIScene patch was not applied; inspect UnityAppController.mm.");
        return source.Substring(0, index) + after + source.Substring(index + before.Length);
    }

    private static bool HasSingleOccurrence(string source, string value)
    {
        var index = source.IndexOf(value, StringComparison.Ordinal);
        return index >= 0 && source.IndexOf(value, index + value.Length, StringComparison.Ordinal) < 0;
    }
}
