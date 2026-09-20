using UnityEngine;

/// <summary>
/// 在 Scene 视图里画出 BodyPartHitbox 的碰撞体，方便对照胸口和上臂是否重叠。
/// 挂在角色根节点上即可，只画自己子节点里的部位盒。
/// </summary>
public sealed class HitboxGizmoDrawer : MonoBehaviour
{
    [SerializeField] bool includeInactive = true;
    [SerializeField] bool drawSolid = true;
    [SerializeField] bool drawWire = true;
    [SerializeField] [Range(0.05f, 0.6f)] float solidAlpha = 0.22f;

    [SerializeField] Color headColor = new Color(0.95f, 0.2f, 0.2f, 1f);
    [SerializeField] Color torsoColor = new Color(0.2f, 0.9f, 0.35f, 1f);
    [SerializeField] Color limbColor = new Color(0.95f, 0.85f, 0.15f, 1f);

    void OnDrawGizmos()
    {
        BodyPartHitbox[] hitboxes = GetComponentsInChildren<BodyPartHitbox>(includeInactive);
        for (int i = 0; i < hitboxes.Length; i++)
        {
            DrawHitbox(hitboxes[i]);
        }
    }

    /// <summary>按部位上色，并画出该节点自己的 Collider。不往父节点找，避免把手臂画成躯干。</summary>
    void DrawHitbox(BodyPartHitbox hitbox)
    {
        if (hitbox == null)
        {
            return;
        }

        Collider collider = hitbox.GetComponent<Collider>();
        if (collider == null || !collider.enabled)
        {
            return;
        }

        Color color = ColorFor(hitbox.bodyPart);
        Color solid = new Color(color.r, color.g, color.b, solidAlpha);
        Color wire = new Color(color.r, color.g, color.b, 1f);
        Matrix4x4 previous = Gizmos.matrix;

        switch (collider)
        {
            case BoxCollider box:
                DrawBox(box, solid, wire);
                break;
            case CapsuleCollider capsule:
                DrawCapsule(capsule, solid, wire);
                break;
            case SphereCollider sphere:
                DrawSphere(sphere, solid, wire);
                break;
        }

        Gizmos.matrix = previous;
    }

    Color ColorFor(DetailedBodyPart part)
    {
        switch (part)
        {
            case DetailedBodyPart.Head:
                return headColor;
            case DetailedBodyPart.Torso:
                return torsoColor;
            default:
                return limbColor;
        }
    }

    void DrawBox(BoxCollider box, Color solid, Color wire)
    {
        Gizmos.matrix = box.transform.localToWorldMatrix;
        if (drawSolid)
        {
            Gizmos.color = solid;
            Gizmos.DrawCube(box.center, box.size);
        }

        if (drawWire)
        {
            Gizmos.color = wire;
            Gizmos.DrawWireCube(box.center, box.size);
        }
    }

    void DrawSphere(SphereCollider sphere, Color solid, Color wire)
    {
        Gizmos.matrix = sphere.transform.localToWorldMatrix;
        if (drawSolid)
        {
            Gizmos.color = solid;
            Gizmos.DrawSphere(sphere.center, sphere.radius);
        }

        if (drawWire)
        {
            Gizmos.color = wire;
            Gizmos.DrawWireSphere(sphere.center, sphere.radius);
        }
    }

    /// <summary>胶囊沿本地轴向画。持枪时能看出上臂有没有伸进绿色躯干。</summary>
    void DrawCapsule(CapsuleCollider capsule, Color solid, Color wire)
    {
        float radius = capsule.radius;
        float height = Mathf.Max(capsule.height, radius * 2f);
        float halfCylinder = Mathf.Max(0f, height * 0.5f - radius);
        Vector3 axis = capsule.direction switch
        {
            0 => Vector3.right,
            2 => Vector3.forward,
            _ => Vector3.up
        };
        Quaternion rotation = Quaternion.FromToRotation(Vector3.up, axis);
        Gizmos.matrix = capsule.transform.localToWorldMatrix * Matrix4x4.TRS(capsule.center, rotation, Vector3.one);

        Vector3 top = Vector3.up * halfCylinder;
        Vector3 bottom = Vector3.down * halfCylinder;
        if (drawSolid)
        {
            Gizmos.color = solid;
            Gizmos.DrawSphere(top, radius);
            Gizmos.DrawSphere(bottom, radius);
            if (halfCylinder > 0.0001f)
            {
                Gizmos.DrawCube(Vector3.zero, new Vector3(radius * 2f, halfCylinder * 2f, radius * 2f));
            }
        }

        if (drawWire)
        {
            Gizmos.color = wire;
            Gizmos.DrawWireSphere(top, radius);
            Gizmos.DrawWireSphere(bottom, radius);
            Gizmos.DrawLine(top + Vector3.right * radius, bottom + Vector3.right * radius);
            Gizmos.DrawLine(top + Vector3.left * radius, bottom + Vector3.left * radius);
            Gizmos.DrawLine(top + Vector3.forward * radius, bottom + Vector3.forward * radius);
            Gizmos.DrawLine(top + Vector3.back * radius, bottom + Vector3.back * radius);
        }
    }
}
