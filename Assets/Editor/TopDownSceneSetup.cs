using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class TopDownSceneSetup
{
    [MenuItem("Tools/Top Down/Setup Scene")]
    public static void Setup()
    {
        var scene = EditorSceneManager.GetActiveScene();

        // Ground
        GameObject ground = GameObject.Find("Ground");
        if (ground == null)
        {
            ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.position = Vector3.zero;
            ground.transform.localScale = new Vector3(4f, 1f, 4f);
            var groundMat = CreateUrpMaterial(new Color(0.25f, 0.28f, 0.32f));
            ground.GetComponent<Renderer>().sharedMaterial = groundMat;
        }

        // Player (rebuild — old capsule shape is no longer valid)
        var oldPlayer = GameObject.Find("Player");
        if (oldPlayer != null) Object.DestroyImmediate(oldPlayer);
        BuildPlayer(new Vector3(0f, 0f, 0f));

        // Enemy
        var oldEnemy = GameObject.Find("Enemy");
        if (oldEnemy != null) Object.DestroyImmediate(oldEnemy);
        BuildEnemy(new Vector3(6f, 0f, 4f));

        // Camera (top-down)
        var cam = Camera.main;
        if (cam != null)
        {
            cam.transform.position = new Vector3(0f, 14f, -8f);
            cam.transform.rotation = Quaternion.Euler(60f, 0f, 0f);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Top Down scene setup complete.");
    }

    private static void BuildPlayer(Vector3 position)
    {
        var player = new GameObject("Player");
        player.transform.position = position;

        var cc = player.AddComponent<CharacterController>();
        cc.center = new Vector3(0f, 1f, 0f);
        cc.height = 2f;
        cc.radius = 0.4f;
        cc.slopeLimit = 45f;
        cc.stepOffset = 0.3f;

        var ctrl = player.AddComponent<TopDownMouseController>();

        var mat = CreateUrpMaterial(new Color(0.2f, 0.55f, 1f));
        var rig = HumanoidBuilder.BuildUnder(player.transform, mat);

        var anim = player.AddComponent<HumanoidAnimator>();
        anim.leftArm  = rig.LeftArm;
        anim.rightArm = rig.RightArm;
        anim.leftLeg  = rig.LeftLeg;
        anim.rightLeg = rig.RightLeg;

        var weapon = rig.RightArm.gameObject.AddComponent<MeleeWeapon>();
        weapon.animator = anim;
        weapon.damage = 10f;
        weapon.armLength = 0.75f;
        weapon.hitRadius = 0.3f;
        ctrl.weapon = weapon;
    }

    private static void BuildEnemy(Vector3 position)
    {
        var enemy = new GameObject("Enemy");
        enemy.transform.position = position;

        var col = enemy.AddComponent<CapsuleCollider>();
        col.center = new Vector3(0f, 1.05f, 0f);
        col.height = 2.1f;
        col.radius = 0.5f;

        enemy.AddComponent<Enemy>();

        var mat = CreateUrpMaterial(new Color(0.9f, 0.15f, 0.15f));
        var rig = HumanoidBuilder.BuildUnder(enemy.transform, mat);

        var anim = enemy.AddComponent<HumanoidAnimator>();
        anim.leftArm  = rig.LeftArm;
        anim.rightArm = rig.RightArm;
        anim.leftLeg  = rig.LeftLeg;
        anim.rightLeg = rig.RightLeg;
    }

    private static Material CreateUrpMaterial(Color color)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        var mat = new Material(shader) { color = color };
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        return mat;
    }
}
