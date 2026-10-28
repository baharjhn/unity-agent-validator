# Unity Agent Validator

A Unity Editor tool that finds **missing scripts** and **missing object references** in prefabs. It's exposed as a Unity Pipeline command, so a person or an AI agent can run it from the Unity CLI and read the results as JSON.

Built with Claude Code, the Unity CLI and the Pipeline package, with me reviewing every step.

```
$ unity command find-missing-refs --result-only
{
  "count": 2,
  "issues": [
    { "assetPath": "Assets/Demo/BrokenMatCube.prefab", "objectPath": "BrokenMatCube",
      "description": "MeshRenderer.m_Materials.Array.data[0] is missing" },
    { "assetPath": "Assets/Demo/BrokenScriptSphare.prefab", "objectPath": "BrokenScriptSphare",
      "description": "Missing script (component index 4)" }
  ]
}
```

## How it works

- **Missing script:** a component slot that comes back `null` from `GetComponents<Component>()`.
- **Missing reference:** an object field that is now `null` but once pointed to something (`objectReferenceEntityIdValue != EntityId.None`). Fields that were never assigned aren't reported.
- Checks every GameObject in each prefab, including inactive ones. Read-only: it never modifies assets.

## Run it

Requires Unity 6000.6.6f1, `com.unity.pipeline` 0.9.1-exp.1 and the Unity CLI (beta). With the project open:

```
unity command find-missing-refs
```

Tests (Edit Mode, Unity Test Framework): `unity command test run --mode EditMode --filter Unity.Validator.Editor.Tests --filter_type assembly`, or Window → General → Test Runner.

## How I built it with an AI agent

1. **Spec first, plan before code.** I gave Claude Code a short spec and reviewed its plan before it wrote anything.
2. **My decisions:** a structured `{ count, issues[] }` result instead of a JSON string, so agents can read the fields directly; extra tests for missing scripts on inactive nested children; scenes kept out of scope.
3. **What the agent adapted:** my spec used `objectReferenceInstanceIDValue`, which is obsolete in Unity 6.6. It switched to `objectReferenceEntityIdValue` with the same logic.
4. **What it got wrong:** 3 tests failed at first because of a wrong assumption about prefab root names. It fixed its test setup after running the tests.
5. **What I verified:** `count: 0` on an empty project proved nothing, so I broke two prefabs by hand. The command found both.

## What I learned

- Agents do best with **structured feedback they can verify**, like a JSON command and tests.
- "No issues found" means little until the tool has found a real issue.
- Missing references have **no safe automatic fix**: a tool or agent should report and ask, not guess. Seeing and reviewing what an agent changed is the hard, important part.

## Limitations

Prefabs only (no scenes or ScriptableObjects). Only `Assets/` by default. Issues inside nested prefabs are reported once per prefab that contains them.
