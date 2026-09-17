# 📚 Unity 与 C# 综合进阶与实战补充知识清单

## 一、 IMGUI 模块补充（绘制机制、事件驱动与布局对比）

### 1. 绘图层级（GUI.depth）与渲染顺序

- **控制原理**：`GUI.depth` 用于控制 IMGUI 控件的前后遮盖顺序。  
- **规则**：**`GUI.depth` 的值越小，绘制层级越靠前（显示在最上层）**。若在同一个脚本或多个 OnGUI 脚本中未指定，则按默认渲染顺序遮挡。  

### 2. 事件处理机制（Event.current）

- **驱动机制**：IMGUI 并非只在渲染时触发，而是由 `Event.current` 事件驱动（包含 `Repaint`、`Layout`、`MouseDown`、`KeyDown` 等事件类型）。  
- **事件吃掉/拦截（Event.Use）**：在开发自定义编辑器工具（Editor Window）或 Scene 视图交互时，调用 `e.Use()` 可以屏蔽/吃掉当前事件，防止该点击或按键继续向下传递给 Scene 视图或 Unity 引擎底层。  

C#

```
private void OnGUI()
{
    // 1. 设置 GUI 绘制层级 (值越小越靠前显示)
    GUI.depth = 0;[cite: 1]

    // 2. 获取当前 IMGUI 事件
    Event e = Event.current;[cite: 1]
    if (e.type == EventType.MouseDown && e.button == 0)[cite: 1]
    {
        // 当在 IMGUI 界面触发鼠标左键按下时
        Debug.Log($"鼠标点击位置: {e.mousePosition}");[cite: 1]
        
        // 3. 屏蔽/吃掉当前事件（防止事件继续传递给 Scene 窗口等）
        // e.Use();
    }
}
```

### 3. GUI vs GUILayout vs EditorGUILayout 深度对比

在 Unity 的 IMGUI 体系中，绘图 API 主要分为三套系统，它们的定位与适用场景有本质区别：

| **维度**         | **GUI (绝对定位模式)**                         | **GUILayout (自动布局模式)**                                 | **EditorGUILayout (编辑器专有自动布局)**                     |
| ---------------- | ---------------------------------------------- | ------------------------------------------------------------ | ------------------------------------------------------------ |
| **所属命名空间** | `UnityEngine`                                  | `UnityEngine`                                                | `UnityEditor`                                                |
| **运行环境**     | 运行时 (Runtime) & 编辑器 (Editor)             | 运行时 (Runtime) & 编辑器 (Editor)                           | **仅编辑器 (Editor Only)**                                   |
| **位置计算方式** | **手动硬编码**：必须传入 `Rect` 确定位置与宽高 | **自动计算**：自动顺排布局，可配合 `BeginHorizontal/Vertical` | **自动计算**：自动顺排，且默认贴合 Inspector/Editor 样式风格 |
| **核心特性**     | 性能最高，无布局计算开销；但多分辨率适配极难写 | 无需手动计算坐标；底层有布局计算开销                         | 封装了大量 Unity 原生 SerializedProperty、ObjectField、Slider 等高级控件 |

#### ⚠️ 项目实战与编辑器开发避坑要点：

1. **`EditorGUILayout` 绝对不能出现在运行时代码中**：

   `EditorGUILayout` 属于 `UnityEditor` 命名空间，游戏打包（Build）时包含该代码会导致编译报错。若脚本同时在运行时与编辑器使用，必须加宏隔离（`#if UNITY_EDITOR`）。

2. **`SerializedProperty` 与数据绑定**：

   在编写自定义 Inspector 时，优先使用 `EditorGUILayout.PropertyField` 绘制属性，而不是普通的 `TextField`。因为 `PropertyField` 会**自动处理 Undo/Redo（撤销重做）、预制体修改高亮（Prefab Override）以及多选编辑**。

#### 代码示例：三种模式的对比写法

C#

