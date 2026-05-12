using UnityEngine;

public static class HumanoidBuilder
{
    public class Rig
    {
        public Transform Root;
        public Transform Head;
        public Transform Torso;
        public Transform LeftArm;
        public Transform RightArm;
        public Transform LeftLeg;
        public Transform RightLeg;
    }

    // Builds a humanoid under `root`. Feet sit at the root's local y=0,
    // head top is around y=2.1, so the root pivot should be on the ground.
    public static Rig BuildUnder(Transform root, Material material)
    {
        var rig = new Rig { Root = root };

        rig.Torso = CreatePart(root, "Torso", PrimitiveType.Cube,
            new Vector3(0.55f, 0.9f, 0.35f), new Vector3(0f, 1.15f, 0f), material);

        rig.Head = CreatePart(root, "Head", PrimitiveType.Sphere,
            new Vector3(0.45f, 0.45f, 0.45f), new Vector3(0f, 1.85f, 0f), material);

        rig.LeftArm  = CreateLimb(root, "LeftArm",  new Vector3(-0.4f, 1.55f, 0f), 0.75f, 0.18f, material);
        rig.RightArm = CreateLimb(root, "RightArm", new Vector3( 0.4f, 1.55f, 0f), 0.75f, 0.18f, material);

        rig.LeftLeg  = CreateLimb(root, "LeftLeg",  new Vector3(-0.15f, 0.7f, 0f), 0.7f, 0.22f, material);
        rig.RightLeg = CreateLimb(root, "RightLeg", new Vector3( 0.15f, 0.7f, 0f), 0.7f, 0.22f, material);

        return rig;
    }

    private static Transform CreatePart(Transform parent, string name, PrimitiveType type,
        Vector3 scale, Vector3 localPos, Material material)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        Strip(go, material);
        return go.transform;
    }

    private static Transform CreateLimb(Transform parent, string name,
        Vector3 jointLocalPos, float length, float thickness, Material material)
    {
        var joint = new GameObject(name).transform;
        joint.SetParent(parent, false);
        joint.localPosition = jointLocalPos;
        joint.localRotation = Quaternion.identity;

        var mesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
        mesh.name = name + "_Mesh";
        mesh.transform.SetParent(joint, false);
        mesh.transform.localPosition = new Vector3(0f, -length * 0.5f, 0f);
        mesh.transform.localScale = new Vector3(thickness, length, thickness);
        Strip(mesh, material);

        return joint;
    }

    private static void Strip(GameObject go, Material material)
    {
        var col = go.GetComponent<Collider>();
        if (col != null) Object.DestroyImmediate(col);
        if (material != null)
        {
            var rend = go.GetComponent<Renderer>();
            if (rend != null) rend.sharedMaterial = material;
        }
    }
}
