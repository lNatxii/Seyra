using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Seyra.SceneTools
{
    [DefaultExecutionOrder(-50), DisallowMultipleComponent]
    public sealed class SeyraMotionSystem : MonoBehaviour
    {
        public const int MaximumMotions = 64;
        // A spare slot allows checking an unregistered candidate without changing live state.
        const int Capacity = MaximumMotions + 1;
        readonly SeyraMotion[] motions = new SeyraMotion[MaximumMotions];
        readonly SeyraMotion[] ordered = new SeyraMotion[MaximumMotions];
        readonly SeyraMotion[] graph = new SeyraMotion[Capacity];
        readonly bool[] edges = new bool[Capacity * Capacity];
        readonly int[] indegree = new int[Capacity];
        readonly int[] queue = new int[Capacity];
        int count, orderedCount;
        bool dirty = true;

        public static SeyraMotionSystem ForScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) throw new ArgumentException("A loaded scene is required for motion.");
            var systems = FindObjectsByType<SeyraMotionSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < systems.Length; i++) if (systems[i].gameObject.scene == scene) return systems[i];
            var go = new GameObject("SeyraMotionSystem");
            SceneManager.MoveGameObjectToScene(go, scene);
            return go.AddComponent<SeyraMotionSystem>();
        }
        int IndexOf(SeyraMotion motion)
        {
            for (int i = 0; i < count; i++) if (motions[i] == motion) return i;
            return -1;
        }
        public void RequireCapacity(SeyraMotion motion)
        {
            if (IndexOf(motion) < 0 && count >= MaximumMotions)
                throw new RoomToolException("MOTION_LIMIT", "At most 64 live objects may have motion.");
        }
        internal void Register(SeyraMotion motion)
        {
            if (motion.gameObject.scene != gameObject.scene) throw new ArgumentException("Motion must belong to the system's scene.");
            RequireCapacity(motion);
            if (IndexOf(motion) < 0) motions[count++] = motion;
            MarkDirty();
        }
        internal void Unregister(SeyraMotion motion)
        {
            int index = IndexOf(motion);
            if (index < 0) return;
            for (int i = index + 1; i < count; i++) motions[i - 1] = motions[i];
            motions[--count] = null;
            MarkDirty();
        }
        public void MarkDirty()
        {
            dirty = true;
            // Cache discovery and delegate changes occur only on structural/configuration changes.
            // Even a dirty Step uses the preallocated graph and never refreshes these lists.
            for (int i = 0; i < count; i++) if (motions[i] != null) motions[i].CacheGrabFrames();
        }
        void Depend(int dependent, int prerequisite)
        {
            int index = prerequisite * Capacity + dependent;
            if (edges[index]) return;
            edges[index] = true;
            indegree[dependent]++;
        }
        void DependOnAncestors(int dependent, Transform ancestor, int graphCount)
        {
            while (ancestor != null)
            {
                for (int i = 0; i < graphCount; i++)
                    if (graph[i] != null && graph[i].transform == ancestor) Depend(dependent, i);
                ancestor = ancestor.parent;
            }
        }
        int BuildGraph(SeyraMotion candidate, PositionSpec proposedPosition, Transform proposedPivot)
        {
            Array.Clear(edges, 0, edges.Length);
            Array.Clear(indegree, 0, indegree.Length);
            Array.Clear(graph, 0, graph.Length);
            int graphCount = count;
            for (int i = 0; i < count; i++) graph[i] = motions[i];
            if (candidate != null && IndexOf(candidate) < 0) graph[graphCount++] = candidate;
            for (int i = 0; i < graphCount; i++)
            {
                var motion = graph[i];
                if (motion == null) continue;
                var position = motion == candidate ? proposedPosition : motion.Position;
                if (position == null || position.Kind != "orbit" || position.PivotId == null) continue;
                Transform pivot = motion == candidate ? proposedPivot : motion.Pivot;
                DependOnAncestors(i, motion.transform.parent, graphCount);
                if (pivot == null) continue;
                if (pivot.gameObject.scene != gameObject.scene || pivot == motion.transform || pivot.IsChildOf(motion.transform))
                    Depend(i, i);
                DependOnAncestors(i, pivot, graphCount);
            }
            return graphCount;
        }
        int Sort(int graphCount, bool saveOrder)
        {
            int head = 0, tail = 0;
            for (int i = 0; i < graphCount; i++) if (indegree[i] == 0) queue[tail++] = i;
            while (head < tail)
            {
                int index = queue[head++];
                if (saveOrder) ordered[head - 1] = graph[index];
                for (int i = 0; i < graphCount; i++)
                    if (edges[index * Capacity + i] && --indegree[i] == 0) queue[tail++] = i;
            }
            return head;
        }
        public bool WouldCycle(SeyraMotion candidate, PositionSpec position, Transform pivot)
        {
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));
            int graphCount = BuildGraph(candidate, position, pivot);
            Sort(graphCount, false);
            for (int i = 0; i < graphCount; i++) if (graph[i] == candidate) return indegree[i] != 0;
            return false;
        }
        void Rebuild()
        {
            int graphCount = BuildGraph(null, null, null);
            Array.Clear(ordered, 0, ordered.Length);
            orderedCount = Sort(graphCount, true);
            for (int i = 0; i < graphCount; i++)
            {
                var motion = graph[i];
                if (motion == null) continue;
                bool invalid = indegree[i] != 0;
                motion.SetDependencyInvalid(invalid);
                // Keep blocked nodes in the evaluation list so status/grab state still updates.
                if (invalid) ordered[orderedCount++] = motion;
            }
            dirty = false;
        }
        void LateUpdate() => Step(Time.deltaTime);
        public void Step(float dt)
        {
            if (float.IsNaN(dt) || float.IsInfinity(dt) || dt < 0) throw new ArgumentOutOfRangeException(nameof(dt));
            if (dirty) Rebuild();
            for (int i = 0; i < orderedCount; i++)
            {
                var motion = ordered[i];
                if (motion != null && motion.isActiveAndEnabled) motion.Evaluate(dt);
            }
        }
    }
}