```
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

public class IMGUI_Layout_Comparison : MonoBehaviour
{
    public string playerName = "Default";
    public Texture2D avatar;
}

#if UNITY_EDITOR
[CustomEditor(typeof(IMGUI_Layout_Comparison))]
public class ComparisonEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // 1. GUI 模式 (基于绝对坐标 Rect)
        GUI.Label(new Rect(10, 10, 150, 20), "GUI (Absolute Pos)");

        // 2. GUILayout 模式 (运行时自动布局)
        GUILayout.BeginVertical("box");
        GUILayout.Label("GUILayout (Auto Layout)");
        if (GUILayout.Button("Runtime Auto Button"))
        {
            Debug.Log("Clicked GUILayout");
        }
        GUILayout.EndVertical();

        // 3. EditorGUILayout 模式 (编辑器专有高级自动布局)
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("EditorGUILayout (Editor Only)", EditorStyles.boldLabel);

        serializedObject.Update();
        SerializedProperty avatarProp = serializedObject.FindProperty("avatar");
        EditorGUILayout.PropertyField(avatarProp, new GUIContent("玩家头像")); // 对象拖拽框

        SerializedProperty nameProp = serializedObject.FindProperty("playerName");
        EditorGUILayout.PropertyField(nameProp, new GUIContent("玩家名称"));
        serializedObject.ApplyModifiedProperties(); // 提交修改并支持 Undo
    }
}
#endif
```

## 二、 Unity 核心 API 与运行机制进阶（性能陷阱与替代方案）

### 1. 完整生命周期执行图谱

严格执行顺序如下（必须掌握 `Awake` $\rightarrow$ `OnEnable` $\rightarrow$ `Start` 的严格次序）：  

- **Initialization（初始化）**：`Awake` $\rightarrow$ `OnEnable` $\rightarrow$ `Start`

    

- **Physics（物理逻辑）**：`FixedUpdate` $\rightarrow$ 内部物理模拟 $\rightarrow$ Trigger / Collision 回调  

- **Game Logic（游戏逻辑）**：`Update` $\rightarrow$ 协程 yield 逻辑 $\rightarrow$ `LateUpdate`

    

- **Rendering（渲染阶段）**：`OnRenderObject` $\rightarrow$ `OnGUI`

    

- **Decommission（销毁/失活）**：`OnDisable` $\rightarrow$ `OnDestroy`

    

> **编辑器特有生命周期扩展**：
>
> - **`OnValidate()`**：在 Inspector 中拖拽赋值或修改参数时触发，适合做参数合法性校验。
> - **`[ExecuteInEditMode]` / `[ExecuteAlways]`**：允许 MonoBehavior 脚本在编辑器未运行（Edit Mode）时执行生命周期（如 `Update` / `OnGUI`），常用于开发关卡编辑器或特效预览。

### 2. 协程（Coroutine）机制与 GC 避坑

协程是 Unity 中进行分帧、异步处理和延时逻辑的核心工具。  

C#

```
using System.Collections;
using UnityEngine;

public class CoroutineLesson : MonoBehaviour
{
    private Coroutine myCoroutine;

    // 预先缓存等待对象，避免在 Coroutine 中频繁 new 产生 GC Alloc
    private WaitForSeconds waitForTwoSeconds = new WaitForSeconds(2.0f);

    private void Start()
    {
        myCoroutine = StartCoroutine(TestCoroutine());[cite: 1]
    }

    private IEnumerator TestCoroutine()
    {
        Debug.Log("第一帧执行");[cite: 1]
        yield return null; // 等待下一帧

        Debug.Log("第二帧执行");[cite: 1]
        yield return new WaitForFixedUpdate(); // 等待下一个物理帧

        // ❌ 差性能做法：yield return new WaitForSeconds(2.0f); (每次都会产生堆内存分配)
        // ✅ 推荐做法：使用外部缓存的变量
        yield return waitForTwoSeconds;[cite: 1]

        // 不受 Time.timeScale 影响的缩放等待
        yield return new WaitForSecondsRealtime(2.0f);[cite: 1]
    }

    private void Stop()
    {
        if (myCoroutine != null)
        {
            StopCoroutine(myCoroutine);[cite: 1]
        }
    }
}
```

