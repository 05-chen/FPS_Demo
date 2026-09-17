---

# 📚 模块一：C# 语言核心进阶（OOP 与高级语法）

---

## 1. 面向对象（OOP）基础

### 1.1 类（class）与对象、构造函数、析构函数、静态成员

#### 💡 概念解析

* **类与对象**：类是蓝图（抽象），对象是根据蓝图在内存中创建的实例（具体）。
* **构造函数 (Constructor)**：实例化时自动调用的函数，用于初始化成员变量。C# 支持构造函数重载与 `: this()` 构造函数函数链调用。
* **析构函数 (Finalizer)**：形如 `~ClassName()`，由垃圾回收器（GC）在销毁对象前隐式调用，**在游戏开发中严禁手动写入复杂逻辑**，因为 GC 决定其调用时机，极易引发非预期问题。
* **静态成员 (static)**：属于类本身而非对象实例，保存在 High-Frequency Heap（高频堆）中，随程序运行常驻内存。
* **静态构造函数**：用于初始化静态变量，**在类首次被使用（访问静态成员或创建首个实例）时自动且仅执行一次**，无访问修饰符且不能带有参数。

#### 💻 核心 API 与 C# 代码示例

```csharp
public class Monster
{
    public string name;
    public int hp;
    
    // 静态成员：记录怪物总诞生数
    public static int TotalSpawnCount { get; private set; }

    // 静态构造函数：仅在首次使用 Monster 类时执行一次
    static Monster()
    {
        TotalSpawnCount = 0;
        Debug.Log("Monster 系统初始化完成");
    }

    // 构造函数链调用
    public Monster() : this("未知怪物", 100) { }

    public Monster(string name, int hp)
    {
        this.name = name;
        this.hp = hp;
        TotalSpawnCount++; // 每次实例化，静态计数自增
    }

    // 析构函数（尽量避免使用，非托管资源释放优先实现 IDisposable）
    ~Monster()
    {
        // 垃圾回收时执行
    }
}

```

#### ⚠️ 易错点与注意事项

1. **静态成员 GC 无法回收**：静态变量引用的对象会一直持有引用路径（GCHandle），导致该对象极其关联资源**永久无法被 GC 回收**。在 Unity 中切记在场景卸载或单例销毁时手动将静态引用置 `null`。
2. **静态构造函数的死锁风险**：若两个类的静态构造函数在多线程中互相依赖访问对方的静态成员，会导致严重的线程死锁。

---

### 1.2 结构体（struct）与类（class）的区别、内存分配与值/引用类型

#### 💡 概念解析

* **值类型 (Value Type)**：包含 `struct`、`enum`、所有数值类型（`int`, `float`, `Vector3` 等）。通常分配在栈（Stack）上，随作用域结束由系统直接弹出，极快且 **0 GC 压力**。
* **引用类型 (Reference Type)**：包含 `class`、`interface`、`delegate`、`string`、数组等。实例本身分配在堆（Heap）上，栈上仅保存指向堆地址的指针。销毁依赖 GC，产生内存回收开销。

#### 📊 核心对比表

| 维度             | 结构体 Struct (值类型)                                     | 类 Class (引用类型)                                      |
| ---------------- | ---------------------------------------------------------- | -------------------------------------------------------- |
| **内存分配**     | 栈（Stack）或所在宿主对象的内部                            | 托管堆（Heap）                                           |
| **赋值行为**     | **深拷贝**（按字节完整复制内容）                           | **浅拷贝**（仅复制堆内存的引用地址）                     |
| **默认构造函数** | C# 10 之前不能显式写无参构造；默认清零                     | 自动提供默认无参构造函数                                 |
| **继承性**       | 隐式继承自 `System.ValueType`，**不支持继承**其他类/结构体 | 支持多层单继承                                           |
| **适用场景**     | 轻量级数据容器（如 `Vector3`, `Color`, `Ray`）             | 复杂逻辑体、生命周期长的对象（如 `Player`, `UIManager`） |

#### ⚠️ 易错点与注意事项

1. **结构体装箱 (Boxing) 陷阱**：若将结构体传参给 `object` 或接口类型（如 `IComparable`），会导致结构体被拷贝并装箱到堆上，直接产生 **GC Alloc**。
2. **修改临时 struct 属性报错**：在 C# 中，若访问 `List<MyStruct>[0].x = 5;` 会直接编译报错。因为索引器返回的是 struct 的**一份栈拷贝**，修改这份临时拷贝没有任何意义。

---

## 2. OOP 三大特性与高级语法

### 2.1 继承、封装、多态（virtual / override / abstract / interface）

#### 💡 概念解析

* **封装**：使用 `private`, `protected`, `internal`, `public` 隐匿内部实现，仅暴露安全接口。
* **继承**：子类复用父类代码（`base` 关键字）。
* **多态 (Polymorphism)**：
* **`virtual` / `override*`*：虚方法重写。父类提供默认实现，子类按需重写（动态绑定，通过虚函数表 VTable 寻址）。
* **`abstract`**：抽象类与抽象方法。父类只声明契约不提供实现，子类**必须**重写。抽象类不能被实例化。
* **`interface`**：接口。完全剥离实现的规范契约，类可以实现**多个接口**（解决 C# 单继承限制）。



#### 💻 核心 API 与 C# 代码示例

```csharp
public interface IDamageable
{
    void TakeDamage(int amount); // 接口契约
}

public abstract class BaseEntity : MonoBehaviour
{
    public string entityName;

    // 抽象方法：子类必须实现
    public abstract void Init();

    // 虚方法：子类可选重写
    public virtual void OnDeath()
    {
        Debug.Log($"{entityName} 死亡，播放通用特效");
    }
}

public class Player : BaseEntity, IDamageable
{
    public override void Init()
    {
        entityName = "主角";
    }

    // 实现接口
    public void TakeDamage(int amount)
    {
        Debug.Log($"玩家受到 {amount} 点伤害");
    }

    // 重写父类虚方法
    public override void OnDeath()
    {
        base.OnDeath(); // 调用父类通用逻辑
        Debug.Log("弹出游戏失败 UI 面板");
    }
}

```

---

### 2.2 索引器、运算符重载与扩展方法

#### 💡 概念解析

* **索引器 (Indexer)**：允许对象像数组一样使用 `obj[index]` 进行访问。
* **运算符重载 (operator)**：自定义类或结构体在进行 `+`, `-`, `==` 等运算时的逻辑。
* **扩展方法 (Extension Methods)**：在**不修改原类代码、不继承原类**的前提下，为现有的类型（甚至系统类型/第三方库）追加新的方法。
* *规则*：必须在**静态类**中定义，且首个参数前加 **`this`** 关键字。



#### 💻 核心 API 与 C# 代码示例

```csharp
// 1. 扩展方法示例：扩展 Unity Transform 的能力
public static class TransformExtensions
{
    // 重置 Transform
    public static void ResetLocal(this Transform trans)
    {
        trans.localPosition = Vector3.zero;
        trans.localRotation = Quaternion.identity;
        trans.localScale = Vector3.one;
    }
}

// 2. 索引器与运算符重载
public struct InventorySlot
{
    public int itemId;
    public int count;

    // 重载 + 运算符：合并同类物品数量
    public static InventorySlot operator +(InventorySlot a, InventorySlot b)
    {
        if (a.itemId == b.itemId)
        {
            return new InventorySlot { itemId = a.itemId, count = a.count + b.count };
        }
        return a;
    }
}

public class PlayerInventory
{
    private InventorySlot[] slots = new InventorySlot[20];

    // 索引器：通过 playerInventory[i] 直接访问格子
    public InventorySlot this[int index]
    {
        get => slots[index];
        set => slots[index] = value;
    }
}

// 调用处：
// transform.ResetLocal(); 

```

