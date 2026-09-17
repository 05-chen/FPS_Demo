# Player 预制体结构说明书

> **说明对象**：`Assets/Player/Player.prefab`（当前主 PlayerPrefab）
> **整理日期**：2026-09-15
> **用途**：按预制体真实层级梳理节点、组件、Layer、关键引用与已知缺口，方便改枪 / 调 ADS / 改 Hitbox 时对号入座。

相关装配菜单：`Tools/Build Player in Hierarchy`、`Tools/Attach Components To Selected Player`、`Tools/Setup UpperBody Weapon and ADS`。总框架见 `框架说明.md`。

---

## 一、角色定位

| 项 | 当前状态 |
| --- | --- |
| 路径 | `Assets/Player/Player.prefab` |
| 引用方 | `Testcene` NetworkManager、`GameList.asset` |
| Tag / Layer | `Player` / **Player (7)** |
| 视觉模型 | `ModelRoot` 解包自 `redSolider.fbx`（网格名仍为 `Ch49_body1` / `Ch49_body2`） |
| Avatar / Controller | `redSolider` Humanoid Avatar + `Assets/Animations/PlayerAnimatorController.controller` |
| 枪模 | `M1 Garand Lowpoly`（来自 `Assets/Model/Gun/`） |
| 联机注意 | `Testcene_GamePlay` 仍指向 `Player Test.prefab`，勿与本文件混为一谈 |

---

## 二、完整层级（手指/脚趾骨骼省略）

```
Player                          # Tag=Player, Layer=7
├── ModelRoot                   # y ≈ -0.798；Animator（applyRootMotion=false）
│   ├── Ch49_body1              # 蒙皮网格
│   ├── Ch49_body2              # 蒙皮网格
│   └── mixamorig:Hips
│       ├── LeftUpLeg → LeftLeg → LeftFoot → …
│       ├── RightUpLeg → RightLeg → RightFoot → …
│       └── Spine → Spine1 → Spine2
│           ├── LeftShoulder → LeftArm → LeftForeArm → LeftHand → …
│           ├── Neck → Head → HeadTop_End
│           └── RightShoulder → RightArm → RightForeArm → RightHand → …
│               └── WeaponSocket            # Layer=2；ShellEjector（+ 多余 WeaponADS，见下）
│                   └── M1 Garand Lowpoly   # Layer=2；weaponHolder
│                       └── SightPoint      # Layer=2；local ≈ (0, -0.193, 0.101)
└── PlayerCamera                # Layer=0；Camera + AudioListener（默认关）
    ├── muzzlePoint             # PlayerWeapon.muzzlePoint
    └── CameraSightTarget       # local ≈ (0, 0, 0.346)；ADS 屏幕中心目标
```

高度约定（须保持一致）：

- `CharacterController.height = 1.6`，`center = (0,0,0)`，`radius ≈ 0.21`
- `PlayerController.standingHeight = 1.6`
- `ModelRoot.localPosition.y ≈ -0.8`（当前约 `-0.7977`）
- `PlayerCamera.localPosition ≈ (0, 0.771, 0.158)`（站立眼高由控制器再调）

---

## 三、根节点组件

挂在 **Player** 根上：

| 组件 | 作用 / 关键字段 |
| --- | --- |
| `NetworkObject` | NGO 网络身份 |
| `ClientNetworkTransform` | Owner 权威位移/旋转同步 |
| `CharacterController` | height 1.6；**不**参与部位判定（射线穿透） |
| `PlayerController` | 移动/视角/姿态；已绑 `playerCamera`、`audioListener`、`playerWeapon`、`weaponADS`（根上那份） |
| `PlayerHealth` | maxHealth 100；倒地 / 死亡复活延迟 |
| `PlayerStatusController` | 伤情 Debuff |
| `PlayerWeapon` | `muzzlePoint`、`bulletPrefab`→`Bullet.prefab`；**`gunSound` 为空**；`proceduralRecoil` / `shellEjector` 引用目前为空 |
| `WeaponADS`（根） | 已接线：`weaponHolder`→枪模、`sightPoint`→`SightPoint`、`cameraSightTarget`→`CameraSightTarget`、`playerCamera`；腰射 `hipfirePosition (0.2,-0.2,0.4)`；FOV 70→50 |
| `ProceduralRecoil` | `weaponHolder`→枪模；`weaponADS`→根上 WeaponADS |
| `PlayerAnimationManager` | `animator`→ModelRoot；控制器/武器/ADS 已绑；`playerCamera`/`headBone` 当前为空（可运行时解析） |

`PlayerController.weaponADS` 指向**根上**的 `WeaponADS`，运行时以这份为准。

---

## 四、ModelRoot 与动画

| 项 | 值 |
| --- | --- |
| 节点 | `Player/ModelRoot` |
| `Animator.avatar` | `redSolider.fbx`（Humanoid） |
| `Animator.controller` | `PlayerAnimatorController` |
| `applyRootMotion` | **false** |
| 网格 | `Ch49_body1`、`Ch49_body2`（Ch49 / Mixamo 命名残留） |
| 骨架 | 标准 `mixamorig:*` |

蓝方换皮时换 `blueSolider` 网格 + Avatar（当前 `blueSolider.fbx` 仍为 Generic，不能直接换）。

---

## 五、部位 Hitbox（6 处）

均在对应骨骼上：`BodyPartHitbox` + Trigger Collider；Layer = 7。

