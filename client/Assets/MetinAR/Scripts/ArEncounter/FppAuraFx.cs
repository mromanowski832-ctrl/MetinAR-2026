using UnityEngine;
using UnityEngine.Rendering;

namespace MetinAR.ArEncounter
{
    /// <summary>Original line-renderer Aura ribbons + burst in the right-grip frame (shared by both FPP view variants).</summary>
    public sealed class FppAuraFx
    {
        private readonly LineRenderer _ribbonA, _ribbonB, _burst;

        public FppAuraFx(Transform gripFrame, Material energy)
        {
            _ribbonA = Ribbon("AuraRibbonA", gripFrame, energy, 24, false);
            _ribbonB = Ribbon("AuraRibbonB", gripFrame, energy, 24, false);
            _burst = Ribbon("AuraBurst", gripFrame, energy, 32, true);
        }

        public bool RibbonsVisible => _ribbonA.enabled;

        public void Render(FppCombatAction action, float elapsed, float auraRemaining, float visualTime)
        {
            bool auraActive = auraRemaining > 0f;
            _ribbonA.enabled = _ribbonB.enabled = auraActive;
            if (auraActive)
            {
                float fade = Mathf.Min(1f, auraRemaining / 0.4f);
                _ribbonA.widthMultiplier = _ribbonB.widthMultiplier = 0.012f * fade;
                for (int i = 0; i < 24; i++)
                {
                    float f = i / 23f, angle = f * Mathf.PI * 5f + visualTime * 6f;
                    float radius = 0.045f + 0.01f * Mathf.Sin(visualTime * 8f + f * 6f);
                    Vector3 offset = new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
                    Vector3 centre = Vector3.Lerp(FppPoseContract.BladeBase, FppPoseContract.BladeTip, f);
                    _ribbonA.SetPosition(i, centre + offset);
                    _ribbonB.SetPosition(i, centre - offset);
                }
            }
            float burstTime = elapsed - FppCombatTimeline.AuraBurstSeconds;
            bool burst = action == FppCombatAction.Aura && burstTime >= 0f && burstTime < 0.28f;
            _burst.enabled = burst;
            if (burst)
            {
                float phase = burstTime / 0.28f;
                _burst.widthMultiplier = 0.035f * (1f - phase);
                for (int i = 0; i < 32; i++)
                {
                    float angle = i * Mathf.PI * 2f / 32f;
                    _burst.SetPosition(i, new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * Mathf.Lerp(0.08f, 0.35f, phase)
                        + new Vector3(0, 0.38f, 0));
                }
            }
        }

        private static LineRenderer Ribbon(string name, Transform parent, Material material, int count, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = count;
            line.loop = loop;
            line.sharedMaterial = material;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;
            return line;
        }
    }
}