---

### 2.3 深拷贝 (Deep Copy) 与 浅拷贝 (Shallow Copy)

#### 💡 概念解析

* **浅拷贝 (Shallow Copy)**：创建一个新对象，复制当前对象的所有字段。若字段是**值类型**则复制一份值；若字段是**引用类型**，则**仅复制引用地址**（新旧对象共享同一个底层子引用对象）。
* *实现*：C# 提供 `MemberwiseClone()` 基础实现。


* **深拷贝 (Deep Copy)**：创建一个新对象，递归复制当前对象及其**所有引用的子对象**。新旧对象完全隔离，互不影响。

#### 💻 核心 API 与 C# 代码示例

```csharp
public class Weapon : ICloneable
{
    public string weaponName;

    public object Clone() => new Weapon { weaponName = this.weaponName };
}

public class Character : ICloneable
{
    public int level;
    public Weapon currentWeapon;

    // 浅拷贝
    public Character ShallowCopy()
    {
        return (Character)this.MemberwiseClone();
    }

    // 深拷贝
    public Character DeepCopy()
    {
        Character clone = (Character)this.MemberwiseClone();
        // 显式为引用类型字段独立创建新对象
        clone.currentWeapon = (Weapon)this.currentWeapon.Clone();
        return clone;
    }
}

```

---

## 3. 泛型与集合容器

### 3.1 泛型类、泛型方法与泛型约束（where）

#### 💡 概念解析

泛型实现了**代码重用与类型安全**，彻底避免了使用 `object` 导致的频繁**装箱/拆箱与类型强转**开销。通过 `where` 关键字可以限制泛型必须具备的特性。

#### 📌 常用泛型约束汇总

* `where T : struct` 必须是值类型。
* `where T : class` 必须是引用类型。
* `where T : new()` 必须包含无参构造函数。
* `where T : BaseClass` 必须继承自特定类。
* `where T : ISomeInterface` 必须实现特定接口。

#### 💻 核心 API 与 C# 代码示例

```csharp
// 游戏常用：单例基类 / 对象池基类
public class MonoSingleton<T> : MonoBehaviour where T : MonoSingleton<T>
{
    private static T instance;
    public static T Instance => instance;

    protected virtual void Awake()
    {
        if (instance == null)
        {
            instance = (T)this;
        }
        else
        {
            Destroy(gameObject);
        }
    }
}

// 泛型方法 + 多重约束
public class ResManager
{
    public T LoadAsset<T>(string path) where T : UnityEngine.Object, new()
    {
        return Resources.Load<T>(path);
    }
}

```

---

### 3.2 常用集合容器及底层内存原理

在 Unity 中，数据结构的选择直接决定了 **内存 CPU Cache Line 命中率** 以及 **GC 触发频率**。

#### 📊 核心数据结构原理与游戏场景选择

```
  [ Array / List<T> ] ── 内存物理连续 ──> CPU Cache 友好 ──> 适用：高频遍历 (如实体更新)
  [ Dictionary<K,V> ] ── 哈希桶映射 ───> O(1) 快速检索 ───> 适用：配置表、ID 查实体
  [ LinkedList<T> ]   ── 指针节点分散 ──> Cache Miss 严重 ──> 适用：中途高频插入/删除 (建议慎用)

```

| 容器类型                    | 数据结构底层实现                  | 查找复杂度      | 插入/删除复杂度   | GC & 内存特性                                         | 游戏开发高频场景                               |
| --------------------------- | --------------------------------- | --------------- | ----------------- | ----------------------------------------------------- | ---------------------------------------------- |
| **`List<T>`**               | 动态连续数组                      | $O(1)$ (按下标) | $O(N)$ (中间插删) | **超容量触发 2 倍扩容**，产生老数组垃圾               | 实体列表、对象池主容器                         |
| **`Dictionary<K,V>`**       | 双数组（Buckets桶 + Entries实体） | $O(1)$          | $O(1)$            | **Key 若为 Struct 未实现 `IEquatable` 会引发装箱 GC** | ID 查找 GameObject、配置表                     |
| **`LinkedList<T>`**         | 双向链表节点                      | $O(N)$          | $O(1)$ (已知节点) | 每个节点都是引用对象，独立 `new` **极易造成堆碎片**   | 频繁中途插入销毁的临时链（更推荐隐式数组链表） |
| **`Stack<T>`**              | 连续数组实现（FILO 后进先出）     | $O(1)$ (看栈顶) | $O(1)$ (压/弹栈)  | 自动动态扩容                                          | **UI 栈管理（页面回退）**                      |
| **`Queue<T>`**              | 环形数组实现（FIFO 先进先出）     | $O(1)$ (看队头) | $O(1)$ (入/出队)  | 自动动态扩容                                          | 网络消息包队列、技能释放队列                   |
| **`ArrayList / Hashtable`** | 内部全部基于 `object[]`           | $O(1)$          | $O(1)$            | **全都是装箱拆箱陷阱，C# 现代开发全面弃用**           | 仅存老旧项目（现代开发严格禁止使用）           |

#### ⚠️ 避坑指南：`List<T>` 与 `Dictionary` 的性能优化

1. **预设 Capacity（容量）**：
`List` 默认扩容机制是按原容量 $2$ 倍重新分配更大数组并拷贝原数据。如果在 `Update` 中高频 `Add` 触发扩容，会产生大量丢弃的旧数组垃圾。**创建时明确预估大小**：`var list = new List<int>(128);`
2. **Enum 作为 Dictionary Key 避坑**：
旧版 Unity 中使用 Enum 作为 Key 会隐式触发装箱 GC。**解决方案**：实现 `IEqualityComparer<T>` 接口，或强转成 `int` 作为 Key。

---

## 4. 委托、事件与 Lambda 表达式

### 4.1 委托（delegate）、内置委托（Action / Func）与事件（event）

#### 💡 概念解析

* **委托 (Delegate)**：面向对象的、类型安全的方法指针容器，可将函数作为参数传递，支持多播（`+=`）。
* **`Action` / `Func*`*：C# 提供的预定义通用泛型委托。
* `Action<T1, T2>`：无返回值。
* `Func<T1, ReturnType>`：带返回值，最后一个泛型参数代表返回值类型。


* **事件 (event)**：对委托的**安全封装**。
* *区别*：使用了 `event` 关键字修饰的委托，**外部类只能进行 `+=` 和 `-=` 操作，禁止在外部直接调用（Invoke）或赋值 `null*`*（保护内部监听者不被外部意外清空）。



#### 💻 核心 API 与 C# 代码示例

```csharp
public class PlayerHP
{
    // 定义事件（推荐使用 Action 内置委托）
    public event Action<float> OnHpChanged; 

    private float currentHp = 100f;

    public void Damage(float value)
    {
        currentHp -= value;
        // 安全触发事件
        OnHpChanged?.Invoke(currentHp); 
    }
}

public class HealthBarUI : MonoBehaviour
{
    [SerializeField] private PlayerHP player;

    private void OnEnable()
    {
        // 订阅事件
        player.OnHpChanged += UpdateHealthBar;
    }

    private void OnDisable()
    {
        // ⚠️ 取消订阅（必须！否则造成严重内存泄漏）
        player.OnHpChanged -= UpdateHealthBar;
    }

    private void UpdateHealthBar(float hp)
    {
        Debug.Log($"刷新 UI 血条为: {hp}");
    }
}

```