| 骨骼 | `DetailedBodyPart` | Collider |
| --- | --- | --- |
| `mixamorig:Head` | Head (0) | Capsule（Trigger） |
| `mixamorig:Spine2` | Torso (1) | Box（Trigger） |
| `mixamorig:LeftUpLeg` | Legs (2) | Capsule |
| `mixamorig:RightUpLeg` | Legs (2) | Capsule |
| `mixamorig:LeftArm` | Arms (3) | Capsule |
| `mixamorig:RightArm` | Arms (3) | Capsule |

手改尺寸后不要再盲跑 Build/Attach 的 Hitbox 重建，否则会被覆盖。

---

## 六、武器与 ADS

### 6.1 节点

| 节点 | Layer | 说明 |
| --- | --- | --- |
| `…/RightHand/WeaponSocket` | Ignore Raycast (2) | 挂 `ShellEjector`；子物体为枪 |
| `WeaponSocket/M1 Garand Lowpoly` | 2 | `WeaponADS.weaponHolder` / `ProceduralRecoil.weaponHolder` |
| `…/SightPoint` | 2 | 照门；当前 local ≈ `(0, -0.193, 0.101)` |
| `PlayerCamera/CameraSightTarget` | 0 | ADS 对齐目标；local ≈ `(0, 0, 0.346)` |
| `PlayerCamera/muzzlePoint` | 0 | 开火原点 |

枪模子树应为 **Ignore Raycast**，且 **无 Collider**（防挡子弹 / 误进战区 Trigger）。可用 **Setup UpperBody Weapon and ADS** 重跑校验。

### 6.2 ADS 行为

有 `sightPoint` + `cameraSightTarget` 时：机瞄移动 `weaponHolder`，使两节点世界坐标重合，并缩 FOV。腰射回到 `hipfirePosition` / `hipfireRotation`。

Scene 微调优先拖：`SightPoint`、`CameraSightTarget`，或改根 `WeaponADS.hipLocalPosition / hipLocalRotation`。

### 6.3 已知缺口 / 脏数据

1. 当前 `WeaponADS` 只有 Player 根节点这一份，已接线到 `M1 Garand Lowpoly / SightPoint / CameraSightTarget`；`WeaponSocket` 只保留 `ShellEjector`，不要再在 Socket 上添加第二份 ADS。
2. `PlayerWeapon.gunSound`、`proceduralRecoil`、`shellEjector` 引用为空（`ShellEjector` 组件在 Socket 上，但武器脚本未拖引用）。
3. `ShellEjector` 的 `shellPrefab` / `ejectionPoint` 等仍为空。

---

## 七、PlayerCamera

| 项 | 状态 |
| --- | --- |
| 组件 | `Camera` + `AudioListener` |
| 默认 | 相机与 Listener **关闭**；进局后仅 Owner `SetViewEnabled` 打开并独占 Listener |
| 子节点 | `muzzlePoint`、`CameraSightTarget` |

---

## 八、与源模型目录的关系

视觉与 Avatar 来自 `Assets/Model/Soldier/`：

```
Assets/Model/Soldier/
├── redSolider.fbx          # 本预制体使用（Humanoid + Avatar）
├── blueSolider.fbx         # 蓝方预留（仍 Generic）
└── Animations/             # 11 个动作 FBX（Idle/Walk/Run/Fire/Reload/Jump…）
```

枪模资产：`Assets/Model/Gun/M1 Garand Lowpoly.fbx`（已实例化进本预制体，非仅挂点）。

旧文档里的 `Swat.fbx` / `Ch49_nonPBR.fbx` 已不作为当前 Player 来源；网格名 `Ch49_*` 只是 redSolider 内部残留命名。

---

## 九、改结构时怎么动手

| 目标 | 建议 |
| --- | --- |
| 从零拼一套 | `Tools → Build Player in Hierarchy`，再跑 Setup 补枪与 ADS |
| 补组件 / Hitbox / 引用 | `Tools → Attach Components To Selected Player`（幂等） |
| 补枪、Sight、CameraSight、清 Layer/Collider、接线 ADS | `Tools → Setup UpperBody Weapon and ADS` |
| 只微调机瞄 | Scene 拖 `SightPoint` / `CameraSightTarget`，Apply Prefab |
| 换枪模 | 替换 `WeaponSocket` 子物体，保持 Layer=2、无 Collider，并重绑 `weaponHolder` / `SightPoint` |
| 动画 | `Tools → Auto Bind Player Animator` 等菜单 |

---

## 十、快速核对清单

- [x] Tag=Player，Layer=7；CC height / standingHeight / ModelRoot.y 对齐
- [x] ModelRoot：Avatar + Controller，`applyRootMotion=false`
- [x] 6 处骨骼 Hitbox
- [x] M1 Garand + SightPoint + CameraSightTarget；根 `WeaponADS` 三引用已接线
- [x] 枪 Layer=2
- [x] `WeaponSocket` 上没有多余 `WeaponADS`
- [x] `ModelRoot` 已挂 `WeaponHandIK`，`enableLeftArmTwoBoneIk=true`；左手目标为枪上的 `LeftHandIK`
- [x] `WeaponSocket` 位于 `mixamorig:RightHand` 下，枪根是右手主挂点；Humanoid 骨骼解析失败时由 Avatar 映射兜底
- [ ] 绑定 `gunSound`；接线 `PlayerWeapon`→`ProceduralRecoil` / `ShellEjector`
- [ ] 实机确认腰射 / 机瞄姿态与准星居中
- [ ] 联机场景统一到本预制体（与 `Player Test.prefab` 合并）
