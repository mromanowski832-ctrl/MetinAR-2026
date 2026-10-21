using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MetinAR.ArEncounter
{
    /// <summary>
    /// Procedural comparison variant (#31): original glove/forearm blockout, sword and energy ribbons. No extracted art.
    /// Animation and hit sampling use the SAME contract pose. Visual approval on a physical phone is pending.
    /// </summary>
    public sealed class FppHandsView : MonoBehaviour, IFppHandsView
    {
        /// <summary>Unscaled sword-model blade points (RightGrip frame at scale 1); the hit segment is Contract.HitBladeBase/Tip.</summary>
        public static readonly Vector3 BladeBase = FppPoseContract.BladeBase;
        public static readonly Vector3 BladeTip = FppPoseContract.BladeTip;

        private readonly List<Mesh> _meshes = new List<Mesh>();
        private readonly List<Material> _materials = new List<Material>();
        private Transform _rightGrip, _leftHand, _rightArm, _leftArm, _sword;
        private FppAuraFx _aura;
        public Transform RightGrip => _rightGrip;
        public Transform LeftHand => _leftHand;
        /// <summary>Sword model root under the grip, uniformly scaled by Contract.SwordScale (#31 A31 retest).</summary>
        public Transform Sword => _sword;
        public FppPoseContract Contract { get; private set; } = FppPoseContract.RetestV3;
        public bool AuraRibbonsVisible => _aura != null && _aura.RibbonsVisible;

        public void Build(Material bodySource, Material energySource, FppPoseContract contract)
        {
            Contract = contract ?? FppPoseContract.RetestV3;
            Material glove = MaterialCopy(bodySource, new Color(0.22f, 0.12f, 0.075f), "FppLeather");
            Material steel = MaterialCopy(bodySource, new Color(0.7f, 0.77f, 0.82f), "FppSteel");
            Material gold = MaterialCopy(bodySource, new Color(0.78f, 0.52f, 0.14f), "FppGold");
            Material energy = MaterialCopy(energySource, new Color(0.12f, 0.95f, 1f), "FppAura");

            _rightGrip = Child("RightGrip", transform);
            _leftHand = Child("LeftHand", transform);
            _sword = Child("Sword", _rightGrip);
            _sword.localScale = Vector3.one * Contract.SwordScale;
            DrawMesh("RightGlove", _rightGrip, HandMesh(1f), glove);
            DrawMesh("LeftGlove", _leftHand, HandMesh(-1f), glove);
            var hilt = new Shape();
            hilt.Box(new Vector3(0, -0.035f, 0), new Vector3(0.035f, 0.29f, 0.035f));
            DrawMesh("SwordGrip", _sword, hilt.Finish(), glove);
            var guard = new Shape();
            guard.Box(new Vector3(0, 0.12f, 0), new Vector3(0.25f, 0.035f, 0.05f));
            guard.Box(new Vector3(0, -0.19f, 0), new Vector3(0.055f, 0.05f, 0.055f));
            DrawMesh("SwordGuardPommel", _sword, guard.Finish(), gold);
            var blade = new Shape();
            blade.Box(new Vector3(0, 0.485f, 0), new Vector3(0.065f, 0.70f, 0.018f));
            blade.Triangle(new Vector3(-0.0325f, 0.835f, -0.009f), new Vector3(0, 0.94f, 0), new Vector3(0.0325f, 0.835f, -0.009f));
            blade.Triangle(new Vector3(0.0325f, 0.835f, 0.009f), new Vector3(0, 0.94f, 0), new Vector3(-0.0325f, 0.835f, 0.009f));
            blade.Triangle(new Vector3(-0.0325f, 0.835f, 0.009f), new Vector3(0, 0.94f, 0), new Vector3(-0.0325f, 0.835f, -0.009f));
            blade.Triangle(new Vector3(0.0325f, 0.835f, -0.009f), new Vector3(0, 0.94f, 0), new Vector3(0.0325f, 0.835f, 0.009f));
            DrawMesh("SwordBlade", _sword, blade.Finish(), steel);

            var arm = new Shape();
            arm.Box(Vector3.zero, Vector3.one);
            Mesh armMesh = arm.Finish();
            _rightArm = DrawMesh("RightForearm", transform, armMesh, glove);
            _leftArm = DrawMesh("LeftForearm", transform, armMesh, glove);
            // Under the scaled sword root: the Aura ribbons follow the visible (scaled) blade.
            _aura = new FppAuraFx(_sword, energy);
        }

        public void Render(FppCombatAction action, float elapsed, float auraRemaining, float visualTime)
        {
            Pose pose = Contract.SwordPose(action, elapsed);
            _rightGrip.SetLocalPositionAndRotation(pose.position, pose.rotation);
            Pose left = Contract.LeftHandPose(action, elapsed);
            _leftHand.SetLocalPositionAndRotation(left.position, left.rotation);
            Arm(_rightArm, new Vector3(0.28f, -0.46f, 0.19f), pose.position + pose.rotation * new Vector3(0.065f, -0.04f, 0));
            Arm(_leftArm, new Vector3(-0.28f, -0.46f, 0.19f), _leftHand.localPosition + _leftHand.localRotation * new Vector3(-0.065f, -0.04f, 0));
            _aura.Render(action, elapsed, auraRemaining, visualTime);
        }

        private static void Arm(Transform arm, Vector3 a, Vector3 b)
        {
            arm.localPosition = (a + b) * 0.5f;
            arm.localRotation = Quaternion.FromToRotation(Vector3.up, b - a);
            arm.localScale = new Vector3(0.075f, Vector3.Distance(a, b), 0.075f);
        }
        private static Transform Child(string name, Transform parent)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }
        private Material MaterialCopy(Material source, Color color, string name)
        {
            var material = new Material(source) { name = name };
            material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            _materials.Add(material);
            return material;
        }
        private Transform DrawMesh(string name, Transform parent, Mesh mesh, Material material)
        {
            Transform t = Child(name, parent);
            t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = t.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (!_meshes.Contains(mesh)) _meshes.Add(mesh);
            return t;
        }
        private Mesh HandMesh(float side)
        {
            var shape = new Shape();
            shape.Box(new Vector3(side * 0.044f, 0, 0.018f), new Vector3(0.06f, 0.085f, 0.035f));
            // Four curled fingers and a separate thumb: an original closed glove around the hilt.
            for (int i = 0; i < 4; i++)
            {
                float y = -0.03f + i * 0.02f;
                shape.Box(new Vector3(side * 0.02f, y, -0.011f), new Vector3(0.055f, 0.017f, 0.019f));
                shape.Box(new Vector3(-side * 0.014f, y, 0), new Vector3(0.018f, 0.017f, 0.034f));
                shape.Box(new Vector3(-side * 0.002f, y, 0.024f), new Vector3(0.024f, 0.017f, 0.018f));
            }
            shape.Box(new Vector3(side * 0.03f, 0.05f, 0.012f), new Vector3(0.04f, 0.02f, 0.025f));
            shape.Box(new Vector3(side * 0.008f, 0.045f, -0.003f), new Vector3(0.025f, 0.025f, 0.02f));
            return shape.Finish();
        }
        private void OnDestroy()
        {
            foreach (Mesh mesh in _meshes) if (mesh != null) Destroy(mesh);
            foreach (Material material in _materials) if (material != null) Destroy(material);
        }
        private sealed class Shape
        {
            private readonly List<Vector3> _vertices = new List<Vector3>();
            private readonly List<int> _indices = new List<int>();
            public void Triangle(Vector3 a, Vector3 b, Vector3 c)
            {
                int n = _vertices.Count;
                _vertices.Add(a); _vertices.Add(b); _vertices.Add(c);
                _indices.Add(n); _indices.Add(n + 1); _indices.Add(n + 2);
            }
            public void Box(Vector3 centre, Vector3 size)
            {
                Vector3 h = size * 0.5f;
                var p = new Vector3[8];
                for (int i = 0; i < 8; i++)
                    p[i] = centre + new Vector3((i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? -h.y : h.y, (i & 4) == 0 ? -h.z : h.z);
                int[] faces = { 0, 2, 3, 1, 4, 5, 7, 6, 0, 4, 6, 2, 1, 3, 7, 5, 0, 1, 5, 4, 2, 6, 7, 3 };
                for (int i = 0; i < faces.Length; i += 4)
                {
                    Triangle(p[faces[i]], p[faces[i + 1]], p[faces[i + 2]]);
                    Triangle(p[faces[i]], p[faces[i + 2]], p[faces[i + 3]]);
                }
            }
            public Mesh Finish()
            {
                var mesh = new Mesh { name = "OriginalFppBlockout" };
                mesh.SetVertices(_vertices);
                mesh.SetTriangles(_indices, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