---

### 4.2 匿名函数、Lambda 表达式与闭包捕获内存陷阱

#### 💡 概念解析

* **Lambda 表达式**：`(参数) => { 函数体 }` 语法糖，用于快速编写内联匿名函数。
* **闭包 (Closure)**：当 Lambda 表达式访问了其**作用域外部的局部变量**时，编译器会自动生成一个隐式的类（Closure Class），并将被捕获的变量作为该类的成员变量。

#### ⚠️ 易错点：闭包产生的 GC Alloc 与循环捕获陷阱

```csharp
// ❌ 错误示范：在 Update 中触发严重的堆内存分配 (GC Alloc)
void Update()
{
    int score = 10;
    // 闭包捕获了外部变量 score！
    // 编译器每一帧都会在堆上 new 一个匿名闭包对象，GC 瞬间爆表！
    DoSomething(() => { 
        Debug.Log(score); 
    });
}

// ❌ 经典陷阱：循环变量捕获
void SpawnButtons()
{
    for (int i = 0; i < 3; i++)
    {
        // 错误：所有的 Lambda 都捕获了同一个 i 的引用，最终点击都会输出 3！
        // btn.onClick.AddListener(() => Debug.Log(i)); 

        // ✅ 正确做法：引入局部变量进行拷贝隔离
        int index = i;
        btn.onClick.AddListener(() => Debug.Log(index));
    }
}

```

---

## 5. 反射、特性与异步编程

### 1. 特性（Attribute）

#### 💡 概念解析

为代码元素（类、结构体、方法、字段等）附加元数据标记。可以在运行时通过反射提取这些标记，实现高度解耦的逻辑控制。

#### 💻 核心 API 与 C# 代码示例

```csharp
// 自定义特性：标记某个方法为网络协议处理器
[AttributeUsage(AttributeTargets.Method)]
public class NetworkHandlerAttribute : Attribute
{
    public int MsgId { get; }
    public NetworkHandlerAttribute(int msgId) => MsgId = msgId;
}

// 应用特性
public class BattleController
{
    [NetworkHandler(1001)]
    public void OnPlayerMove(string data)
    {
        Debug.Log("处理玩家移动消息: " + data);
    }
}

```

---

### 2. 反射（Reflection）机制及游戏实战

#### 💡 概念解析

允许程序在**运行时**获取程序集（Assembly）的结构，动态创建对象、访问私有成员或调用函数。

* **优点**：极度灵活，可用于实现自动化配置表映射、UI 自动绑定、Excel 转数据类。
* **缺点**：**性能开销极大**（存在大量字符串匹配与类型安全校验），且 IL2CPP 打包时容易因代码裁剪（Code Stripping）导致找不到反射成员。

#### 💻 核心 API 与 C# 代码示例

```csharp
// 结合上述特性的反射应用：自动注册网络消息
public class NetworkManager
{
    public void AutoRegisterHandlers(object controller)
    {
        Type type = controller.GetType();
        // 获取所有 Method
        MethodInfo[] methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        foreach (var method in methods)
        {
            // 检查是否带有自定义特性
            var attr = method.GetCustomAttribute<NetworkHandlerAttribute>();
            if (attr != null)
            {
                Debug.Log($"成功绑定网络消息 ID [{attr.MsgId}] 到方法 [{method.Name}]");
            }
        }
    }
}

```

---

### 3. 多线程与 async / await 异步编程

#### 💡 概念解析

* **Unity 主线程限制**：**所有 Unity API（如 `Transform`, `GameObject`, `Instantiate`）只能在主线程调用！** 子线程中访问会直接抛出 `UnityException`。
* **多线程 (Thread / Task)**：适用于纯 C# 的复杂数学计算、解压缩、网络 Socket 数据解包。
* **`async / await`**：C# 基于状态机的异步编程模型，能够写出同步风格的异步逻辑。配合第三方库（如 **UniTask**）是现代 Unity 开发的主流替代协程（Coroutine）方案。

#### 💻 核心 API 与 C# 代码示例

```csharp
using System.Threading.Tasks;
using UnityEngine;

public class AsyncLesson : MonoBehaviour
{
    private async void Start()
    {
        Debug.Log($"1. 开始加载，主线程 ID: {System.Threading.Thread.CurrentThread.ManagedThreadId}");

        // 切换到后台线程做耗时密集计算
        int result = await Task.Run(() => CalculateHeavyData());

        // await 会自动切回 Unity 主线程上下文 (Context)
        Debug.Log($"2. 计算完毕结果: {result}，当前线程 ID: {System.Threading.Thread.CurrentThread.ManagedThreadId}");

        // 可以安全调用 Unity API
        transform.position = Vector3.zero;
    }

    private int CalculateHeavyData()
    {
        // 模拟后台耗时计算（严禁在此调用 Unity API！）
        System.Threading.Thread.Sleep(1000);
        return 999;
    }
}

```

---









# 📚 模块二：Unity 基础与 2D / 渲染组件

## 1. 音频系统（Audio）

### 1.1 AudioSource、AudioClip 与 AudioListener

#### 💡 概念解析

- **AudioClip**：音频资源本体（如 `.wav`, `.mp3`），保存在内存或磁盘中的波形/压缩数据。
- **AudioSource**：音频播放器组件。负责指定播放哪一个 `AudioClip`，控制音量、音调（Pitch）、3D 空间音效衰减以及环绕声等。
- **AudioListener**：音频接收器/耳朵。听取场景中所有 `AudioSource` 发出的声音并输出到终端设备。**整个场景中有且只能有一个激活的 `AudioListener`**（通常挂载在 Main Camera 上）。

#### 💻 核心 API 与 C# 代码示例

C#

```
[RequireComponent(typeof(AudioSource))]
public class AudioPlayerExample : MonoBehaviour
{
    private AudioSource audioSource;
    public AudioClip bgmClip;
    public AudioClip sfxClip;

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
    }

    public void PlayBGM()
    {
        audioSource.clip = bgmClip;
        audioSource.loop = true; // BGM 开启循环
        audioSource.Play();
    }

    // 播放一次性音效（如击中、爆炸、UI 点击）
    public void PlaySFX()
    {
        // PlayOneShot 不会打断当前正在播放的音效，支持多个音效叠加播放！
        audioSource.PlayOneShot(sfxClip, 0.8f); 
    }
}
```

### 1.2 工业级 SoundManager 音频管理器设计思路

#### 💡 架构设计

在商业项目中，绝不能在每个 GameObject 上随意挂载 `AudioSource` 播放背景音和音效。通常采用**单例模式 + 对象池**设计的 `SoundManager`：

1. **分通道管理 (AudioMixer / AudioGroup)**：将音频划分为 BGM（背景音）、SFX（环境/战斗音效）、UI（界面音效）、Voice（配音）等通道，支持独立调节音量。
2. **BGM 渐隐切换 (CrossFade)**：使用双 `AudioSource` 配合协程/DOTween 实现新旧 BGM 的平滑渐入渐出（Fade In/Out）。
3. **音效对象池 (AudioSource Pool)**：预先生成若干个 `AudioSource` 节点。播放音效时从池中取出，播放完毕后回收，避免频繁 `Instantiate` 产生垃圾。

## 2. 2D 游戏开发体系

