// File: Assets/Flock/Runtime/FlockController.Debug.cs
namespace Flock.Runtime {
    using Flock.Runtime.Data;
    using Unity.Collections;
    using Unity.Mathematics;
    using UnityEngine;

    public sealed partial class FlockController {
        // ---------------- FlockController.Debug.cs ----------------
        // Paste-only debug code (no logic changes, just moved out)

        void OnDrawGizmosSelected() {
            // Rebuild environment data from current inspector values.
            FlockEnvironmentData environmentData = BuildEnvironmentData();

            if (debugDrawBounds) {
                DrawBoundsGizmos(environmentData);
            }

            if (!Application.isPlaying || simulation == null || !simulation.IsCreated) {
                return;
            }

            NativeArray<float3> positions = simulation.Positions;
            NativeArray<float3> velocities = simulation.Velocities;

            if (debugDrawGrid) {
                DrawGridGizmos(environmentData);
            }

            // Per-fish radii for ALL agents (uses shared debug toggles)
            if (debugDrawAgents) {
                DrawAgentsGizmos(positions);
            }

            // Single-agent detailed view – shows neighbour radius,
            // search radius (cellRange), bounds look-ahead, etc.
            if (debugDrawNeighbourhood) {
                DrawNeighbourhoodGizmos(positions, velocities, environmentData);
            }
        }

        void DrawBoundsGizmos(FlockEnvironmentData environmentData) {
            float3 center = environmentData.BoundsCenter;
            Gizmos.color = Color.green;

            if (environmentData.BoundsType == FlockBoundsType.Sphere && environmentData.BoundsRadius > 0f) {
                Gizmos.DrawWireSphere(
                    (Vector3)center,
                    environmentData.BoundsRadius);
            } else {
                float3 extents = environmentData.BoundsExtents;
                Gizmos.DrawWireCube(
                    (Vector3)center,
                    (Vector3)(extents * 2f));
            }
        }

        void DrawNeighbourhoodGizmos(
            NativeArray<float3> positions,
            NativeArray<float3> velocities,
            FlockEnvironmentData environmentData) {

            int length = positions.Length;
            if (length == 0
                || !behaviourSettingsArray.IsCreated
                || behaviourSettingsArray.Length == 0) {
                return;
            }

            int index = math.clamp(
                debugAgentIndex,
                0,
                length - 1);

            float3 agentPosition = positions[index];

            // Resolve this agent's behaviour index
            int behaviourIndex = 0;
            if (agentBehaviourIds != null
                && index >= 0
                && index < agentBehaviourIds.Length) {
                behaviourIndex = math.clamp(
                    agentBehaviourIds[index],
                    0,
                    behaviourSettingsArray.Length - 1);
            }

            FlockBehaviourSettings settings = behaviourSettingsArray[behaviourIndex];

            float neighbourRadius = settings.NeighbourRadius;
            float separationRadius = settings.SeparationRadius;
            float bodyRadius = settings.BodyRadius;

            if (neighbourRadius <= 0.0f
                && separationRadius <= 0.0f
                && bodyRadius <= 0.0f) {
                return;
            }

            // Highlight the selected agent itself
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere((Vector3)agentPosition, 0.2f);

            // Body radius (physical size)
            if (debugDrawBodyRadius && bodyRadius > 0f) {
                Gizmos.color = new Color(0f, 1f, 1f, 0.7f); // cyan
                Gizmos.DrawWireSphere(
                    (Vector3)agentPosition,
                    bodyRadius);
            }

            // Separation radius (hard "back off" bubble)
            if (debugDrawSeparationRadius && separationRadius > 0f) {
                Gizmos.color = new Color(1f, 0f, 0f, 0.5f); // red
                Gizmos.DrawWireSphere(
                    (Vector3)agentPosition,
                    separationRadius);
            }

            // Neighbour perception radius (logical view distance)
            if (debugDrawNeighbourRadiusSphere && neighbourRadius > 0f) {
                Gizmos.color = new Color(1f, 1f, 0f, 0.35f); // yellow
                Gizmos.DrawWireSphere(
                    (Vector3)agentPosition,
                    neighbourRadius);
            }

            // Grid search radius in world units (how far in cells this type actually scans)
            if (debugDrawGridSearchRadiusSphere
                && neighbourRadius > 0f
                && environmentData.CellSize > 0.0001f) {

                float cellSize = environmentData.CellSize;

                int cellRange = Mathf.Max(
                    1,
                    Mathf.CeilToInt(neighbourRadius / cellSize));

                float gridSearchWorldRadius = cellRange * cellSize;

                Gizmos.color = new Color(1f, 0.5f, 0f, 0.35f); // orange
                Gizmos.DrawWireSphere(
                    (Vector3)agentPosition,
                    gridSearchWorldRadius);

#if UNITY_EDITOR
                UnityEditor.Handles.Label(
                    (Vector3)agentPosition + Vector3.up * (gridSearchWorldRadius + 0.25f),
                    $"beh={behaviourIndex}, cellRange={cellRange}, neighR={neighbourRadius:0.##}");
#endif
            }

            // Highlight its grid cell
            int3 cell = GetCell(agentPosition, environmentData);
            float cellSizeG = environmentData.CellSize;
            float3 origin = environmentData.GridOrigin;
            float3 cellCenter = origin + new float3(
                (cell.x + 0.5f) * cellSizeG,
                (cell.y + 0.5f) * cellSizeG,
                (cell.z + 0.5f) * cellSizeG);

            Gizmos.color = Color.magenta;
            Gizmos.DrawWireCube(
                (Vector3)cellCenter,
                new float3(cellSizeG, cellSizeG, cellSizeG));

            // Draw neighbours inside logical neighbour radius
            if (neighbourRadius > 0f) {
                float radiusSquared = neighbourRadius * neighbourRadius;
                Gizmos.color = Color.red;

                for (int i = 0; i < length; i += 1) {
                    if (i == index) {
                        continue;
                    }

                    float3 other = positions[i];
                    float3 offset = other - agentPosition;
                    float distanceSquared = math.lengthsq(offset);

                    if (distanceSquared <= radiusSquared) {
                        Gizmos.DrawSphere((Vector3)other, 0.12f);
                    }
                }
            }
        }

