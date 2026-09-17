# Unity & NGO 核心开发规范

## 1. 基础技术栈与官方文档
- Unity 2022.3+ LTS
- Netcode for GameObjects (NGO) 1.8+
- Unity API 参考: https://docs.unity3d.com/2022.3/Documentation/ScriptReference/
- NGO 官方文档: https://docs.unity3d.com/Packages/com.unity.netcode.gameobjects@1.8/manual/

## 2. 网络同步规范 (NGO 1.8+)
- 严禁使用 UNet 废弃语法（如 `[Command]`, `[ClientRpc]`，`NetworkIdentity`）。
- 涉及网络同步的类必须继承 `NetworkBehaviour`。
- 状态同步强制使用 `NetworkVariable<T>`，且必须显式声明读写权限（如 `NetworkVariableReadPermission.Everyone`, `NetworkVariableWritePermission.Server`）。
- 跨端通信使用 `[ServerRpc]` 与 `[ClientRpc]`（方法名必须以 `ServerRpc` / `ClientRpc` 结尾）。
- 输入与本地操控：必须包含 `if (!IsOwner) return;` 权限隔离。
- 网络物体生成：必须在服务端调用 `NetworkObject.Spawn()` 或 `SpawnWithOwnership()`。
- 属性写入守护：给 `NetworkVariable` 赋值前必须判断 `if (!IsSpawned) return;` 或 `if (!IsServer) return;`。
- 事件闭环：在 `OnNetworkSpawn` 中订阅网络事件/回调，必须在 `OnNetworkDespawn` 或 `OnDestroy` 中进行反订阅，防止内存泄漏。

## 3. 代码风格与现代 C# 规范 (抗代码冗余)
- **拒绝防御性过载**：优先使用现代 C# 语法（如 `is` 模式匹配、三元表达式、Lambda `=>`）。
- **杜绝冗余分支**：严禁对同一个单例（如 `NetworkManager.Singleton`）、组件或判空逻辑在单帧流程内重复写 3 次以上的 `if (...) return;`。
- **卫语句精简**：顶层统一做主防护，内部核心逻辑保持紧凑流畅，不要将单行语句拆成多行 `if` 嵌套。
- **日志规范**：统一使用项目内部的 `GameLog.Info()` / `GameLog.Warning()` / `GameLog.Error()`，禁止直接写 `Debug.Log()`。

## 4. 注释与文档保护规范 (重要)
- **保护现有注释**：在进行局部修改或优化时，**严禁无故删除或裁切现有代码的文档注释（///）与行内注释**。除非该函数/方法被完整重构或废弃，否则必须原样保留。
- **核心逻辑强制注释**：新编写或重构的方法/类，必须使用 `/// <summary>` 添加清晰简练的注释，解释其核心作用与设计意图。
- **关键分支说明**：对于网络同步（Rpc、NetworkVariable 响应）、复杂数学计算或状态切换等关键逻辑，必须在代码旁附带简洁的行内注释说明。

## 5. C# 命名规范
- **类名 / 属性 / 方法名**：使用大驼峰（PascalCase），如 `PlayerController`, `TakeDamage`。
- **私有成员变量**：使用小驼峰并加下划线前缀，如 `_moveSpeed`, `_targetArea`。
- **函数参数 / 局部变量**：使用小驼峰（lowerCamelCase），如 `damageAmount`, `clientId`。
- **命名空间**：核心底层逻辑归入 `Core` 命名空间；场景与战区逻辑归入 `World`；UI 逻辑归入 `UI`。