### 2.1 SpriteRenderer 渲染器与渲染层级（Sorting Layer & Order in Layer）

#### 💡 概念解析

在 2D 游戏中，物体的遮挡关系**不仅取决于 Z 轴深度**，更取决于渲染图层顺序。

#### 📌 2D 渲染排序优先级（从高到低）

$$\text{Sorting Layer (排序图层)} \longrightarrow \text{Order in Layer (层内顺序)} \longrightarrow \text{Distance to Camera (相机距离/Z轴)}$$

1. **Sorting Layer**：在 `Project Settings -> Tags & Layers` 中自定义图层列表。**越靠下的图层越晚渲染（覆盖在最上层）**。
2. **Order in Layer**：相同 Sorting Layer 内的整数序号。**数值越大越靠前显示**。
3. **Isometric 2.5D 遮挡排序 (Transparency Sort Mode)**：在斜 45 度视角（如《星露谷物语》）中，人物走到树后需要被树遮挡，走到树前需要遮挡树。需要在 Project Settings 中将 Sorting Axis 设为 $(0, 1, 0)$，使**Y 轴坐标越高（越靠上）的物体越早渲染（被遮挡）**。

### 2.2 Tilemap（瓦片地图）系统与 2D 物理

#### 💡 概念解析

- **Tilemap**：由 `Grid` 父节点托管的网格系统，内部包含多个 `Tilemap` 子图层（如 Ground、Obstacles、Decoration）。

- **Tile Palette (瓦片色板)**：刷地图的工具面板，将 Sprite 拖入色板生成 `Tile` 资产。

- **Composite Collider 2D (组合碰撞体)**：

  当在 Tilemap 上为几千个格子分别添加 `Tilemap Collider 2D` 时，会产生数千个独立的物理碰撞体，导致严重卡顿。

  - **优化方案**：在 Tilemap 挂载 **`Composite Collider 2D`**，并将 `Tilemap Collider 2D` 的 **`Used By Composite`** 勾选。物理引擎会自动将相连的矩形格子合并为一个完整的多边形碰撞 Mesh，极大提升 2D 物理性能！

```
【未优化】数千个独立格子碰撞体 (物理开销大)      【优化后】Composite Collider 合并为一个完整边缘 Mesh
┌───┬───┬───┬───┐                              ┌───────────────┐
│   │   │   │   │     ─── Used By Composite ───>   │               │
├───┼───┼───┼───┤                              │               │
│   │   │   │   │                              └───────────────┘
└───┴───┴───┴───┘
```

## 3. 资源与场景管理

### 3.1 SceneManager 与场景异步加载（AsyncOperation）

#### 💡 概念解析

同步加载场景 `SceneManager.LoadScene()` 会造成主线程卡死（界面冻结）。商业项目必须使用 `LoadSceneAsync` 进行异步加载，并结合协程或 `async/await` 绘制 Loading 进度条。

#### 💻 核心 API 与 C# 代码示例

C#

```
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class SceneLoader : MonoBehaviour
{
    public Slider progressBar;

    public void LoadNextScene(string sceneName)
    {
        StartCoroutine(LoadSceneAsyncRoutine(sceneName));
    }

    private IEnumerator LoadSceneAsyncRoutine(string sceneName)
    {
        // 发起异步加载
        AsyncOperation asyncOp = SceneManager.LoadSceneAsync(sceneName);

        // 阻止场景在加载完成后自动跳转（常用于等待玩家点击“按任意键继续”）
        asyncOp.allowSceneActivation = false;

        // asyncOp.progress 的取值范围是 0.0 ~ 0.9
        // 0.9 代表场景资源已加载完毕，正在等待激活
        while (asyncOp.progress < 0.9f)
        {
            float progress = Mathf.Clamp01(asyncOp.progress / 0.9f);
            if (progressBar != null) progressBar.value = progress;
            yield return null; // 分帧处理
        }

        if (progressBar != null) progressBar.value = 1.0f;
        Debug.Log("场景加载完毕，准备切换...");

        // 允许激活场景进行实际跳转
        asyncOp.allowSceneActivation = true;
    }
}
```

### 3.2 Resources 动态资源加载与卸载

#### 💡 概念解析

- **`Resources` 机制**：将资源放在名为 `Resources` 的文件夹下，打包时无论资源是否被引用，都会被强制打包进安装包首包中。
- **适用场景**：小型游戏快速原型开发、动态加载配置。
- **大型项目禁忌**：导致首包体积过大、内存占用高。在商业大中型项目中，`Resources` 已经全面被 **Addressables（可寻址资源系统）** 替代。

#### 💻 核心 API 与 C# 代码示例

C#

```
public void ManageResourcesExample()
{
    // 1. 动态异步加载 Prefab 资源
    ResourceRequest request = Resources.LoadAsync<GameObject>("Prefabs/Effect");
    request.completed += (op) =>
    {
        GameObject prefab = request.asset as GameObject;
        Instantiate(prefab);
    };

    // 2. 卸载未使用的无用资源 (如切场景时调用)
    // 警告：这是一项极其耗时的操作，建议在切场景 Loading 时调用！
    Resources.UnloadUnusedAssets();

    // 3. 显式卸载指定非 GameObject 资源 (如 Texture, AudioClip)
    // 绝对不能用于卸载 Prefab/GameObject 节点！
    // Resources.UnloadAsset(myTexture);
}
```

## 4. 向量数学基础在游戏逻辑中的高频应用

数学是游戏逻辑与 3D 物理的核心。掌握点乘与叉乘的几何意义能轻松解决 90% 以上的视角与方向判断问题。

### 4.1 向量点乘 (Dot Product) 实战

#### 📐 核心数学公式

$$\vec{A} \cdot \vec{B} = \vert{}\vec{A}\vert{}\vert{}\vec{B}\vert{}\cos\theta$$

若 $\vec{A}$ 和 $\vec{B}$ 为**单位向量（Normalized）**，则 $\vec{A} \cdot \vec{B} = \cos\theta$。

#### 💡 游戏高频场景：扇形 FOV 视野判定与前后方向判断

判断玩家 `Player` 是否发现了处于面前扇形视野范围内的敌人 `Enemy`：

C#

```
public bool IsEnemyInFOV(Transform player, Transform enemy, float maxAngle, float maxRadius)
{
    Vector3 toEnemy = enemy.position - player.position;

    // 1. 距离判断 (使用 sqrMagnitude 消除开根号开销)
    if (toEnemy.sqrMagnitude > maxRadius * maxRadius) return false;

    // 2. 方向点乘判断
    Vector3 forward = player.forward;
    Vector3 dirToEnemy = toEnemy.normalized; // 归一化为单位向量

    float dotResult = Vector3.Dot(forward, dirToEnemy);
    
    // dotResult > 0 说明在玩家前方，< 0 说明在玩家后方
    // 计算当前夹角
    float angle = Mathf.Acos(dotResult) * Mathf.Rad2Deg;

    // 3. 判断夹角是否在半视野角以内
    return angle <= maxAngle * 0.5f;
}
```

### 4.2 向量叉乘 (Cross Product) 实战

#### 📐 核心数学公式

$$\vec{C} = \vec{A} \times \vec{B}$$

结果是一个**垂直于 $\vec{A}$ 和 $\vec{B}$ 所构成平面的新向量 $\vec{C}$**，方向遵循**右手定则**。

#### 💡 游戏高频场景：判断左右方向与转向控制