        void DrawAgentsGizmos(NativeArray<float3> positions) {
            int length = positions.Length;
            if (length == 0
                || !behaviourSettingsArray.IsCreated
                || behaviourSettingsArray.Length == 0
                || agentBehaviourIds == null
                || agentBehaviourIds.Length < length) {
                return;
            }

            for (int index = 0; index < length; index += 1) {
                float3 pos = positions[index];

                int behaviourIndex = agentBehaviourIds[index];
                if ((uint)behaviourIndex >= (uint)behaviourSettingsArray.Length) {
                    continue;
                }

                FlockBehaviourSettings settings = behaviourSettingsArray[behaviourIndex];

                float bodyRadius = settings.BodyRadius;
                float separationRadius = settings.SeparationRadius;
                float neighbourRadius = settings.NeighbourRadius;

                // Small center marker so you still see the fish itself
                Gizmos.color = new Color(1f, 1f, 1f, 0.75f);
                Gizmos.DrawSphere(pos, 0.05f);

                // Body radius (physical size)
                if (debugDrawBodyRadius && bodyRadius > 0f) {
                    Gizmos.color = new Color(0f, 1f, 1f, 0.6f);      // cyan
                    Gizmos.DrawWireSphere((Vector3)pos, bodyRadius);
                }

                // Separation radius (hard "back off" bubble)
                if (debugDrawSeparationRadius && separationRadius > 0f) {
                    Gizmos.color = new Color(1f, 0f, 0f, 0.4f);      // red
                    Gizmos.DrawWireSphere((Vector3)pos, separationRadius);
                }

                // Logical neighbour radius (who this fish can see)
                if (debugDrawNeighbourRadiusSphere && neighbourRadius > 0f) {
                    Gizmos.color = new Color(1f, 1f, 0f, 0.25f);     // yellow
                    Gizmos.DrawWireSphere((Vector3)pos, neighbourRadius);
                }
            }
        }

        void DrawGridGizmos(FlockEnvironmentData environmentData) {
            float3 origin = environmentData.GridOrigin;
            float cell = environmentData.CellSize;
            int3 res = environmentData.GridResolution;

            // Safety guard – drawing millions of cubes will tank the editor.
            int totalCells = res.x * res.y * res.z;
            if (totalCells > 10_000) {
                return;
            }

            Gizmos.color = new Color(0.2f, 0.6f, 1.0f, 0.15f);

            for (int x = 0; x < res.x; x += 1) {
                for (int y = 0; y < res.y; y += 1) {
                    for (int z = 0; z < res.z; z += 1) {
                        float3 center = origin + new float3(
                            (x + 0.5f) * cell,
                            (y + 0.5f) * cell,
                            (z + 0.5f) * cell);

                        Gizmos.DrawWireCube(
                            center,
                            new float3(cell, cell, cell));
                    }
                }
            }
        }

        static int3 GetCell(
            float3 position,
            FlockEnvironmentData environmentData) {

            float3 local = position - environmentData.GridOrigin;
            float3 scaled = local / math.max(environmentData.CellSize, 0.0001f);

            int3 cell = (int3)math.floor(scaled);
            int3 max = environmentData.GridResolution - new int3(1, 1, 1);

            return math.clamp(
                cell,
                new int3(0, 0, 0),
                max);
        }
    }
}
