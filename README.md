# Unity Agent Validator

An Editor tool that finds **missing scripts** and **missing object references** in a Unity project's prefabs. It is exposed as a Unity Pipeline (`com.unity.pipeline`) command so the Unity CLI, or an AI agent driving the Editor, can call it and read the results as JSON.

```
$ unity command find-missing-refs --result-only
{
  "count": 2,
  "issues": [
    {
      "assetPath": "Assets/Demo/BrokenMatCube.prefab",
      "objectPath": "BrokenMatCube",
      "description": "MeshRenderer.m_Materials.Array.data[0] is missing"
    },
    {
      "assetPath": "Assets/Demo/BrokenScriptSphare.prefab",
      "objectPath": "BrokenScriptSphare",
      "description": "Missing script (component index 4)"
    }
  ]
}
```

## Requirements

| | Version |
|---|---|
| Unity Editor | 6000.6.6f1 |
| `com.unity.pipeline` | 0.9.1-exp.1 |
| `com.unity.test-framework` | 1.8.0 (for the tests) |
| Unity CLI (`unity`) | 1.0.0-beta.14 or later, to run the command from a terminal |

The project uses the Universal Render Pipeline (URP).

## Getting started

1. Clone the repository and open the project in Unity 6000.6.6f1.
2. Wait for the Editor to finish importing, then check that the CLI can reach it:
   ```bash
   unity status
   ```
   Look for the state `ready`.
3. Run the validator:
   ```bash
   unity command find-missing-refs
   ```
   Add `--result-only` to print only the JSON result, without the CLI's response envelope.

## What it detects

| Issue | How it is found | Example description |
|---|---|---|
| Missing script | A component slot whose script can't be loaded comes back as `null` from `GetComponents<Component>()`. | `Missing script (component index 4)` |
| Missing object reference | Each component is read through a `SerializedObject`. An `ObjectReference` property is reported when its `objectReferenceValue` is `null` but its `objectReferenceEntityIdValue` is not `EntityId.None`: a reference was assigned, but its target no longer exists. This is the field the Inspector shows as *Missing*. | `MeshRenderer.m_Materials.Array.data[0] is missing` |

Fields that were never assigned (`None` in the Inspector) are **not** reported.

Every GameObject in a prefab is checked, including inactive ones and children of inactive parents. The scan only reads assets; it never modifies or saves them.

> **Unity 6.6 note:** `SerializedProperty.objectReferenceInstanceIDValue` is obsolete as an error in this version, so the scanner uses `objectReferenceEntityIdValue` instead. The check is equivalent.

### Output fields

| Field | Meaning |
|---|---|
| `count` | Number of issues found. |
| `issues[].assetPath` | Project path of the prefab. |
| `issues[].objectPath` | Hierarchy path from the prefab root, e.g. `Enemy/Body/Mesh`. The root takes the prefab's file name. |
| `issues[].description` | What is missing. |

## Using the scanner from C#

`MissingReferenceScanner` is a public static class in the `Unity.Validator.Editor` assembly (Editor only):

```csharp
using Unity.Validator.Editor;

// Every prefab under Assets/ (the default)
var issues = MissingReferenceScanner.ScanAllPrefabs();

// Prefabs under specific folders only
var demoIssues = MissingReferenceScanner.ScanAllPrefabs(new[] { "Assets/Demo" });

// A single prefab
var one = MissingReferenceScanner.ScanPrefab("Assets/Demo/BrokenMatCube.prefab");

// Any GameObject hierarchy, e.g. one built in memory
var inMemory = MissingReferenceScanner.ScanGameObject(someRoot, "label-for-assetPath");

foreach (var issue in issues)
    UnityEngine.Debug.LogWarning(issue); // "<assetPath> [<objectPath>]: <description>"
```

## Project layout