在 3D 俯视角或 2D 游戏中，判断目标 `Target` 在玩家的**左侧还是右侧**（用于控制角色向左/向右闪避或转向）：

C#

```
public string GetTargetLeftOrRight(Transform player, Transform target)
{
    Vector3 forward = player.forward;
    Vector3 dirToTarget = (target.position - player.position).normalized;

    // 计算叉乘向量
    Vector3 crossResult = Vector3.Cross(forward, dirToTarget);

    // 在 Unity 坐标系中（Y 轴朝上）：
    // 若 Y 分量 > 0，说明 target 在 player 的右侧
    // 若 Y 分量 < 0，说明 target 在 player 的左侧
    if (crossResult.y > 0)
    {
        return "目标在右侧";
    }
    else if (crossResult.y < 0)
    {
        return "目标在左侧";
    }
    return "目标正前或正后";
}
```







没问题！下面为你带来“模块三：Unity 中高级核心系统”。

这一模块是 Unity 商业项目开发中**逻辑最密、组件联动最多、面试考查频率极高**的部分。整体笔记延续前两个模块的工业级标准，包含底层渲染/计算原理、状态机设计与数据持久化避坑指南。

# 📚 模块三：Unity 中高级核心系统

## 1. UGUI 系统全解

### 1.1 三大 Canvas 渲染模式

| **模式**                   | **渲染机制与坐标系**                                         | **摄像机依赖**      | **适用场景**                                  |
| -------------------------- | ------------------------------------------------------------ | ------------------- | --------------------------------------------- |
| **Screen Space - Overlay** | 永远覆盖在所有 3D/2D 场景对象的最前端；坐标系直接映射屏幕像素 | 无需摄像机          | 常规 HUD、主界面 UI、系统弹窗                 |
| **Screen Space - Camera**  | UI 由指定 Camera 渲染，拥有实际的 Z 轴深度；可实现 UI 与 3D 物品/特效的层级穿插 | 必须绑定指定 Camera | 带 3D 模型预览的 UI、需要 UI 后处理特效的界面 |
| **World Space**            | UI 变成 3D 场景中的一个平面，受场景 3D 摄像机裁切与透视变换影响 | 使用场景主摄像机    | NPC 头顶血条/名字、VR 交互界面、场景指示牌    |

### 1.2 自动布局组件与 ContentSizeFitter

#### 💡 核心组件

- **Horizontal / Vertical Layout Group**：将子节点按水平或垂直方向自动排列。
- **Grid Layout Group**：将子节点按固定网格（行列）排列。
- **ContentSizeFitter**：根据子内容的实际宽高（Preferred Size），动态改变当前 RectTransform 的 Size。

#### ⚠️ 常见组合：动态高度滚动列表 (ScrollView + VerticalLayoutGroup + ContentSizeFitter)

- **实现方式**：在 ScrollView 的 `Content` 节点上挂载 `Vertical Layout Group` 和 `Content Size Fitter`（Vertical Fit 设为 *Preferred Size*）。当动态向 Content 添加子项时，Content 的高度会自动撑开，从而实现原生平滑滚动。
- **性能提醒**：自动布局组件在子节点数量较多时，每一帧修改顶点开销极大，**无限循环列表请勿使用 AutoLayout 组件**。

### 1.3 EventSystem 事件系统与 Raycast 响应接口

#### 💡 接口响应机制

除了通过 Inspector 绑定 `Button.onClick` 外，UGUI 提供了更加灵活的事件响应接口。只需在脚本中实现 `UnityEngine.EventSystems` 命名空间下的接口，即可捕获输入事件：

C#

```
using UnityEngine;
using UnityEngine.EventSystems;

// 实现点击、拖拽、鼠标指针进出接口
public class CustomUIElement : MonoBehaviour, 
    IPointerClickHandler, 
    IDragHandler, 
    IPointerEnterHandler, 
    IPointerExitHandler
{
    public void OnPointerClick(PointerEventData eventData)
    {
        // 判断右键点击
        if (eventData.button == PointerEventData.InputButton.Right)
        {
            Debug.Log("UI 被鼠标右键点击！");
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        // 随鼠标/手指移动 UI (将屏幕增量坐标应用到 UI)
        transform.position += (Vector3)eventData.delta;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        // 鼠标悬停高亮/显示 Tips 提示框
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        // 隐藏 Tips 提示框
    }
}
```

### 1.4 RectTransform 锚点机制（Pivot、Anchors 与 Rect 计算原理）

#### 💡 概念解析

- **Pivot (中心点/轴心)**：`[0,0]` 到 `[1,1]` 之间的规范化坐标。决定了当前 UI 元素的**旋转中心、缩放中心**以及在父节点中的定位基准点。
- **Anchors (锚点)**：由 `Min(x,y)` 和 `Max(x,y)` 四个点构成的矩形，相对父节点定义。
  - **锚点合拢 (Point Anchor)**：`Min == Max`。此时 UI 元素保持**固定宽高**，坐标表现为 `AnchoredPosition`（相对锚点的偏移）。
  - **锚点拉伸 (Stretch Anchor)**：`Min != Max`。此时 UI 元素会随着父节点尺寸变化而**自动拉伸**，坐标表现为 `Left, Right, Top, Bottom` 边距。

```
【锚点合拢 (Point)】                  【锚点拉伸 (Stretch)】
  Min == Max                            Min != Max
┌───────────────┐                     ┌───────────────┐
│       ▲       │                     │ ┌───────────┐ │ ◄─ Top
│       │ PosY  │                     │ │           │ │
│   ───►●       │ (AnchoredPosition)  │ │   UI      │ │
│       │       │                     │ └───────────┘ │ ◄─ Bottom
└───────────────┘                     └───────────────┘
                                        ▲           ▲
                                      Left        Right
```

## 2. Animator / Animation 动画系统

### 2.1 新旧动画系统与 Animation Clip 对比

- **Legacy Animation (老动画系统)**：基于 `Animation` 组件，直接播放 `AnimationClip`。性能开销相对低，但缺乏状态机逻辑控制，适合简单的 UI 动画、开门/关门等简单机械动画。
- **Mecanim Animator (新动画系统)**：基于 `Animator` 组件与 `Animator Controller` 状态机。支持角色骨骼重定向 (Avatar)、动画混合树、层级遮罩与 IK，适合复杂的角色战斗与移动逻辑。

### 2.2 Animator 状态机、Transition 与参数控制

#### 💡 核心概念

- **State (状态)**：包含一个 `AnimationClip`。
- **Transition (状态过度)**：连接两个状态的箭头。
  - **`Has Exit Time`**：若勾选，必须等待当前动画播放到指定百分比后才允许切换；**动作打击类游戏取消勾选**以确保受击/闪避能瞬间切入。
  - **`Fixed Duration` & `Transition Duration`**：控制新旧动画混合过度的平滑时间。
- **Parameter (控制参数)**：包含 `Float`, `Int`, `Bool`, `Trigger`。

#### 💻 核心 API 与 C# 代码示例

C#

```
public class PlayerAnimatorController : MonoBehaviour
{
    private Animator animator;

    // 推荐：使用 Hash 值替代字符串进行参数设置，消除字符串哈希查找开销
    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int IsDeadHash = Animator.StringToHash("IsDead");
    private static readonly int AttackTriggerHash = Animator.StringToHash("Attack");

    private void Awake()
    {
        animator = GetComponent<Animator>();
    }

    public void UpdateMove(float speed)
    {
        animator.SetFloat(SpeedHash, speed);
    }

    public void PerformAttack()
    {
        // Trigger 触发后会自动由状态机重置
        animator.SetTrigger(AttackTriggerHash);
    }
}
```

