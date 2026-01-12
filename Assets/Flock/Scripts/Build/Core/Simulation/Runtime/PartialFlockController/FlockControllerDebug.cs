using Flock.Scripts.Build.Influence.Environment.Bounds.Data;
using Flock.Scripts.Build.Influence.Environment.Data;
using Flock.Scripts.Build.Agents.Fish.Data;

using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;

namespace Flock.Scripts.Build.Core.Simulation.Runtime.PartialFlockController
{

    /**
    * <summary>
    * Debug gizmo rendering for the flock controller (bounds, grid, agents, and neighbourhood probes).
    * </summary>
    */
    public sealed partial class FlockController
    {
        private void OnDrawGizmosSelected()
        {
            FlockEnvironmentData environmentData = BuildEnvironmentData();

            if (!Application.isPlaying || simulation == null || !simulation.IsCreated)
            {
                return;
            }

            NativeArray<float3> positions = simulation.Positions;
            NativeArray<float3> velocities = simulation.Velocities;

            if (debugDrawGrid)
            {
                DrawGridGizmos(environmentData);
            }

            if (debugDrawAgents)
            {
                DrawAgentsGizmos(positions);
            }

            if (debugDrawNeighbourhood)
            {
                DrawNeighbourhoodGizmos(positions, velocities, environmentData);
            }
        }

        private void OnDrawGizmos()
        {
            FlockEnvironmentData environmentData = BuildEnvironmentData();

            if (debugDrawBounds)
            {
                DrawBoundsGizmos(environmentData);
            }
        }

        private static int3 GetCell(
            float3 position,
            FlockEnvironmentData environmentData)
        {

            float3 local = position - environmentData.GridOrigin;
            float3 scaled = local / math.max(environmentData.CellSize, 0.0001f);

            int3 cell = (int3)math.floor(scaled);
            int3 max = environmentData.GridResolution - new int3(1, 1, 1);

            return math.clamp(
                cell,
                new int3(0, 0, 0),
                max);
        }

        private void DrawBoundsGizmos(FlockEnvironmentData environmentData)
        {
            float3 center = environmentData.BoundsCenter;
            Gizmos.color = Color.green;

            if (environmentData.BoundsType == FlockBoundsType.Sphere && environmentData.BoundsRadius > 0f)
            {
                Gizmos.DrawWireSphere(
                    (Vector3)center,
                    environmentData.BoundsRadius);
                return;
            }

            float3 extents = environmentData.BoundsExtents;
            Gizmos.DrawWireCube(
                (Vector3)center,
                (Vector3)(extents * 2f));
        }

        private void DrawNeighbourhoodGizmos(
            NativeArray<float3> positions,
            NativeArray<float3> velocities,
            FlockEnvironmentData environmentData)
        {

            int agentCount = positions.Length;

            if (!TryResolveNeighbourhoodContext(
                    positions,
                    agentCount,
                    out int selectedAgentIndex,
                    out float3 selectedAgentPosition,
                    out int behaviourIndex,
                    out float neighbourRadius,
                    out float separationRadius,
                    out float bodyRadius))
            {
                return;
            }

            DrawSelectedAgentMarker(selectedAgentPosition);

            DrawSelectedAgentRadii(
                selectedAgentPosition,
                neighbourRadius,
                separationRadius,
                bodyRadius);

            DrawGridSearchRadius(
                selectedAgentPosition,
                behaviourIndex,
                neighbourRadius,
                environmentData);

            DrawSelectedAgentGridCell(
                selectedAgentPosition,
                environmentData);

            DrawNeighboursInsideNeighbourRadius(
                positions,
                selectedAgentIndex,
                selectedAgentPosition,
                neighbourRadius);
        }

