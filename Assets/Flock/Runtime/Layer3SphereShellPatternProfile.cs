namespace Flock.Runtime.Patterns {
    using System.Collections.Generic;
    using Flock.Runtime.Data;
    using Unity.Mathematics;
    using UnityEngine;

    [CreateAssetMenu(menuName = "Flock/Layer-3 Patterns/Sphere Shell", fileName = "Layer3_SphereShell")]
    public sealed class Layer3SphereShellPatternProfile : FlockLayer3PatternProfile {
        [Header("Sphere Shell")]
        [SerializeField] bool useBoundsCenter = true;

        [SerializeField]
        Vector3 centerNorm = new Vector3(0.5f, 0.5f, 0.5f);

        [SerializeField, Min(0f)]
        float radius = 5f;

        [Tooltip("<= 0 means 'auto' = radius * 0.25")]
        [SerializeField]
        float thickness = -1f;

        public override FlockLayer3PatternKind Kind => FlockLayer3PatternKind.SphereShell;
        public float Radius => radius;
        public float Thickness => thickness;
        public bool UseBoundsCenter => useBoundsCenter;
        public Vector3 CenterNorm => centerNorm;

        protected override void BakeInternal(
            in FlockEnvironmentData env,
            uint behaviourMask,
            List<FlockLayer3PatternCommand> commands,
            List<FlockLayer3PatternSphereShell> sphereShellPayloads,
            List<FlockLayer3PatternBoxShell> boxShellPayloads) {

            if (radius <= 0f) {
                radius = 0.1f;
            }

            float t = thickness <= 0f ? radius * 0.25f : thickness;
            t = math.max(0.001f, t);

            float3 center = useBoundsCenter
                ? env.BoundsCenter
                : BoundsNormToWorld(env, (float3)centerNorm);

            int payloadIndex = sphereShellPayloads.Count;

            sphereShellPayloads.Add(new FlockLayer3PatternSphereShell {
                Center = center,
                Radius = radius,
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