### 2.3 混合树 (Blend Tree)、动画层级 (Layers) 与 IK (逆向动力学)

#### 💡 核心机制

1. **Blend Tree (混合树)**：根据参数平滑混合多个动画 Clip。

   - **1D Blend Tree**：单参数控制（如：根据 `Speed` 在 *站立 $\rightarrow$ 走路 $\rightarrow$ 跑步* 之间融合）。
   - **2D Simple Directional**：双参数控制（如：根据 `MoveX` 和 `MoveY` 在 *前/后/左/右* 移步动画间自由混合）。

2. **Animator Layers & Mask (动画分层与遮罩)**：

   - 可以通过 Layer 实现“下半身播放跑步动画，上半身播放开枪/挥剑动画”。
   - 在子图层配置 **Avatar Mask**，勾选需要受该图层控制的骨骼节点（如仅勾选上半身）。设置 Layer 的 **Weight (权重)** 为 1.0。

3. **IK (Inverse Kinematics 逆向动力学)**：

   常规动画是 FK（正向动力学：父骨骼带动子骨骼）。IK 则是“指定手/脚终点位置，由算法自动计算肘部/膝盖的弯曲角度”。

   - *应用场景*：角色踩在斜坡/阶梯上时，脚掌精准贴合地面 (Foot IK)；手部精准抓取不同大小的枪械/物体。

### 2.4 动画事件 (Animation Events) 触发机制

#### 💡 概念解析

在 `AnimationClip` 的时间轴帧上直接插入事件标记，动画播放到该帧时会**自动触发 GameObject 脚本上对应的同名 C# 函数**。

- **高频场景**：播放挥剑动画到第 15 帧时触发伤害判定；播放脚步动画到达落地帧时播放脚步声 `PlayFootstepSFX()`。
- **易错点/坑点**：若动画事件绑定的 C# 方法在脚本中被删改或重命名，运行到该帧时会直接在 Console 报错 `AnimationEvent has no function name...`。

## 3. NavMesh 导航寻路系统

### 3.1 NavMesh 烘焙、NavMeshAgent 与 NavMeshObstacle

#### 💡 核心组件

1. **NavMesh Bake (网格烘焙)**：将场景中标记为 `Navigation Static` 的地形与障碍物进行几何分析，生成 NPC 可通行的多边形网格。
2. **NavMeshAgent (寻路代理)**：挂载在 NPC 上，负责路径规划与移动控制。
   - *常用 API*：`agent.SetDestination(Vector3 target)`；`agent.isStopped = true;`。
3. **NavMeshObstacle (动态障碍物)**：挂载在临时移动/出现的物体上（如可被关上的铁门、推倒的木箱）。
   - **`Carve` (雕刻)**：勾选后，该障碍物会在烘焙好的 NavMesh 上实时“挖出”一个洞，迫使周围 Agent 重新计算寻路路径。

### 3.2 Off-Mesh Link (非网格链接)

#### 💡 概念解析

当两个孤立的 NavMesh 网格之间存在断崖、栅栏或需要跃下的高台时，寻路代理默认无法穿越。

- **Off-Mesh Link** 用于在两个断开的网格之间手动或自动建立“连接通路”。
- **应用场景**：处理 NPC 跳跃过沟壑、从高墙翻下、爬梯子等非平地寻路动作。
- **代码控制**：通过监听 `agent.isOnOffMeshLink`，当 Agent 进入 Link 时暂停自动寻路，播放翻越/跳跃自定义动画，完成后调用 `agent.CompleteOffMeshLink()` 恢复寻路。

## 4. 物理系统进阶

### 4.1 射线检测（Physics.Raycast）在四大场景中的实战应用

#### 场景 1：FPS 枪械射击与弹孔生成

C#

```
public class GunRaycast : MonoBehaviour
{
    public Transform muzzlePoint; // 枪口位置
    public GameObject bulletHolePrefab; // 弹孔贴图 Prefab

    public void Shoot()
    {
        Ray ray = new Ray(muzzlePoint.position, muzzlePoint.forward);
        int layerMask = ~(1 << LayerMask.NameToLayer("Player")); // 忽略玩家自己

        if (Physics.Raycast(ray, out RaycastHit hit, 100f, layerMask))
        {
            // 在击中点生成弹孔，法线方向贴合墙面
            Instantiate(bulletHolePrefab, hit.point + hit.normal * 0.01f, Quaternion.LookRotation(hit.normal));
            
            // 如果击中敌人
            if (hit.collider.CompareTag("Enemy"))
            {
                // hit.collider.GetComponent<IDamageable>()?.TakeDamage(10);
            }
        }
    }
}
```

#### 场景 2：3D 鼠标拾取物体 (ScreenRaycast)

C#

```
void Update()
{
    if (Input.GetMouseButtonDown(0))
    {
        // 从摄像机发射射线通过屏幕鼠标坐标
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 50f))
        {
            Debug.Log($"玩家拾取了物品: {hit.collider.gameObject.name}");
        }
    }
}
```

#### 场景 3：角色 Ground Check (接地检测)

相比于 Collider 回调，使用射线向下检测判断角色是否站在地面上更精准，能彻底避免“贴墙滑行导致无限连跳”的 Bug：

C#

```
public bool CheckIsGrounded(Transform playerTransform, float rayLength = 0.2f)
{
    // 从脚底稍微偏上的位置向下发射短射线
    Vector3 origin = playerTransform.position + Vector3.up * 0.1f;
    int groundLayer = 1 << LayerMask.NameToLayer("Ground");
    
    return Physics.Raycast(origin, Vector3.down, rayLength, groundLayer);
}
```

## 5. 数据持久化

### 5.1 PlayerPrefs 本地简单存储

- **原理**：Unity 提供的轻量级键值对存储。在 Windows 上写入注册表，在 iOS/Android 上写入 Local App 偏好文件。
- **API**：`PlayerPrefs.SetInt()`, `PlayerPrefs.GetString()`, `PlayerPrefs.Save()`。
- **适用场景**：保存音量设置、语言偏好、画质选项。
- **禁忌**：**严禁用于保存玩家核心数据**（如金币、等级、背包），因为文件明文保存极其容易被玩家开挂修改。

### 5.2 三种序列化方案对比与实战 (XML vs JSON vs 二进制)

#### 📊 方案特性对比表

| **维度**     | **XML**                    | **JSON**                    | **Binary (二进制 / Protobuf)**       |
| ------------ | -------------------------- | --------------------------- | ------------------------------------ |
| **可读性**   | 高 (基于标签)              | 极高 (基于键值对)           | 无法直接阅读                         |
| **文件体积** | 大 (包含冗余闭合标签)      | 中等                        | **极小** (数据紧凑)                  |
| **解析速度** | 较慢                       | 快                          | **极快**                             |
| **应用场景** | 复杂配置表、编辑器工具存储 | 本地存档、Web HTTP API 交互 | **网络 Socket 数据包、核心加密存档** |

#### 💻 JSON 序列化实战代码 (JsonUtility / Newtonsoft.Json)

C#