        private bool TryResolveNeighbourhoodContext(
            NativeArray<float3> positions,
            int agentCount,
            out int selectedAgentIndex,
            out float3 selectedAgentPosition,
            out int behaviourIndex,
            out float neighbourRadius,
            out float separationRadius,
            out float bodyRadius)
        {

            selectedAgentIndex = 0;
            selectedAgentPosition = float3.zero;
            behaviourIndex = 0;

            neighbourRadius = 0f;
            separationRadius = 0f;
            bodyRadius = 0f;

            if (agentCount == 0
                || !behaviourSettingsArray.IsCreated
                || behaviourSettingsArray.Length == 0)
            {
                return false;
            }

            selectedAgentIndex = math.clamp(
                debugAgentIndex,
                0,
                agentCount - 1);

            selectedAgentPosition = positions[selectedAgentIndex];

            behaviourIndex = 0;

            if (agentBehaviourIds != null
                && selectedAgentIndex >= 0
                && selectedAgentIndex < agentBehaviourIds.Length)
            {
                behaviourIndex = math.clamp(
                    agentBehaviourIds[selectedAgentIndex],
                    0,
                    behaviourSettingsArray.Length - 1);
            }

            FlockBehaviourSettings settings = behaviourSettingsArray[behaviourIndex];

            neighbourRadius = settings.NeighbourRadius;
            separationRadius = settings.SeparationRadius;
            bodyRadius = settings.BodyRadius;

            if (neighbourRadius <= 0.0f
                && separationRadius <= 0.0f
                && bodyRadius <= 0.0f)
            {
                return false;
            }

            return true;
        }

        private void DrawSelectedAgentMarker(float3 agentPosition)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere((Vector3)agentPosition, 0.2f);
        }

        private void DrawSelectedAgentRadii(
            float3 agentPosition,
            float neighbourRadius,
            float separationRadius,
            float bodyRadius)
        {

            // Body radius (physical size).
            if (debugDrawBodyRadius && bodyRadius > 0f)
            {
                Gizmos.color = new Color(0f, 1f, 1f, 0.7f);
                Gizmos.DrawWireSphere(
                    (Vector3)agentPosition,
                    bodyRadius);
            }

            // Separation radius (hard "back off" bubble).
            if (debugDrawSeparationRadius && separationRadius > 0f)
            {
                Gizmos.color = new Color(1f, 0f, 0f, 0.5f);
                Gizmos.DrawWireSphere(
                    (Vector3)agentPosition,
                    separationRadius);
            }

            // Neighbour perception radius (logical view distance).
            if (debugDrawNeighbourRadiusSphere && neighbourRadius > 0f)
            {
                Gizmos.color = new Color(1f, 1f, 0f, 0.35f);
                Gizmos.DrawWireSphere(
                    (Vector3)agentPosition,
                    neighbourRadius);
            }
        }

        private void DrawGridSearchRadius(
            float3 agentPosition,
            int behaviourIndex,
            float neighbourRadius,
            FlockEnvironmentData environmentData)
        {

            if (!debugDrawGridSearchRadiusSphere
                || neighbourRadius <= 0f
                || environmentData.CellSize <= 0.0001f)
            {
                return;
            }

            float cellSizeValue = environmentData.CellSize;

            int cellRange = Mathf.Max(
                1,
                Mathf.CeilToInt(neighbourRadius / cellSizeValue));

            float gridSearchWorldRadius = cellRange * cellSizeValue;

            Gizmos.color = new Color(1f, 0.5f, 0f, 0.35f);
            Gizmos.DrawWireSphere(
                (Vector3)agentPosition,
                gridSearchWorldRadius);

#if UNITY_EDITOR
            UnityEditor.Handles.Label(
                (Vector3)agentPosition + Vector3.up * (gridSearchWorldRadius + 0.25f),
                $"beh={behaviourIndex}, cellRange={cellRange}, neighR={neighbourRadius:0.##}");
#endif
        }