### 3. 高频核心 API 性能规避（Camera / Transform）

- **`Camera.main` 的性能缓存**： 在老版本 Unity 中，每次调用 `Camera.main` 都会执行一次 `GameObject.FindGameObjectsWithTag("MainCamera")` 查找。虽然现代 Unity（2020.2+）对内部做了一定缓存，但频繁在 `Update` 中访问依然会产生 Native-to-Managed（C++ 与 C# 跨界）的开销。  

  - **商业项目标准做法**：在 `Awake` 或静态单例类中初始化并缓存 `Camera.main` 引用。

- **`transform.position` vs `transform.localPosition`**：

  访问 `position` 需要从父节点逐级向下计算世界坐标矩阵；访问 `localPosition` 仅读取本地相对坐标。高频移动计算时，优先计算 `localPosition`。

- **Rigidbody 移动约束**： 带刚体的物体严禁直接修改 `transform.position`，因为这会破坏物理引擎的碰撞预测逻辑并引发碰撞体重新计算，带有 Rigidbody 的移动必须使用 `Rigidbody.MovePosition()` 或 `Rigidbody.linearVelocity`。  

## 三、 物理系统（Physics）补全与高精无 GC 检测

### 1. 基础射线检测（Physics.Raycast）与 LayerMask 位运算

利用 `LayerMask` 做位运算屏蔽不需要检测的图层。  

C#

```
void Update()
{
    // 构造射线：从摄像机发射到鼠标点击位置
    Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);[cite: 1]
    RaycastHit hitInfo;[cite: 1]

    // LayerMask 使用位运算：只检测第 8 层 "Enemy" (1 << 8)
    int layerMask = 1 << LayerMask.NameToLayer("Enemy");[cite: 1]

    // 发射射线 (射线, 碰撞输出信息, 检测最大距离, 图层掩码)
    if (Physics.Raycast(ray, out hitInfo, 100f, layerMask))[cite: 1]
    {
        Debug.Log("击中了对象: " + hitInfo.collider.name);[cite: 1]
        Debug.Log("击中点坐标: " + hitInfo.point);[cite: 1]
        Debug.Log("击中法线方向: " + hitInfo.normal);[cite: 1]

        Debug.DrawLine(ray.origin, hitInfo.point, Color.red);[cite: 1]
    }
}
```

### 2. 进阶无 GC 射线检测（NonAlloc 机制）

在商业项目中，常规 `Physics.RaycastAll` 或 `Physics.OverlapSphere` 每次调用都会在堆（Heap）上分配一个新的数组返回，高频调用会导致大量的 GC 垃圾。

- **优化方案**：统一使用带 **`NonAlloc`** 后缀的 API（如 `Physics.RaycastNonAlloc`），将检测结果填充进预先分配好的数组容器中。

C#

```
private RaycastHit[] hitBuffer = new RaycastHit[10]; // 预分配缓存数组

void HighPerformanceRaycast()
{
    Ray ray = new Ray(transform.position, transform.forward);
    int layerMask = 1 << LayerMask.NameToLayer("Enemy");

    // 返回实际击中的数量，结果直接填充进入 hitBuffer，避免产生堆内存分配！
    int count = Physics.RaycastNonAlloc(ray, hitBuffer, 100f, layerMask);
    for (int i = 0; i < count; i++)
    {
        Debug.Log($"Hit: {hitBuffer[i].collider.name}");
    }
}
```

### 3. 静态碰撞体移动陷阱（Static Collider Warning）

- **原理**：如果给 GameObject 加了 `Collider` 却没有加 `Rigidbody`，它会被物理引擎标记为 **Static Collider**。
- **避坑**：若直接用代码修改 Static Collider 的 Transform，会导致 Unity 强行重新构建场景的物理空间哈希树（Spatial Hash Tree），消耗极高。**要移动的碰撞体必须挂载 Rigidbody**（若不受重力影响，可勾选 `IsKinematic = true`）。

