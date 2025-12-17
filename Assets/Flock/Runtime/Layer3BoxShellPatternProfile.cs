namespace Flock.Runtime.Patterns {
    using System.Collections.Generic;
    using Flock.Runtime.Data;
    using Unity.Mathematics;
    using UnityEngine;

    [CreateAssetMenu(menuName = "Flock/Layer-3 Patterns/Box Shell", fileName = "Layer3_BoxShell")]
    public sealed class Layer3BoxShellPatternProfile : FlockLayer3PatternProfile {
        [Header("Box Shell")]
        [SerializeField] bool useBoundsCenter = true;

        [SerializeField]
        Vector3 centerNorm = new Vector3(0.5f, 0.5f, 0.5f);

        [SerializeField]
        Vector3 halfExtents = new Vector3(5f, 5f, 5f);

        [Tooltip("<= 0 means 'auto' = min(halfExtents) * 0.25")]
        [SerializeField]
        float thickness = -1f;

        public override FlockLayer3PatternKind Kind => FlockLayer3PatternKind.BoxShell;

        public bool UseBoundsCenter => useBoundsCenter;
        public Vector3 CenterNorm => centerNorm;
        public Vector3 HalfExtents => halfExtents;
        public float Thickness => thickness;

        protected override void BakeInternal(
            in FlockEnvironmentData env,
            uint behaviourMask,
            List<FlockLayer3PatternCommand> commands,
            List<FlockLayer3PatternSphereShell> sphereShellPayloads,
            List<FlockLayer3PatternBoxShell> boxShellPayloads) {

            if (halfExtents.x <= 0f
                || halfExtents.y <= 0f
                || halfExtents.z <= 0f) {
                return;
            }

            float3 center = useBoundsCenter
                ? env.BoundsCenter
                : BoundsNormToWorld(env, (float3)centerNorm);

            float3 he = new float3(
                math.max(halfExtents.x, 0.001f),
                math.max(halfExtents.y, 0.001f),
                math.max(halfExtents.z, 0.001f));

            float t = thickness <= 0f
                ? math.cmin(he) * 0.25f
                : thickness;

            t = math.max(0.001f, t);

            int payloadIndex = boxShellPayloads.Count;

            boxShellPayloads.Add(new FlockLayer3PatternBoxShell {
                Center = center,
                HalfExtents = he,
                Thickness = t,
            });

            commands.Add(new FlockLayer3PatternCommand {
                Kind = Kind,
                PayloadIndex = payloadIndex,
                Strength = Strength,
                BehaviourMask = behaviourMask,
            });
        }
    }
}