```
Assets/
├── Tools/Validator/
│   ├── Editor/                          Unity.Validator.Editor (Editor-only assembly)
│   │   ├── MissingReferenceScanner.cs   Scanning logic
│   │   ├── MissingReferenceIssue.cs     Issue model (assetPath, objectPath, description)
│   │   └── FindMissingRefsCommand.cs    The find-missing-refs Pipeline command
│   └── Tests/Editor/                    Unity.Validator.Editor.Tests (Edit Mode tests)
│       └── MissingReferenceScannerTests.cs
├── Demo/                                Deliberately broken prefabs for trying the tool
│   ├── BrokenMatCube.prefab             MeshRenderer pointing at a deleted material
│   └── BrokenScriptSphare.prefab        Component whose script no longer exists
└── Scenes/SampleScene.unity
```

## Running the tests

The Edit Mode tests use the Unity Test Framework (NUnit). They cover:

- clean objects and never-assigned fields (no false positives)
- references to destroyed objects and to deleted assets
- missing scripts on the prefab root, on an inactive nested child, and under an inactive parent
- `ScanAllPrefabs` reporting only the broken prefabs in a folder
- an invalid prefab path

The tests write temporary assets to `Assets/ValidatorTestTemp/` and delete it when they finish.

**With the Editor open**, run them through the Pipeline:

```bash
unity command test run --mode EditMode --filter Unity.Validator.Editor.Tests --filter_type assembly
```

**With the Editor closed** (for example in CI):

```bash
unity test . --mode EditMode --filter Unity.Validator.Editor.Tests
```

You can also run them from **Window → General → Test Runner → EditMode** in the Editor.

## Limitations

- **Prefabs only.** Scenes and ScriptableObject assets are not scanned.
- **`Assets/` only by default.** Prefabs inside packages are skipped unless you pass their folders to `ScanAllPrefabs`.
- **Nested prefabs can repeat issues.** A missing reference inherited from a nested prefab is reported once for every prefab that contains it.


## How it was built (agentic workflow)

I built this with Claude Code driving the Unity Editor through the Unity CLI and the Pipeline package, with me reviewing each step.

1. **Spec first.** I wrote a short spec (scanner, `find-missing-refs` command, Edit Mode tests) and asked the agent to show its plan before writing any code.
2. **Plan review and decisions.**
   - I chose a structured result model (`{ count, issues[] }`) over a JSON string, so an agent can read the fields directly instead of parsing text.
   - I asked for extra test coverage: missing scripts on an inactive nested child and under an inactive parent.
   - I kept scenes out of scope and approved the one change outside the tool folder (adding `com.unity.test-framework` to `manifest.json`).
3. **What the agent adapted.** My spec used `objectReferenceInstanceIDValue`, which is obsolete as an error in Unity 6.6. The agent noticed it didn't compile and switched to `objectReferenceEntityIdValue != EntityId.None`, which keeps the same logic.
4. **What the agent got wrong first.** 3 tests failed on the first run because of a wrong assumption in its test setup: when Unity loads a prefab, the root object takes the file's name. It fixed the setup after running the tests, not the scanner.
5. **What I verified myself.**
   - Ran `unity command find-missing-refs` on the clean project: `count: 0`. On its own that proves little, because the project had no prefabs.
   - Built two broken prefabs by hand (a deleted material, a deleted script) and ran the command again: it reported both, with correct paths. This was the first non-empty result and the real proof that the scanner works.
   - Ran the Edit Mode tests in the Test Runner.

## What I learned

- Agents work best with **structured feedback they can check against**: a command that returns JSON and a test suite let the agent (and me) verify the work instead of trusting it.
- **Reviewing the plan** caught the important decisions early (result format, test coverage, scope) before any code was written.
- An agent can adapt to API changes the spec didn't know about, but every change still needs a human to confirm the logic stays the same.
- "No issues found" isn't evidence until you've seen the tool find a real issue.
- For missing references there is **no safe automatic fix**: nobody knows what the deleted material or script was. A tool or agent should report and ask, not guess. That's why seeing and reviewing what an agent changed matters so much in the Editor.
