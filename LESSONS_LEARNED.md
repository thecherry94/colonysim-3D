# Lessons Learned

> **Purpose:** Document debugging struggles and solutions so we don't repeat the same mistakes.

---

## 1. The Winding Order Disaster (Phase 1, Session 2)

### The Bug
Chunk rendered **hollow** - you could see through it. Only back faces were visible, front faces were culled.

### Time Wasted
~15+ iterations of trial-and-error before finding the correct solution.

### What Went Wrong

#### 1. Wrong Diagnosis
We kept trying to fix the **vertex positions** (reordering v0, v1, v2, v3 for each face) when the vertices were actually fine. The problem was in the **triangle index order**.

#### 2. Workaround Masked the Real Problem
When we switched `CullMode.Back` to `CullMode.Front`, the chunk appeared solid. This *felt* like a fix, so we moved on. But it was a **bandaid** - the normals were still pointing inward, causing incorrect lighting/shading.

The chunk looked "weird" in the editor because:
- `CullMode.Front` hides inside faces (making it appear solid)
- But normals still pointed inward
- Lighting calculations used wrong normal direction
- Result: flat, oddly-lit surfaces

#### 3. Didn't Understand Winding ↔ Normals Relationship
Godot determines which side of a triangle is "front" based on **index winding order**:
- Clockwise indices (when viewed from outside) = front face
- Counter-clockwise indices = back face

Even though we set explicit normals pointing outward, the winding order told Godot "these triangles face inward" - causing a conflict.

#### 4. Trial-and-Error on 6 Faces = Chaos
With 6 faces, each having 4 vertices that could be reordered many ways, random permutations were never going to work systematically.

### The Actual Fix (2 Lines!)

```csharp
// ChunkMeshGenerator.cs - Reverse the index winding:
// FROM: { 0, 1, 2, 0, 2, 3 }  (CCW)
// TO:   { 0, 2, 1, 0, 3, 2 }  (CW)
private static readonly int[] QuadIndices = { 0, 2, 1, 0, 3, 2 };

// Chunk.cs - Use standard back-face culling:
CullMode = BaseMaterial3D.CullModeEnum.Back
```

### The Lesson

> **When faces render inside-out, fix the INDEX winding order, not the vertex positions.**
>
> Vertices define shape. Indices define which side is "front."

### How to Debug This Faster Next Time

1. **First test:** Set `CullMode.Disabled` to see ALL faces
   - If chunk looks solid with culling disabled → winding is wrong
   - If chunk still has holes → vertices are wrong

2. **Second test:** If winding is wrong, reverse the indices:
   - `{ 0, 1, 2 }` → `{ 0, 2, 1 }` (reverse second triangle)
   - For quads: `{ 0, 1, 2, 0, 2, 3 }` → `{ 0, 2, 1, 0, 3, 2 }`

3. **Don't accept CullMode.Front as a fix** - it's a workaround that breaks lighting

### References
- [Godot ArrayMesh Tutorial](https://docs.godotengine.org/en/stable/tutorials/3d/procedural_geometry/arraymesh.html)
- [BaseMaterial3D CullMode](https://docs.godotengine.org/en/stable/classes/class_basematerial3d.html)

---

## 2. General Debugging Principles

### Do First
1. **Search the web** before guessing - someone has probably solved this exact problem
2. **Isolate the problem** - test with simplest possible case (one face, one cube)
3. **Understand the system** before changing code randomly

### Avoid
1. **Random permutations** - understand WHY something is wrong
2. **Workarounds that "look right"** - verify the fix is actually correct (check lighting, normals, etc.)
3. **Changing multiple things at once** - change one thing, test, repeat

---

*Last updated: 2026-02-01*