```
using System;
using UnityEngine;

[Serializable]
public class PlayerSaveData
{
    public string playerName;
    public int level;
    public float[] position; // 数组可直接被序列化
}

public class SaveSystem
{
    // 保存为 JSON 文件
    public static void SaveData(PlayerSaveData data)
    {
        // JsonUtility 是 Unity 内置序列化工具，性能极高
        string json = JsonUtility.ToJson(data, true);
        string path = Application.persistentDataPath + "/save.json";
        
        System.IO.File.WriteAllText(path, json);
        Debug.Log("存档保存成功至: " + path);
    }

    // 从 JSON 加载
    public static PlayerSaveData LoadData()
    {
        string path = Application.persistentDataPath + "/save.json";
        if (System.IO.File.Exists(path))
        {
            string json = System.IO.File.ReadAllText(path);
            return JsonUtility.FromJson<PlayerSaveData>(json);
        }
        return new PlayerSaveData(); // 返回默认数据
    }
}
```









压轴的 **“模块四：游戏架构与进阶”** 来了！

这一模块是衡量一个开发者能否从“写功能小妹/小弟”迈向“独立架构师/中高级程序”的核心水岭。它覆盖了**商业项目中最常用的三大架构模块**以及**热更新与网络底层的硬核原理**。

# 📚 模块四：游戏架构与进阶

## 1. UI 框架与常用单例架构

在商业项目中，UI 界面成百上千，绝不能用 `SetActive(true/false)` 硬编码控制。标准解决方案是设计一套基于 **层级（Layer）与栈（Stack）** 的 `UIManager`。

### 1.1 界面管理器（UIManager）层级与栈管理

#### 💡 架构设计原理

1. **界面层级划分 (UI Layers)**：
   - **Bottom / Normal（常驻/普通层）**：主界面 HUD、小地图、角色状态栏。
   - **PopUp（弹窗层）**：背包、商店、任务面板（遵循**后进先出 LIFO** 栈管理）。
   - **System / Top（系统/顶层）**：网络断线重连提示、系统 Loading、跑马灯。
2. **栈管理（Stack Management）**：
   - 打开新弹窗时，将当前弹窗 `Push` 入栈并暂停/遮罩下层；
   - 点击关闭或按 ESC 时，从栈顶 `Pop` 出当前界面并销毁/隐藏，同时自动恢复上一个界面的交互。

#### 💻 核心架构 C# 实现示例

C#

```
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 1. UI 面板基类
public abstract class BasePanel : MonoBehaviour
{
    public virtual void OnEnter() { gameObject.SetActive(true); }  // 打开界面
    public virtual void OnPause() { }                              // 被上层面板遮挡
    public virtual void OnResume() { }                             // 上层关闭，恢复响应
    public virtual void OnExit() { gameObject.SetActive(false); }  // 关闭界面
}

// 2. UIManager 单例核心
public class UIManager : MonoBehaviour
{
    private static UIManager instance;
    public static UIManager Instance => instance;

    // 保存面板 Prefab 路径的字典
    private Dictionary<string, string> panelPathDict = new Dictionary<string, string>();
    // 实例化后的面板缓存
    private Dictionary<string, BasePanel> panelDict = new Dictionary<string, BasePanel>();
    // 弹窗 UI 栈
    private Stack<BasePanel> panelStack = new Stack<BasePanel>();

    [SerializeField] private Transform popUpLayer; // 弹窗挂载父节点

    private void Awake()
    {
        instance = this;
    }

    // 入栈并打开 UI
    public BasePanel PushPanel(string panelName)
    {
        // 1. 暂停栈顶上一个界面
        if (panelStack.Count > 0)
        {
            BasePanel topPanel = panelStack.Peek();
            topPanel.OnPause();
        }

        // 2. 获取或实例化新界面
        BasePanel panel = GetPanel(panelName);
        panel.OnEnter();

        // 3. 压入栈顶
        panelStack.Push(panel);
        return panel;
    }

    // 出栈并关闭当前 UI
    public void PopPanel()
    {
        if (panelStack.Count <= 0) return;

        // 1. 弹出栈顶界面并关闭
        BasePanel topPanel = panelStack.Pop();
        topPanel.OnExit();

        // 2. 恢复上一个界面
        if (panelStack.Count > 0)
        {
            BasePanel previousPanel = panelStack.Peek();
            previousPanel.OnResume();
        }
    }

    private BasePanel GetPanel(string panelName)
    {
        if (panelDict.TryGetValue(panelName, out BasePanel panel))
            return panel;

        // 若未缓存，从 Resources (或 Addressables) 加载
        GameObject prefab = Resources.Load<GameObject>($"UI/{panelName}");
        GameObject go = Instantiate(prefab, popUpLayer);
        panel = go.GetComponent<BasePanel>();
        panelDict.Add(panelName, panel);
        return panel;
    }
}
```

### 1.2 事件中心（EventCenter）观察者模式实现

#### 💡 架构设计原理

全局解耦的核心工具。发送者只管“广播事件”，接收者只管“监听事件”，两者**零直接代码引用**，极大降低代码耦合度。

#### 💻 核心 C# 实现示例

C#

```
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EventCenter
{
    // 使用 Action 字典维护事件名与监听回调的映射
    private static Dictionary<string, Delegate> eventTable = new Dictionary<string, Delegate>();

    // 添加监听器 (无参)
    public static void AddListener(string eventName, Action handler)
    {
        OnListenerAdding(eventName, handler);
        eventTable[eventName] = (Action)eventTable[eventName] + handler;
    }

    // 添加监听器 (带参)
    public static void AddListener<T>(string eventName, Action<T> handler)
    {
        OnListenerAdding(eventName, handler);
        eventTable[eventName] = (Action<T>)eventTable[eventName] + handler;
    }

    // 移除监听器
    public static void RemoveListener(string eventName, Action handler)
    {
        if (eventTable.TryGetValue(eventName, out Delegate d))
        {
            Action currentHandler = (Action)d - handler;
            if (currentHandler == null) eventTable.Remove(eventName);
            else eventTable[eventName] = currentHandler;
        }
    }

    // 广播事件 (无参)
    public static void Broadcast(string eventName)
    {
        if (eventTable.TryGetValue(eventName, out Delegate d))
        {
            (d as Action)?.Invoke();
        }
    }

    // 广播事件 (带参)
    public static void Broadcast<T>(string eventName, T arg)
    {
        if (eventTable.TryGetValue(eventName, out Delegate d))
        {
            (d as Action<T>)?.Invoke(arg);
        }
    }

    private static void OnListenerAdding(string eventName, Delegate handler)
    {
        if (!eventTable.ContainsKey(eventName))
            eventTable.Add(eventName, null);
    }
}
```

### 1.3 公共 Mono 驱动模块设计

#### 💡 架构设计原理

在 Unity 开发中，非 `MonoBehaviour` 的普通 C# 类（如网络管理器、数据调度中心、纯 C# 业务逻辑类）**无法直接使用 `StartCoroutine` 协程，也无法监听 `Update` 帧更新**。

- **解决方案**：创建一个全局唯一的 `MonoManager` 单例，暴露 `Update` 事件接口与协程代理接口，让所有纯 C# 类可以“借用”它的生命周期。

C#

```
public class MonoManager : MonoBehaviour
{
    private static MonoManager instance;
    public static MonoManager Instance => instance;

    private event Action onUpdateEvent;

    private void Awake()
    {
        instance = this;
        DontDestroyOnLoad(gameObject); // 切场景不销毁
    }

    private void Update()
    {
        onUpdateEvent?.Invoke();
    }

    // 暴露给普通 C# 类的 Update 监听接口
    public void AddUpdateListener(Action action) => onUpdateEvent += action;
    public void RemoveUpdateListener(Action action) => onUpdateEvent -= action;
}
```

