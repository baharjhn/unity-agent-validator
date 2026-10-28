using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.Validator.Editor.Tests
{
    public class MissingReferenceScannerTests
    {
        const string k_TempFolder = "Assets/ValidatorTestTemp";

        // A GUID no script in the project has, so components pointing at it load as missing scripts.
        const string k_MissingScriptGuid = "0badc0de0badc0de0badc0de0badc0de";

        GameObject m_SceneObject;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(k_TempFolder))
                AssetDatabase.CreateFolder("Assets", "ValidatorTestTemp");
        }

        [TearDown]
        public void TearDown()
        {
            if (m_SceneObject != null)
                Object.DestroyImmediate(m_SceneObject);
            AssetDatabase.DeleteAsset(k_TempFolder);
        }

        // ---- In-memory: ScanGameObject ----

        [Test]
        public void ScanGameObject_CleanObject_ReportsNothing()
        {
            m_SceneObject = new GameObject("Clean");
            m_SceneObject.AddComponent<MeshRenderer>();

            Assert.That(MissingReferenceScanner.ScanGameObject(m_SceneObject, "test"), Is.Empty);
        }

        [Test]
        public void ScanGameObject_UnassignedReference_IsNotReported()
        {
            m_SceneObject = new GameObject("Unassigned");
            m_SceneObject.AddComponent<MeshFilter>().sharedMesh = null;

            Assert.That(MissingReferenceScanner.ScanGameObject(m_SceneObject, "test"), Is.Empty);
        }

        [Test]
        public void ScanGameObject_DestroyedReference_IsReported()
        {
            m_SceneObject = new GameObject("Root");
            AddRendererWithDestroyedMaterial(m_SceneObject);

            var issues = MissingReferenceScanner.ScanGameObject(m_SceneObject, "test");

            Assert.That(issues, Has.Count.EqualTo(1));
            Assert.That(issues[0].assetPath, Is.EqualTo("test"));
            Assert.That(issues[0].objectPath, Is.EqualTo("Root"));
            Assert.That(issues[0].description, Does.StartWith("MeshRenderer.m_Materials"));
        }

        [Test]
        public void ScanGameObject_DestroyedReferenceOnInactiveNestedChild_ReportsFullPath()
        {
            m_SceneObject = new GameObject("Root");
            var middle = new GameObject("Middle");
            middle.transform.SetParent(m_SceneObject.transform);
            var leaf = new GameObject("Leaf");
            leaf.transform.SetParent(middle.transform);
            leaf.SetActive(false);
            AddRendererWithDestroyedMaterial(leaf);

            var issues = MissingReferenceScanner.ScanGameObject(m_SceneObject, "test");

            Assert.That(issues, Has.Count.EqualTo(1));
            Assert.That(issues[0].objectPath, Is.EqualTo("Root/Middle/Leaf"));
        }

        // ---- Prefab assets: ScanPrefab / ScanAllPrefabs ----

        [Test]
        public void ScanPrefab_ReferenceToDeletedAsset_IsReported()
        {
            var materialPath = $"{k_TempFolder}/Temp.mat";
            AssetDatabase.CreateAsset(new Material(Shader.Find("Hidden/InternalErrorShader")), materialPath);

            m_SceneObject = new GameObject("WithMaterial");
            m_SceneObject.AddComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            var prefabPath = $"{k_TempFolder}/WithMaterial.prefab";
            PrefabUtility.SaveAsPrefabAsset(m_SceneObject, prefabPath);
            AssetDatabase.DeleteAsset(materialPath);

            var issues = MissingReferenceScanner.ScanPrefab(prefabPath);

            Assert.That(issues, Has.Count.EqualTo(1));
            Assert.That(issues[0].assetPath, Is.EqualTo(prefabPath));
            Assert.That(issues[0].objectPath, Is.EqualTo("WithMaterial"));
            Assert.That(issues[0].description, Does.StartWith("MeshRenderer.m_Materials"));
        }

        [Test]
        public void ScanPrefab_MissingScriptOnRoot_IsReported()
        {
            var prefabPath = WriteMissingScriptPrefab(
                new Node("RootScript", active: true, missingScript: true));

            var issues = MissingReferenceScanner.ScanPrefab(prefabPath);

            Assert.That(issues, Has.Count.EqualTo(1));
            Assert.That(issues[0].objectPath, Is.EqualTo("RootScript"));
            Assert.That(issues[0].description, Does.StartWith(MissingReferenceScanner.MissingScriptDescription));
        }

        [Test]
        public void ScanPrefab_MissingScriptOnInactiveNestedChild_IsReported()
        {
            var prefabPath = WriteMissingScriptPrefab(
                new Node("NestedScript", active: true, missingScript: false),
                new Node("Middle", active: true, missingScript: false),
                new Node("Leaf", active: false, missingScript: true));

            var issues = MissingReferenceScanner.ScanPrefab(prefabPath);

            Assert.That(issues, Has.Count.EqualTo(1));
            Assert.That(issues[0].assetPath, Is.EqualTo(prefabPath));
            Assert.That(issues[0].objectPath, Is.EqualTo("NestedScript/Middle/Leaf"));
            Assert.That(issues[0].description, Does.StartWith(MissingReferenceScanner.MissingScriptDescription));
        }

        [Test]
        public void ScanPrefab_MissingScriptUnderInactiveParent_IsReported()
        {
            var prefabPath = WriteMissingScriptPrefab(
                new Node("InactiveParent", active: true, missingScript: false),
                new Node("Hidden", active: false, missingScript: false),
                new Node("Leaf", active: true, missingScript: true));

            var issues = MissingReferenceScanner.ScanPrefab(prefabPath);

            Assert.That(issues.Select(i => i.objectPath), Is.EqualTo(new[] { "InactiveParent/Hidden/Leaf" }));
        }

        [Test]
        public void ScanAllPrefabs_FindsIssuesOnlyInBrokenPrefabs()
        {
            m_SceneObject = new GameObject("Clean");
            PrefabUtility.SaveAsPrefabAsset(m_SceneObject, $"{k_TempFolder}/Clean.prefab");
            var brokenPath = WriteMissingScriptPrefab(
                new Node("Broken", active: true, missingScript: true));

            var issues = MissingReferenceScanner.ScanAllPrefabs(new[] { k_TempFolder });

            Assert.That(issues.Select(i => i.assetPath), Is.EqualTo(new[] { brokenPath }));
        }

        [Test]
        public void ScanPrefab_InvalidPath_ReportsNothing()
        {
            Assert.That(MissingReferenceScanner.ScanPrefab($"{k_TempFolder}/DoesNotExist.prefab"), Is.Empty);
        }

        // ---- Helpers ----

        static void AddRendererWithDestroyedMaterial(GameObject gameObject)
        {
            var material = new Material(Shader.Find("Hidden/InternalErrorShader"));
            gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
            Object.DestroyImmediate(material);
        }

        readonly struct Node
        {
            public readonly string Name;
            public readonly bool Active;
            public readonly bool MissingScript;

            public Node(string name, bool active, bool missingScript)
            {
                Name = name;
                Active = active;
                MissingScript = missingScript;
            }
        }

        /// <summary>
        /// Writes a prefab whose nodes form a chain (each node is the parent of the next) and imports it.
        /// A missing script can't be added through the API, so the prefab is written as YAML with a
        /// MonoBehaviour pointing at <see cref="k_MissingScriptGuid"/>. The file is named after the
        /// first node because a loaded prefab's root always takes the file name.
        /// </summary>
        static string WriteMissingScriptPrefab(params Node[] nodes)
        {
            long GameObjectId(int i) => 1000 + i * 10;
            long TransformId(int i) => 1001 + i * 10;
            long BehaviourId(int i) => 1002 + i * 10;

            var yaml = new StringBuilder();
            yaml.Append("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
            for (var i = 0; i < nodes.Length; i++)
            {
                var node = nodes[i];
                yaml.Append($"--- !u!1 &{GameObjectId(i)}\nGameObject:\n");
                yaml.Append("  m_ObjectHideFlags: 0\n  serializedVersion: 6\n  m_Component:\n");
                yaml.Append($"  - component: {{fileID: {TransformId(i)}}}\n");
                if (node.MissingScript)
                    yaml.Append($"  - component: {{fileID: {BehaviourId(i)}}}\n");
                yaml.Append($"  m_Layer: 0\n  m_Name: {node.Name}\n  m_TagString: Untagged\n");
                yaml.Append($"  m_IsActive: {(node.Active ? 1 : 0)}\n");

                yaml.Append($"--- !u!4 &{TransformId(i)}\nTransform:\n");
                yaml.Append($"  m_ObjectHideFlags: 0\n  m_GameObject: {{fileID: {GameObjectId(i)}}}\n  serializedVersion: 2\n");
                yaml.Append("  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}\n  m_LocalPosition: {x: 0, y: 0, z: 0}\n  m_LocalScale: {x: 1, y: 1, z: 1}\n");
                yaml.Append(i + 1 < nodes.Length ? $"  m_Children:\n  - {{fileID: {TransformId(i + 1)}}}\n" : "  m_Children: []\n");
                yaml.Append($"  m_Father: {{fileID: {(i == 0 ? 0 : TransformId(i - 1))}}}\n");

                if (node.MissingScript)
                {
                    yaml.Append($"--- !u!114 &{BehaviourId(i)}\nMonoBehaviour:\n");
                    yaml.Append($"  m_ObjectHideFlags: 0\n  m_GameObject: {{fileID: {GameObjectId(i)}}}\n  m_Enabled: 1\n");
                    yaml.Append($"  m_Script: {{fileID: 11500000, guid: {k_MissingScriptGuid}, type: 3}}\n");
                    yaml.Append("  m_Name: \n  m_EditorClassIdentifier: \n");
                }
            }

            var path = $"{k_TempFolder}/{nodes[0].Name}.prefab";
            File.WriteAllText(path, yaml.ToString());
            // Unity may log about the unresolvable script while importing; that is the point of the test.
            LogAssert.ignoreFailingMessages = true;
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return path;
        }
    }
}
