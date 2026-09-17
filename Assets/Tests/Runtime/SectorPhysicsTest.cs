using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace World
{
    /// <summary>
    /// 黑盒物理测试：动态生成 Dummy（CharacterController）与枪模 Collider，穿过据点 Trigger 并断言计数。
    /// 路径：Assets/Tests/Runtime。由 SectorTestHarness 在 Auto-Host 完成后通过 F5 / UI 按钮启动。
    /// </summary>
    public sealed class SectorPhysicsTest : MonoBehaviour
    {
        public static int LastPassed { get; private set; }
        public static int LastFailed { get; private set; }

        static int _runSerial;
        static bool _busy;
        Coroutine _running;

        [ContextMenu("运行黑盒物理 Trigger 测试")]
        public void RunFromHarness()
        {
            if (_busy)
            {
                GameLog.Warn("SectorTest", "黑盒物理测试正在进行，忽略重复的 F5。");
                return;
            }

            _running = StartCoroutine(RunAndAssert());
        }

        /// <summary>供 PlayMode Test Runner 与场景按钮共用。</summary>
        public static IEnumerator RunAndAssert()
        {
            if (_busy)
            {
                GameLog.Warn("SectorTest", "黑盒物理测试已在运行，跳过本次。");
                yield break;
            }

            _busy = true;
            SectorTestReport.Suite("黑盒-Trigger 物理过滤");
            int passed = 0;
            int failed = 0;
            int serial = ++_runSerial;
            var created = new List<GameObject>();
            StrongpointArea area = null;
            try
            {
                // 每次换位置，避免上次 Destroy 延迟或重复 F5 留下的 Dummy 叠在同一圈里。
                Vector3 zonePos = new Vector3(400f + serial * 40f, 220f, 400f);
                GameObject zoneGo = new GameObject("SectorPhysics_Zone_" + serial);
                created.Add(zoneGo);
                zoneGo.transform.position = zonePos;
                SphereCollider sphere = zoneGo.AddComponent<SphereCollider>();
                sphere.isTrigger = true;
                sphere.radius = 2.5f;
                Rigidbody zoneBody = zoneGo.AddComponent<Rigidbody>();
                zoneBody.isKinematic = true;
                zoneBody.useGravity = false;
                area = zoneGo.AddComponent<StrongpointArea>();

                yield return new WaitForFixedUpdate();
                yield return new WaitForFixedUpdate();
                int baseline = area.DebugOccupantCount;
                if (!SectorTestReport.Check(
                        "黑盒-初始圈内人数为 0",
                        baseline == 0,
                        "实际=" + baseline + "（若刚按过 F5，请等本次跑完再测）"))
                {
                    failed++;
                }
                else
                {
                    passed++;
                }

                GameObject gun = CreateGunMesh(zonePos + Vector3.forward * 8f);
                created.Add(gun);
                yield return MoveRigidbodyInto(gun, zonePos);
                int afterGun = area.DebugOccupantCount;
                if (!SectorTestReport.Check(
                        "黑盒-枪模穿过 Trigger 不计入人数",
                        afterGun == baseline,
                        "枪模进入后人数=" + afterGun + "（应为 " + baseline + "）"))
                {
                    failed++;
                }
                else
                {
                    passed++;
                }

                GameObject dummy = CreateDummy(zonePos + Vector3.forward * 8f, 9200u + (ulong)serial);
                created.Add(dummy);
                CharacterController controller = dummy.GetComponent<CharacterController>();
                yield return MoveControllerInto(controller, zonePos);
                int afterBody = area.DebugOccupantCount;
                if (!SectorTestReport.Check(
                        "黑盒-CharacterController 穿过后人数 +1",
                        afterBody == baseline + 1,
                        "身体进入后人数=" + afterBody + "（应为 " + (baseline + 1) + "）"))
                {
                    failed++;
                }
                else
                {
                    passed++;
                }

                yield return MoveControllerInto(controller, zonePos + Vector3.forward * 16f);
                Physics.SyncTransforms();
                yield return new WaitForFixedUpdate();
                yield return new WaitForFixedUpdate();
                int afterLeave = area.DebugOccupantCount;
                if (!SectorTestReport.Check(
                        "黑盒-Dummy 离开 Trigger 后人数归零",
                        afterLeave == baseline,
                        "离开后人数=" + afterLeave + "（应为 " + baseline + "）"))
                {
                    failed++;
                }
                else
                {
                    passed++;
                }

                yield return MoveControllerInto(controller, zonePos);
                created.Remove(dummy);
                controller.enabled = false;
                dummy.SetActive(false);
                area.CleanInvalidEntries();
                Object.DestroyImmediate(dummy);
                dummy = null;
                area.CleanInvalidEntries();
                Physics.SyncTransforms();
                yield return new WaitForFixedUpdate();
                int afterDestroy = area.DebugOccupantCount;
                if (!SectorTestReport.Check(
                        "黑盒-Dummy Destroy 后人数归零",
                        afterDestroy == baseline,
                        "销毁后人数=" + afterDestroy + "（应为 " + baseline + "）"))
                {
                    failed++;
                }
                else
                {
                    passed++;
                }
            }
            finally
            {
                for (int i = 0; i < created.Count; i++)
                {
                    if (created[i] == null)
                    {
                        continue;
                    }

                    created[i].SetActive(false);
                    Object.DestroyImmediate(created[i]);
                }

                _busy = false;
            }

            yield return null;
            SectorTestReport.Summary("黑盒-Trigger 物理过滤", passed, failed);
            LastFailed = failed;
            LastPassed = passed;
            if (failed > 0)
            {
                GameLog.Error("SectorTest", "黑盒物理测试存在失败项，请查看上方 [FAIL]。");
            }
        }

        static GameObject CreateDummy(Vector3 position, ulong clientId)
        {
            GameObject dummy = new GameObject("SectorPhysics_Dummy");
            dummy.tag = "Player";
            dummy.transform.position = position;
            CharacterController controller = dummy.AddComponent<CharacterController>();
            controller.center = new Vector3(0f, 1f, 0f);
            controller.height = 2f;
            controller.radius = 0.4f;
            SectorTestDummy marker = dummy.AddComponent<SectorTestDummy>();
            marker.AssignClientId(clientId);
            return dummy;
        }

        static GameObject CreateGunMesh(Vector3 position)
        {
            GameObject gun = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gun.name = "SectorPhysics_GunMesh";
            gun.tag = "Untagged";
            gun.transform.position = position;
            gun.transform.localScale = new Vector3(0.2f, 0.2f, 0.8f);
            BoxCollider box = gun.GetComponent<BoxCollider>();
            box.isTrigger = false;
            box.enabled = true;
            Rigidbody body = gun.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            return gun;
        }

        static IEnumerator MoveRigidbodyInto(GameObject mover, Vector3 target)
        {
            Rigidbody body = mover.GetComponent<Rigidbody>();
            Vector3 start = mover.transform.position;
            const int steps = 16;
            for (int i = 1; i <= steps; i++)
            {
                Vector3 next = Vector3.Lerp(start, target, i / (float)steps);
                body.MovePosition(next);
                Physics.SyncTransforms();
                yield return new WaitForFixedUpdate();
            }
        }

        static IEnumerator MoveControllerInto(CharacterController controller, Vector3 target)
        {
            const int steps = 16;
            for (int i = 0; i < steps; i++)
            {
                Vector3 remaining = target - controller.transform.position;
                remaining.y = 0f;
                controller.Move(remaining / Mathf.Max(1, steps - i));
                Physics.SyncTransforms();
                yield return new WaitForFixedUpdate();
            }

            controller.Move(target - controller.transform.position);
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
        }
    }
}
