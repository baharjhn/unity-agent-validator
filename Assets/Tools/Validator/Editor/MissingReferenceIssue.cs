using System;

namespace Unity.Validator.Editor
{
    /// <summary>
    /// One broken link found in a prefab: a missing script or a missing object reference.
    /// Field names are camelCase because they are serialized to JSON as-is.
    /// </summary>
    [Serializable]
    public class MissingReferenceIssue
    {
        /// <summary>Project path of the prefab, e.g. <c>Assets/Prefabs/Enemy.prefab</c>.</summary>
        public string assetPath;

        /// <summary>Hierarchy path from the prefab root, e.g. <c>Enemy/Body/Mesh</c>.</summary>
        public string objectPath;

        /// <summary>What is missing, e.g. <c>MeshRenderer.m_Materials.Array.data[0] is missing</c>.</summary>
        public string description;

        public MissingReferenceIssue(string assetPath, string objectPath, string description)
        {
            this.assetPath = assetPath;
            this.objectPath = objectPath;
            this.description = description;
        }

        public override string ToString() => $"{assetPath} [{objectPath}]: {description}";
    }
}
