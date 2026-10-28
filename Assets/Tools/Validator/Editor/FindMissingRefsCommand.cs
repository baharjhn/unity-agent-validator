using System;
using System.Collections.Generic;
using Unity.Pipeline.Commands;

namespace Unity.Validator.Editor
{
    static class FindMissingRefsCommand
    {
        /// <summary>Result of <c>unity command find-missing-refs</c>; Pipeline serializes it to JSON.</summary>
        [Serializable]
        internal class FindMissingRefsResult
        {
            public int count;
            public List<MissingReferenceIssue> issues;
        }

        [CliCommand("find-missing-refs", "List missing references in prefabs")]
        static FindMissingRefsResult FindMissingRefs()
        {
            var issues = MissingReferenceScanner.ScanAllPrefabs();
            return new FindMissingRefsResult { count = issues.Count, issues = issues };
        }
    }
}