## 四、 C# 基础、内存管理与 GC 深度优化

### 1. 装箱与拆箱（Boxing & Unboxing）与隐藏 GC

- **概念**：
  - **装箱**：将值类型转换为引用类型（如 `object`），会在堆内存中分配空间，并将值拷贝过去。  
  - **拆箱**：将引用类型强制转换为值类型，从堆中获取值并拷贝回栈。  

C#

```
int val = 10;
object obj = val;       // 装箱：产生堆内存分配 (GC Alloc)
int newVal = (int)obj;  // 拆箱：需要类型强转，消耗 CPU 性能

// 隐式装箱避坑：
string s1 = "Age: " + val;           // ❌ 错误：产生隐式装箱
string s2 = "Age: " + val.ToString(); // ✅ 正确：直接调用 ToString()，无装箱
```

- **企业级高频避坑**：
  - **`GameObject.CompareTag()` 代替 `.tag`**：`if (go.tag == "Enemy")` 会产生字符串堆内存分配，使用 `if (go.CompareTag("Enemy"))` 可做到 0 GC。
  - **`GetComponents<T>(List<T> results)` 代替 `GetComponents<T>()`**：前者会将组件填充至传入的列表，避免每次返回新数组。

### 2. 字符串不可变性与 StringBuilder / ZString

- **不可变性（Immutability）**：`string` 每次修改（如 `+` 拼接）都会在堆内存中创建一个全新的字符串对象，旧对象等待 GC 回收。  
- **优化方案**：
  1. 频繁拼接字符串时，使用 `System.Text.StringBuilder`。  
  2. 在中大型商业项目中，广泛采用如 `Cysharp.ZString` 等第三方零分配（Zero Allocation）字符串格式化库。

C#

```
using System.Text;

StringBuilder sb = new StringBuilder();[cite: 1]
for (int i = 0; i < 100; i++)[cite: 1]
{
    sb.Append("Index: ");[cite: 1]
    sb.Append(i);[cite: 1]
}
string result = sb.ToString();[cite: 1]
```

## 五、 UGUI 核心架构与现代化 UI 体系

### 1. UGUI 核心三要素

| **核心要素**               | **机制与项目应用重点**                                       |
| -------------------------- | ------------------------------------------------------------ |
| **Canvas (画布)**          | **渲染模式**： 1. **Screen Space - Overlay**：屏幕空间覆盖，永远最前，无需 Camera。   2. **Screen Space - Camera**：摄像机模式，由指定 Camera 渲染，可实现 3D UI 穿插。   3. **World Space**：世界空间，变成 3D 场景中的平面（如 NPC 头顶血条、VR 界面）。 |
| **EventSystem**[cite: 1]   | 负责接收输入信号，通过 **Graphics Raycaster** 分发事件[cite: 1]。 ⚠️ **性能优化**：取消无交互 Image/Text 上的 **Raycast Target** 勾选，能极大降低射线检测开销。 |
| **RectTransform**[cite: 1] | 继承自 Transform[cite: 1]。 1. **Anchors (锚点)**：决定相对父节点定位与自适应拉伸的基准[cite: 1]。 2. **Pivot (中心点)**：决定自身旋转、缩放与定位的中心[cite: 1]。 |

### 2. 渲染机制与 Canvas 动静分离

- **Canvas 重绘 (Canvas Rebatching)**：一个 Canvas 下的任意 UI 元素发生更新，该 Canvas 下的所有 UI 元素都会重新计算顶点并合并 Mesh。
- **最佳实践：动静分离**
  - `Static Canvas`：放置背景、框架等从不更新的 UI。
  - `Dynamic Canvas`：放置血条、小地图、常态移动/闪烁的 UI。

### 3. 核心控件与 UI 无限循环列表

