using UnityEngine;
using Enemy;

public enum DetailedBodyPart
{
    Head,
    Torso,
    Legs,
    Arms
}

[RequireComponent(typeof(Collider))]
public class BodyPartHitbox : MonoBehaviour
{
    public DetailedBodyPart bodyPart = DetailedBodyPart.Torso;

    public void TakeHit(string weaponType)
    {
        Debug.Log($"<color=green>[假人 Hitbox 被击中]</color> HitboxType: {bodyPart} | 武器: {weaponType}");

        PracticeDummyHealth dummyHealth = GetComponentInParent<PracticeDummyHealth>(true);
        if (dummyHealth == null)
        {
            Debug.LogWarning($"[假人 Hitbox] 父级未找到 PracticeDummyHealth，无法把 {bodyPart} 交给假人。");
            return;
        }

        dummyHealth.ApplyHit(bodyPart, weaponType);
    }

    public void OnHit(int damage)
    {
        TakeHit("Projectile");
    }

    /// <summary>
    /// 沿射线穿过假人时，躯干盒往往会挡住头/手脚。按距离排序后穿过所有 Hitbox，优先取更精确的部位。
    /// </summary>
    public static BodyPartHitbox PickBestAlongRay(RaycastHit[] hits, int count)
    {
        if (hits == null || count <= 0)
        {
            return null;
        }

        System.Array.Sort(hits, 0, count, DistanceComparer.Instance);

        BodyPartHitbox best = null;
        int bestSpecificity = -1;
        for (int i = 0; i < count; i++)
        {
            Collider col = hits[i].collider;
            if (col == null)
            {
                continue;
            }

            // CharacterController 本质也是 Collider，且体积通常大于所有骨骼 Hitbox，会挡在最近处。
            // 这里必须 continue 跳过（穿透），一旦误用 break 就会提前终止扫描，导致部位判定整体失效。
            if (col is CharacterController)
            {
                continue;
            }

            BodyPartHitbox hitbox = col.GetComponentInParent<BodyPartHitbox>();
            if (hitbox == null)
            {
                // 战区 / 出生区等 Trigger 不是遮挡物：穿透继续找部位盒。
                // 墙/地面等实心碰撞体才作为范围哨兵终止，避免把身后别人的头算进来。
                if (col.isTrigger)
                {
                    continue;
                }

                break;
            }

            int specificity = Specificity(hitbox.bodyPart);
            if (specificity > bestSpecificity)
            {
                bestSpecificity = specificity;
                best = hitbox;
            }
        }

        return best;
    }

    public static BodyPartHitbox PickBestInOverlaps(Collider[] colliders, int count)
    {
        if (colliders == null || count <= 0)
        {
            return null;
        }

        BodyPartHitbox best = null;
        int bestSpecificity = -1;
        for (int i = 0; i < count; i++)
        {
            Collider col = colliders[i];
            if (col == null)
            {
                continue;
            }

            BodyPartHitbox hitbox = col.GetComponentInParent<BodyPartHitbox>();
            if (hitbox == null)
            {
                continue;
            }

            int specificity = Specificity(hitbox.bodyPart);
            if (specificity > bestSpecificity)
            {
                bestSpecificity = specificity;
                best = hitbox;
            }
        }

        return best;
    }

    static int Specificity(DetailedBodyPart part)
    {
        switch (part)
        {
            case DetailedBodyPart.Head:
                return 3;
            case DetailedBodyPart.Arms:
            case DetailedBodyPart.Legs:
                return 2;
            default:
                return 1;
        }
    }

    sealed class DistanceComparer : System.Collections.Generic.IComparer<RaycastHit>
    {
        public static readonly DistanceComparer Instance = new DistanceComparer();

        public int Compare(RaycastHit a, RaycastHit b)
        {
            return a.distance.CompareTo(b.distance);
        }
    }
}
