// Copyright (c) Eitan
// SPDX-License-Identifier: MIT or your preferred license

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace EasyMic.Editor.Integrations
{
    /// <summary>
    /// Manages optional dependencies by detecting the presence of other plugins/assemblies.
    /// It automatically adds or removes scripting define symbols based on whether a specific core type from another assembly exists.
    /// This script runs automatically when the Unity Editor launches or after scripts are recompiled.
    /// </summary>
    [InitializeOnLoad]
    public static class OptionalDependencyManager
    {
        /// <summary>
        /// Defines the configuration for a single optional integration.
        /// </summary>
        private readonly struct IntegrationDefinition
        {
            /// <summary>
            /// The scripting define symbol to add/remove. e.g., "EASYMIC_SHERPA_ONNX_INTEGRATION".
            /// </summary>
            public readonly string SymbolToDefine;

            /// <summary>
            /// The full name of a core type within the optional assembly to check for.
            /// This is the most reliable way to detect if a plugin is present.
            /// e.g., "Eitan.SherpaOnnxUnity.OnlineRecognizer".
            /// </summary>
            public readonly string CoreTypeFullName;

            public IntegrationDefinition(string symbolToDefine, string coreTypeFullName)
            {
                SymbolToDefine = symbolToDefine;
                CoreTypeFullName = coreTypeFullName;
            }
        }

        /// <summary>
        /// A list of all optional integrations to manage.
        /// To support a new integration, simply add a new IntegrationDefinition to this list.
        /// </summary>
        private static readonly List<IntegrationDefinition> s_IntegrationDefinitions = new List<IntegrationDefinition>
        {
            // --- Configuration for SherpaOnnxUnity Integration ---
            new IntegrationDefinition(
                symbolToDefine: "EASYMIC_SHERPA_ONNX_INTEGRATION",
                // 重要：请确认 SherpaOnnx 插件的核心类及其完整的命名空间。
                // 根据您提供的信息，OnlineRecognizer 应该位于 Eitan.SherpaOnnxUnity.Runtime.Integration 命名空间下。
                // 如果不是，请修改为正确的类型全名。
                coreTypeFullName: "Eitan.SherpaOnnxUnity.Runtime.Integration.SherpaOnnxAnchor"
            ),

            // --- Configuration for EasyMic APM (3A) Integration ---
            // We use an anchor type to detect the presence of the APM package.
            // Please ensure the APM package defines this type; otherwise, update the name below
            // to match the actual anchor type in the APM assembly.
            new IntegrationDefinition(
                symbolToDefine: "EASYMIC_APM_INTEGRATION",
                coreTypeFullName: "Eitan.EasyMic.Apm.Runtime.Integration.EasyMicApmAnchor"
            ),

            // To add another integration, add a new entry here. For example:
            // new IntegrationDefinition(
            //     symbolToDefine: "EASYMIC_ANOTHER_PLUGIN_INTEGRATION",
            //     coreTypeFullName: "Another.Plugin.Core.MainClass"
            // )
        };

        /// <summary>
        /// Static constructor called by Unity on editor launch and after script compilation.
        /// </summary>
        static OptionalDependencyManager()
        {
            // Defer the call to ensure all assemblies are loaded and the build pipeline is ready.
            EditorApplication.delayCall += UpdateAllSymbols;
        }

        /// <summary>
        /// Iterates through all integration definitions and updates their corresponding scripting define symbols.
        /// </summary>
        private static void UpdateAllSymbols()
        {
            foreach (var definition in s_IntegrationDefinitions)
            {
                bool dependencyExists = DoesTypeExist(definition.CoreTypeFullName);
                SetSymbolState(definition.SymbolToDefine, dependencyExists);
            }
        }

        /// <summary>
        /// Sets the state (added/removed) of a specific symbol across all valid build target groups.
        /// </summary>
        /// <param name="symbol">The symbol to manage.</param>
        /// <param name="shouldExist">True if the symbol should be present, false if it should be removed.</param>
        private static void SetSymbolState(string symbol, bool shouldExist)
        {
            foreach (BuildTargetGroup group in Enum.GetValues(typeof(BuildTargetGroup)))
            {
                if (!IsValidBuildTargetGroup(group)) continue;

#pragma warning disable CS0618 // Type or member is obsolete
                var definesString = PlayerSettings.GetScriptingDefineSymbolsForGroup(group);
#pragma warning restore CS0618 // Type or member is obsolete
                var allDefines = definesString.Split(';').Where(d => !string.IsNullOrWhiteSpace(d)).ToList();
                
                bool alreadyDefined = allDefines.Contains(symbol);

                if (shouldExist && !alreadyDefined)
                {
                    allDefines.Add(symbol);
#pragma warning disable CS0618 // Type or member is obsolete
                    PlayerSettings.SetScriptingDefineSymbolsForGroup(group, string.Join(";", allDefines));
#pragma warning restore CS0618 // Type or member is obsolete
                    UnityEngine.Debug.Log($"[OptionalDependencyManager] Enabled integration by adding '{symbol}' for {group}.");
                }
                else if (!shouldExist && alreadyDefined)
                {
                    allDefines.Remove(symbol);
#pragma warning disable CS0618 // Type or member is obsolete
                    PlayerSettings.SetScriptingDefineSymbolsForGroup(group, string.Join(";", allDefines));
#pragma warning restore CS0618 // Type or member is obsolete
                    UnityEngine.Debug.Log($"[OptionalDependencyManager] Disabled integration by removing '{symbol}' for {group}.");
                }
            }
        }

        /// <summary>
        /// Checks if a type with the given full name exists in any of the currently loaded assemblies.
        /// </summary>
        /// <param name="fullTypeName">The full name of the type, including its namespace.</param>
        /// <returns>True if the type is found, otherwise false.</returns>
        private static bool DoesTypeExist(string fullTypeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetType(fullTypeName, false) != null) // Use 'false' to not throw an exception on failure
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Verifies that a BuildTargetGroup is a valid, non-obsolete target for setting defines.
        /// </summary>
        private static bool IsValidBuildTargetGroup(BuildTargetGroup group)
        {
            // Filter out unknown or obsolete groups to prevent errors/warnings in different Unity versions.
            if (group == BuildTargetGroup.Unknown) return false;

            var field = typeof(BuildTargetGroup).GetField(group.ToString());
            return field != null && !Attribute.IsDefined(field, typeof(ObsoleteAttribute));
        }
    }
}