- **Text vs TextMeshPro (TMP)**： 项目**强制使用 TextMeshPro (TMP)**[cite: 1]。TMP 利用 SDF（Signed Distance Field）矢量渲染技术，支持高品质描边、发光，且修改文本时性能消耗远低于 Legacy Text。

- **ScrollView 性能优化**：

  当 ScrollView 中有成百上千个列表项（如物品栏、排行榜）时，禁止直接创建所有项。项目标准方案为**循环复用列表（Loop/Recycling ScrollView）**，即仅创建可视区域内的少量 GameObject，通过滑动滚动更新数据。

### 4. 常用代码交互示例

C#

```
using UnityEngine;
using UnityEngine.UI; 
using TMPro; // 使用 TextMeshPro[cite: 1]

public class UGUILesson : MonoBehaviour
{
    public Button myButton;[cite: 1]
    public Toggle myToggle;[cite: 1]
    public TMP_InputField myInputField; // 使用 TMP 替代传统 InputField[cite: 1]
    public Slider mySlider;[cite: 1]

    private void Start()
    {
        // 1. Button 点击事件监听[cite: 1]
        myButton.onClick.AddListener(() =>
        {
            Debug.Log("UGUI 按钮被点击");[cite: 1]
        });

        // 2. Toggle 状态改变监听[cite: 1]
        myToggle.onValueChanged.AddListener((isOn) =>
        {
            Debug.Log($"Toggle 当前状态: {isOn}");[cite: 1]
        });

        // 3. Slider 滑块数值变化监听[cite: 1]
        mySlider.onValueChanged.AddListener((val) =>
        {
            Debug.Log($"当前进度/音量: {val}");[cite: 1]
        });

        // 4. InputField 输入结束监听[cite: 1]
        myInputField.onEndEdit.AddListener((text) =>
        {
            Debug.Log($"用户输入的内容为: {text}");[cite: 1]
        });
    }

    private void OnDestroy()
    {
        // 移除监听，防止内存泄漏或空指针回调[cite: 1]
        myButton.onClick.RemoveAllListeners();[cite: 1]
        myToggle.onValueChanged.RemoveAllListeners();
        mySlider.onValueChanged.RemoveAllListeners();
        myInputField.onEndEdit.RemoveAllListeners();
    }
}
```

## 六、 现代 Unity 商业项目技术栈扩展（架构与技术选型）

```
                         ┌─────────────────────────────────────────┐
                         │      现代 Unity 商业项目技术栈            │
                         └────────────────────┬────────────────────┘
                                              │
         ┌────────────────────────────────────┼────────────────────────────────────┐
         ▼                                    ▼                                    ▼
┌──────────────────┐                ┌──────────────────┐                ┌──────────────────┐
│  Addressables    │                │   UI Toolkit     │                │  Job System +    │
│  可寻址资源管理系统  │                │ 新一代 UI 解决方案 │                │  Burst Compiler  │
└────────┬─────────┘                └────────┬─────────┘                └────────┬─────────┘
         │                                    │                                    │
  替代传统 AssetBundle                类似 HTML+CSS (UXML+USS)              多线程并行计算，
  自动处理依赖与热更新                无 Canvas 重绘，高性能               突破 C# 性能瓶颈
```

1. **Addressables（可寻址资源管理系统）**：
   - 彻底解决传统 `Resources.Load` 占满首包和 `AssetBundle` 手动管理依赖繁琐的问题，提供一套集加载、卸载、引用计数、远端热更新于一体的标准方案。
2. **UI Toolkit (UIBuilder + UXML + USS)**：
   - 官方主推的新一代 UI 体系，采用类似前端 HTML + CSS 的解耦架构。目前已全面接管 Unity 编辑器工具开发，并在运行时 UI 中逐步取代 UGUI。
3. **Job System + Burst Compiler**：
   - 利用 CPU 多核并行计算（如大量 NPC 寻路、弹幕轨迹计算），配合 Burst 编译器将 C# 代码直接编译成极致优化的高性能机器码。