## 2. 热更新与网络

### 2.1 AssetBundle (AB包) 打包与加载流程

#### 💡 概念与机制

AssetBundle 是 Unity 原生用于**动态更新资源**的压缩包文件。包含 Mesh、Prefab、AudioClip、Material 等资源，不包含 C# 源代码（源代码通过热更脚本/代码热更处理）。

```
[ 资源文件 (Prefab/Texture) ] ──> 设置 AssetBundle Name ──> [ BuildPipeline.BuildAssetBundles ]
                                                                     │
                                                                     ▼
[ 运行时加载 ] ◄── LoadFromFile / UnityWebRequest ◄── [ 写入 StreamingAssets 或下载到 PersistentDataPath ]
```

#### 💻 核心 API 与打包加载示例

C#

```
// 1. 编辑器打包代码 (必须放在 Editor 目录下)
#if UNITY_EDITOR
using UnityEditor;
public class ABBuilder
{
    [MenuItem("Tools/Build AssetBundles")]
    public static void BuildAllAB()
    {
        string targetPath = Application.streamingAssetsPath;
        // 为当前平台打包 AB (如 Android/iOS/StandaloneWindows64)
        BuildPipeline.BuildAssetBundles(targetPath, BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows64);
    }
}
#endif

// 2. 运行时加载 AB 包代码
public class ABLoader : MonoBehaviour
{
    public IEnumerator LoadABExample()
    {
        string path = System.IO.Path.Combine(Application.streamingAssetsPath, "monsters.ab");

        // 异步从本地文件加载 AB 包
        AssetBundleCreateRequest bundleReq = AssetBundle.LoadFromFileAsync(path);
        yield return bundleReq;

        AssetBundle bundle = bundleReq.assetBundle;
        if (bundle == null) yield break;

        // 异步从 AB 包中加载资源 Prefab
        AssetBundleRequest assetReq = bundle.LoadAssetAsync<GameObject>("DragonPrefab");
        yield return assetReq;

        GameObject prefab = assetReq.asset as GameObject;
        Instantiate(prefab);

        // 卸载 AB 包头信息 (false 代表保留已实例化出来的对象；true 代表连同已例化的对象一并销毁)
        bundle.Unload(false);
    }
}
```

### 2.2 Lua / 代码热更新基础概念 (xLua / HybridCLR)

#### 💡 为什么需要热更新？

传统 C# 代码在打包为移动端原生应用（如 `.apk` 或 `.ipa`）后，代码会被编译成 IL / 机器码。**App Store 或安卓应用商店不允许直接替换二进制可执行代码**。一旦代码有 Bug，必须重新提交商店审核。热更新允许游戏在启动时从服务器下载最新的“代码文件”并在运行时直接解释执行。

#### 📌 主流热更技术方案对比

```
  ┌───────────────────────────┐         ┌───────────────────────────┐
  │   Lua 方案 (xLua / ToLua)  │         │  C# 原生方案 (HybridCLR)  │
  ├───────────────────────────┤         ├───────────────────────────┤
  │ 逻辑写在 .lua 脚本中        │         │ 逻辑全用标准 C# 代码编写     │
  │ 基于 C# 虚拟机的 C# 与 Lua  │         │ 彻底颠覆 Lua 方案！         │
  │ 双向反射/粘合代码交互      │         │ 实现了 IL2CPP 底层 C# 代码  │
  │ 开发模式割裂，学习成本高    │         │ 的原生地热更新（零语言割裂） │
  └───────────────────────────┘         └───────────────────────────┘
```

1. **Lua 热更方案 (xLua)**：
   - 将业务逻辑（UI、战斗逻辑）使用 Lua 语言编写。
   - C# 底层通过 LuaState 虚拟机驱动执行 Lua 脚本；使用 `[LuaCallCSharp]` 特性打出 C# 与 Lua 的交互桥接代码。
2. **HybridCLR (俗称“HUABAN / 华班”)**：
   - 现代 Unity 商业项目首选的 C# 代码热更方案。彻底解决了传统 IL2CPP 无法动态加载 C# Assembly 的限制，**让开发者直接用原生 C# 写热更新逻辑**。

### 2.3 TCP / UDP Socket 通信原理与网络消息协议

在实时联机游戏（如 MOBA、FPS、MMO）中，网络通信是整个架构的核心底层。

#### 1. TCP 与 UDP 的本质区别与选择

| **协议** | **连接特性**                 | **可靠性与顺序**                            | **延迟开销**                       | **适用游戏场景**                                  |
| -------- | ---------------------------- | ------------------------------------------- | ---------------------------------- | ------------------------------------------------- |
| **TCP**  | 面向连接 (三次握手/四次挥手) | **100% 可靠**，按序到达；内部重传与拥塞控制 | 相对较高 (存在粘包/半包与队头阻塞) | 棋牌、回合制、MMORPG 聊天与背包、HTTP 注册登录    |
| **UDP**  | 无连接 (只管往外发)          | 不保证可靠性与顺序，可能丢包                | **极低**                           | 帧同步 RTS、动作格斗、FPS (配合上层 KCP 重传协议) |

#### 2. TCP 粘包 / 半包原理与分包协议设计

- **问题产生**：TCP 是基于**字节流 (Stream)** 传输的，没有边界的概念。如果客户端连续发送两个较小的消息，TCP 会将它们合并为一个数据包发送（粘包）；或者如果消息太大，会被拆成多次发送（半包）。
- **解决方案：自定义消息协议包头 (Header + Body)**。

```
                    【应用层 TCP 数据包结构】
┌───────────────────────────┬───────────────────────────┐
│     Header (包头)         │       Body (包体)         │
├─────────────┬─────────────┼───────────────────────────┤
│ Length (4B) │ MsgID (4B)  │ Protobuf / JSON 字节序列  │
└─────────────┴─────────────┴───────────────────────────┘
 ◄── 告诉接收方 body 长度 ──►
```

#### 💻 C# Socket 消息分包处理逻辑示例

C#

```
using System;
using System.IO;
using System.Net.Sockets;

public class NetworkClient
{
    private Socket socket;
    private byte[] receiveBuffer = new byte[4096];
    private MemoryStream readStream = new MemoryStream();

    // 收到原始 TCP 字节流的回调
    private void OnReceiveData(int count)
    {
        readStream.Write(receiveBuffer, 0, count);
        readStream.Position = 0; // 重置指针准备读取

        // 循环解析包头 (假设 包头长度 Length 为 int 占用 4 字节)
        while (readStream.Length - readStream.Position >= 4)
        {
            BinaryReader reader = new BinaryReader(readStream);
            int bodyLength = reader.ReadInt32(); // 读取 Body 字节长度

            // 判断剩余可读字节是否满足一个完整的 Body 长度
            if (readStream.Length - readStream.Position >= bodyLength)
            {
                byte[] bodyBytes = reader.ReadBytes(bodyLength);
                // 成功提取出一个完整的消息包，丢给上层解析！
                ProcessMessageBody(bodyBytes);
            }
            else
            {
                // 数据不够一个完整的包（发生了半包），撤回位置指针，等待下次继续接收！
                readStream.Position -= 4;
                break;
            }
        }

        // 清理已解析完毕的缓存字节
        TrimReadStream();
    }

    private void ProcessMessageBody(byte[] body) { /* Protobuf 反序列化 */ }
    private void TrimReadStream() { /* 清理 MemoryStream */ }
}
```