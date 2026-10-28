using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Unity.Validator.Editor
{
    /// <summary>
    /// Finds missing scripts and missing object references in prefabs. Read-only: nothing is
    /// modified or saved.
    /// </summary>
    public static class MissingReferenceScanner
    {
        public const string MissingScriptDescription = "Missing script";

        static readonly string[] k_DefaultSearchFolders = { "Assets" };

        /// <summary>
        /// Scans every prefab under <paramref name="searchInFolders"/> (default: <c>Assets</c>,
        /// so package prefabs are not included).
        /// </summary>
        public static List<MissingReferenceIssue> ScanAllPrefabs(string[] searchInFolders = null)
        {
            var issues = new List<MissingReferenceIssue>();
            var guids = AssetDatabase.FindAssets("t:Prefab", searchInFolders ?? k_DefaultSearchFolders);
            foreach (var guid in guids)
                issues.AddRange(ScanPrefab(AssetDatabase.GUIDToAssetPath(guid)));
            return issues;
        }

        /// <summary>Scans the prefab asset at <paramref name="assetPath"/>.</summary>
        public static List<MissingReferenceIssue> ScanPrefab(string assetPath)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            return root == null ? new List<MissingReferenceIssue>() : ScanGameObject(root, assetPath);
        }

        /// <summary>
        /// Scans <paramref name="root"/> and all of its children, inactive ones included.
        /// <paramref name="assetPath"/> is only copied into the reported issues.
        /// </summary>
        public static List<MissingReferenceIssue> ScanGameObject(GameObject root, string assetPath)
        {
            var issues = new List<MissingReferenceIssue>();
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                var objectPath = GetObjectPath(transform, root.transform);
                var components = transform.GetComponents<Component>();
                for (var i = 0; i < components.Length; i++)
                {
                    var component = components[i];
                    // A component whose script cannot be loaded comes back as null.
                    if (component == null)
                    {
                        issues.Add(new MissingReferenceIssue(assetPath, objectPath,
                            $"{MissingScriptDescription} (component index {i})"));
                        continue;
                    }

                    ScanComponent(component, assetPath, objectPath, issues);
                }
            }
            return issues;
        }

        static void ScanComponent(Component component, string assetPath, string objectPath, List<MissingReferenceIssue> issues)
        {
            using var serializedObject = new SerializedObject(component);
            var property = serializedObject.GetIterator();
            while (property.Next(true))
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference)
                    continue;

                // Null with a valid id means a reference was assigned but its target no longer exists
                // (shown as "Missing" in the Inspector). EntityId.None is simply unassigned.
                // EntityId replaces the instance id in Unity 6.6, where objectReferenceInstanceIDValue
                // is obsolete as an error.
                if (property.objectReferenceValue == null && property.objectReferenceEntityIdValue != EntityId.None)
                {
                    issues.Add(new MissingReferenceIssue(assetPath, objectPath,
                        $"{component.GetType().Name}.{property.propertyPath} is missing"));
                }
            }
        }

        static string GetObjectPath(Transform transform, Transform root)
        {
            var path = transform.name;
            while (transform != root && transform.parent != null)
            {
                transform = transform.parent;
                path = $"{transform.name}/{path}";
            }
            return path;
        }
    }
}