        private void DrawSelectedAgentGridCell(
            float3 agentPosition,
            FlockEnvironmentData environmentData)
        {

            int3 cell = GetCell(agentPosition, environmentData);

            float cellSizeValue = environmentData.CellSize;
            float3 origin = environmentData.GridOrigin;

            float3 cellCenter = origin + new float3(
                (cell.x + 0.5f) * cellSizeValue,
                (cell.y + 0.5f) * cellSizeValue,
                (cell.z + 0.5f) * cellSizeValue);

            Gizmos.color = Color.magenta;
            Gizmos.DrawWireCube(
                (Vector3)cellCenter,
                new float3(cellSizeValue, cellSizeValue, cellSizeValue));
        }

        private void DrawNeighboursInsideNeighbourRadius(
            NativeArray<float3> positions,
            int selectedAgentIndex,
            float3 selectedAgentPosition,
            float neighbourRadius)
        {

            if (neighbourRadius <= 0f)
            {
                return;
            }

            float neighbourRadiusSquared = neighbourRadius * neighbourRadius;
            Gizmos.color = Color.red;

            for (int index = 0; index < positions.Length; index += 1)
            {
                if (index == selectedAgentIndex)
                {
                    continue;
                }

                float3 other = positions[index];
                float3 offset = other - selectedAgentPosition;
                float distanceSquared = math.lengthsq(offset);

                if (distanceSquared <= neighbourRadiusSquared)
                {
                    Gizmos.DrawSphere((Vector3)other, 0.12f);
                }
            }
        }

        private void DrawAgentsGizmos(NativeArray<float3> positions)
        {
            int agentCount = positions.Length;

            if (agentCount == 0
                || !behaviourSettingsArray.IsCreated
                || behaviourSettingsArray.Length == 0
                || agentBehaviourIds == null
                || agentBehaviourIds.Length < agentCount)
            {
                return;
            }

            for (int index = 0; index < agentCount; index += 1)
            {
                float3 position = positions[index];

                int behaviourIndex = agentBehaviourIds[index];
                if ((uint)behaviourIndex >= (uint)behaviourSettingsArray.Length)
                {
                    continue;
                }

                FlockBehaviourSettings settings = behaviourSettingsArray[behaviourIndex];

                float bodyRadius = settings.BodyRadius;
                float separationRadius = settings.SeparationRadius;
                float neighbourRadius = settings.NeighbourRadius;

                Gizmos.color = new Color(1f, 1f, 1f, 0.75f);
                Gizmos.DrawSphere(position, 0.05f);

                if (debugDrawBodyRadius && bodyRadius > 0f)
                {
                    Gizmos.color = new Color(0f, 1f, 1f, 0.6f);
                    Gizmos.DrawWireSphere((Vector3)position, bodyRadius);
                }

                if (debugDrawSeparationRadius && separationRadius > 0f)
                {
                    Gizmos.color = new Color(1f, 0f, 0f, 0.4f);
                    Gizmos.DrawWireSphere((Vector3)position, separationRadius);
                }

                if (debugDrawNeighbourRadiusSphere && neighbourRadius > 0f)
                {
                    Gizmos.color = new Color(1f, 1f, 0f, 0.25f);
                    Gizmos.DrawWireSphere((Vector3)position, neighbourRadius);
                }
            }
        }

        private void DrawGridGizmos(FlockEnvironmentData environmentData)
        {
            float3 origin = environmentData.GridOrigin;
            float cellSizeValue = environmentData.CellSize;
            int3 resolution = environmentData.GridResolution;

            int totalCells = resolution.x * resolution.y * resolution.z;
            if (totalCells > 10_000)
            {
                return;
            }

            Gizmos.color = new Color(0.2f, 0.6f, 1.0f, 0.15f);

            for (int x = 0; x < resolution.x; x += 1)
            {
                for (int y = 0; y < resolution.y; y += 1)
                {
                    for (int z = 0; z < resolution.z; z += 1)
                    {
                        float3 center = origin + new float3(
                            (x + 0.5f) * cellSizeValue,
                            (y + 0.5f) * cellSizeValue,
                            (z + 0.5f) * cellSizeValue);

                        Gizmos.DrawWireCube(
                            center,
                            new float3(cellSizeValue, cellSizeValue, cellSizeValue));
                    }
                }
            }
        }
    }
}
