# Unity游戏底层开发的知识点



# 模块一：C#/C++语言与内存基础

---

# 1.1 内存与 CPU 缓存机制

### 1. [ ] 堆 (Heap) 与 栈 (Stack)

#### 内存分配机制与生命周期

* **栈 (Stack)：**

* **机制：** 由 CPU 的栈指针（ESP/RSP 寄存器）直接管理，采用 **LIFO（后进先出）** 顺序。分配内存仅需移动栈指针（一个减法指令）。

* **生命周期：** **严格随作用域（Scope）绑定**。当函数返回或大括号 `}` 结束时，栈指针直接弹回，内存瞬间自动释放。

* **特点：** 空间较小（一般几 MB），但速度极快。

  


* **堆 (Heap)：**
* **机制：** 是一块巨大的全局共享内存区域（在 C# 中由托管堆管理，C++ 中由 OS/CRT 管理）。分配时需要通过内存管理器寻找足够大的空闲块（包含复杂的寻找算法如 Best-Fit / First-Fit）。
* **生命周期：** **手动控制（C++）或由垃圾回收器控制（C# GC）**。只要没有显式释放或引用断开，它就会一直存在，脱离了函数作用域的限制。
* **特点：** 空间极大，但分配与释放开销高。



#### 分配与释放的 CPU 开销差异

| 维度           | 栈 (Stack)                      | 堆 (Heap)                                        |
| -------------- | ------------------------------- | ------------------------------------------------ |
| **分配指令**   | 仅需 1~2 条汇编指令（移动指针） | 触发内存管理器查找、碎片合并、锁同步（多线程下） |
| **释放开销**   | 0（随着函数退出直接覆盖）       | 需调用 `free()` / `delete` 或触发 GC 标记与清除  |
| **时间复杂度** | $O(1)$ 恒定极速                 | $O(1)$ ~ $O(N)$ 波动较大                         |

> **💡 进阶拓展：线程栈与 Stack Overflow**
> 每个线程都有自己独立的栈，而所有线程共享同一个堆。如果写了死循环递归或在栈上分配了极大的局部结构体数组（例如 C# 的 `stackalloc` 或 C++ 局部大数组），就会导致**栈溢出（Stack Overflow）**。

---

### 2. [ ] CPU 缓存局部性 (Cache Line & Data Locality)

#### 为什么连续内存比分散内存快几十倍？

现代 CPU 的运算速度（GHz）远飞于内存（DRAM）的读取速度。为了消除这个“内存墙”，CPU 内置了 L1, L2, L3 三级缓存。

```
[ CPU Core ] <---> [ L1 Cache ] <---> [ L2 Cache ] <---> [ L3 Cache ] <---> [ Main Memory (RAM) ]
速度:             ~1 ns               ~4 ns               ~10 ns             ~100 ns

```

1. **Cache Line（缓存行）：** CPU 从内存读取数据时，**绝不会只读 1 个字节**，而是一次性读取一整块连续内存（通常是 **64 字节**），这叫一个 Cache Line。
2. **空间局部性 (Spatial Locality)：** 当程序访问内存地址 $A$ 时，很大可能会紧接着访问 $A+1, A+2$。
* **连续内存（如 `List<Struct>` 或 `int[]`）：** 访问第一个元素时，整个 64 字节的 Cache Line 都被加载到了 CPU 缓存中。读取后续元素时触发 **Cache Hit（缓存命中）**，仅需 ~1ns。
* **分散内存（如 `LinkedList<Node>` 或指针链）：** 节点在堆中随机分布。访问下一个节点时，CPU 发现缓存里没有（**Cache Miss**），必须暂停 CPU，等待从 RAM 中重新加载 64 字节，造成几十个甚至上百个 CPU 周期（Stall）的浪费。



```
连续内存 (Array/Vector):
[ Item 0 | Item 1 | Item 2 | Item 3 ] ---> 一次性加载进 64B Cache Line (Cache Hit!)

分散内存 (LinkedList/Pointer Chasing):
[ Node 0 ] ----> 寻址指针 ----> [ Node 1 (物理地址极远) ] (Cache Miss! 强行等待 RAM)

```

> **💡 进阶拓展：Data-Oriented Design (DOD / ECS)**
> Unity 的 **DOTS (Data-Oriented Tech Stack)** 和 **Entities** 包，核心逻辑就是弃用传统的 OOP（对象分散在堆中），将所有数据组织为连续的 Component 数组（SOA - Structure of Arrays），使 CPU 的 Cache Hit 率接近 100%。

---

### 3. [ ] 内存碎片 (Memory Fragmentation)

#### 为什么频繁 new/delete 或 GC 会导致内存碎片？

内存碎片分为两种：

1. **内部碎片：** 由于内存对齐等原因，分配给你的块比你实际需要的大。
2. **外部碎片：** 在堆上频繁分配和释放不同大小的对象（如 `new Enemy()`，不久后死亡 `delete`）。随着时间推移，空闲内存被切割成大量**不连续的小微孔洞**。此时即使总剩余内存有 100MB，但如果你要分配一个连续的 10MB 数组，依然会触发 **OOM（Out of Memory）**。

#### 如何通过内存池 / 对象池解决？

* **对象池 (Object Pool)：**
* 用于**生命周期短、频繁生成**的对象（如子弹、特效、伤害数字）。
* **原理：** 提前创建一批对象存入队列/数组。需要时从池中出列（`SetActive(true)`），销毁时归还池中（`SetActive(false)`），**彻底避免 runtime 的 `new` 与 `Destroy*`*。


* **内存池 (Memory Pool / Chunk Allocator)：**
* 底层直接向操作系统申请一大块连续内存（如 16MB Chunk）。
* 将这块内存切割成固定大小（如 32B, 64B, 128B）的 Slot。分配时直接返回 Slot 指针，释放时仅做标记，归还给内存池，**绝不交还给操作系统/C++默认分配器**，从根源消除外部碎片。



---

### 4. [ ] 内存对齐 (Memory Alignment)

#### 结构体成员顺序对内存占用和 CPU 读取效率的影响

CPU 访问内存时，按照对齐边界（通常是 4 或 8 字节）按块读取。如果变量的地址没有按照它的字节大小对齐，CPU 可能需要发起 **两次** 内存读取并进行位移拼接（甚至触发硬件级的 Alignment Fault 异常）。

#### 示例分析：C# / C++ 中的结构体重排

假设我们有一个结构体：

```csharp
struct BadStruct {
    byte a;   // 1 byte
    int b;    // 4 bytes
    byte c;   // 1 byte
}

```

* **内存布局（默认对齐下，最大成员对齐数为 4）：**
* `a` (1 byte) + 【3 bytes 填充补齐 Padding】
* `b` (4 bytes)
* `c` (1 byte) + 【3 bytes 填充补齐 Padding】
* **总大小：12 字节！**（实际有用数据仅 6 字节，浪费了一半）



优化调整声明顺序（将占用字节大的字段放在前面）：

```csharp
struct GoodStruct {
    int b;    // 4 bytes
    byte a;   // 1 byte
    byte c;   // 1 byte
              // 【2 bytes 填充补齐 Padding】
}

```

* **优化后总大小：8 字节！** 节省了 33% 的内存空间。

> **💡 进阶拓展：C# 的 `[StructLayout]`**
> 在 C# 中，你可以显式声明 `[StructLayout(LayoutKind.Sequential)]` 控制顺序，或使用 `[StructLayout(LayoutKind.Explicit)]` 搭配 `[FieldOffset(0)]` 手动指定每个字段的字节偏移量，甚至能做到 C/C++ 风格的 **Union（联合体）** 结构。

---

# 1.2 C# 核心机制（Unity 重点）

### 1. [ ] 值类型与引用类型

#### 深拷贝与浅拷贝的区别

* **值类型 (Value Types)：** `struct`, `int`, `float`, `enum`, `Vector3` 等。直接存储值本身，分配在栈或宿主对象内。
* **赋值：** 永远是**深拷贝（值传递）**。`a = b` 会在内存中完整复制一份数据，修改 `a` 不会影响 `b`。


* **引用类型 (Reference Types)：** `class`, `interface`, `delegate`, `string`, `Array` 等。分配在托管堆上，变量本身只保存一个指向堆内存地址的指针（4 或 8 字节）。
* **赋值：** 默认是**浅拷贝（引用传递）**。`a = b` 只是让 `a` 指向了 `b` 的堆地址，修改 `a` 的属性会直接改变 `b`。



#### struct 与 class 的适用场景（如 Vector3 为什么是 struct）

* **为什么 Vector3 是 struct？**
1. **极高频的创建与销毁：** 游戏逻辑中每帧可能会产生成千上万个向量计算。如果 `Vector3` 是 `class`，这千万个对象会在堆上产生巨大的内存碎片，并引发频繁的 GC Frame Drop（卡顿）。
2. **小尺寸数据：** `Vector3` 仅包含 3 个 `float`，共 12 字节。直接在栈上传递或作为组件字段连续存放（Data Locality），性能远超指针寻址。


* **选型准则：**
* 数据量小（通常 < 16 字节）、生命周期短、不可变（Immutable）或纯数值载体 $\rightarrow$ **Struct**。
* 需要继承/多态、数据量大、生命周期长、共享状态 ---> **Class**。



---

### 2. [ ] 装箱 (Boxing) 与 拆箱 (Unboxing)

#### 隐式装箱的堆内存开销与优化

* **装箱：** 将**值类型**转换为**引用类型**（例如 `object`, `ValueType` 或接口）。
* **底层发生了什么？**
1. 在托管堆申请一块内存（包含对象头 Object Header 和类型对象指针 TypeHandle）。
2. 将栈上的值类型数据完整拷贝到堆上。
3. 返回堆地址的引用。




* **拆箱：** 将引用类型转回值类型。需要检查类型安全并进行数据拷贝。

#### 常见隐式装箱大坑与优化：

```csharp
// 1. 字符串拼接 / 格式化
int hp = 100;
Debug.Log("HP: " + hp); // 隐式调用 string.Concat(object, object)，hp 被装箱！
// 优化：
Debug.Log($"HP: {hp.ToString()}"); // ToString() 避免装箱

// 2. 使用非泛型集合 / 接口传递
ArrayList list = new ArrayList();
list.Add(10); // 装箱！
// 优化：使用泛型集合
List<int> list = new List<int>(); // 零装箱

// 3. 值类型实现接口后，隐式转换为接口
interface IDamageable { void TakeDamage(); }
struct Player : IDamageable { public void TakeDamage() {} }

Player p = new Player();
IDamageable d = p; // 装箱！因为接口是引用类型！

```

---

### 3. [ ] 垃圾回收 (GC - Generational GC)

#### 分代 GC 的工作原理（0 代、1 代、2 代）

.NET / C# 默认使用**分代 GC（Generational GC）**，基于假设：**越新创建的对象，生命周期越短；越老的对象，存活越久。**

```
堆内存分布:
[ Generation 0 (频繁回收) ] ---> [ Generation 1 (缓冲区) ] ---> [ Generation 2 (长寿/大对象) ]

```

* **Gen 0（0 代）：** 新创建的小对象（$< 85,000$ 字节）。GC 最频繁触发，速度极快（只扫描极小区域）。
* **Gen 1（1 代）：** 存活过一次 Gen 0 回收的对象，作为 0 代与 2 代之间的缓冲区。
* **Gen 2（2 代）：** 存活过 Gen 1 回收的对象，以及大对象堆（LOH - Large Object Heap，$\ge 85KB$）。GC 很少触发，一旦触发会扫描全堆（Full GC），引发严重卡顿！

> **⚠️ Unity 的 Mono GC 与 SGen/Boehm GC：**
> 注意！标准的 Unity Mono 使用的是 **Boehm-Demers-Weiser GC**（非分代、非压缩式），这意味着 Unity 传统的 GC **不会做内存压缩合并**，极易产生内存碎片！而在较新版本的 Unity 中推出了 **Incremental GC（增量式 GC）**。

#### Incremental GC（增量式 GC）如何降低卡顿？

传统的 GC 是 **Stop-the-World (STW)**：一旦触发，主线程暂停，GC 一口气干完所有标记清除工作，造成画面单帧突发卡顿（如 50ms）。

**Incremental GC** 利用 Write Barrier 技术，将原本需要一次性执行的 GC 检查划分成多份，**分散到未来的几十帧中**（每帧只占用 1~2ms），从而将突发的“大卡顿”平滑化为不可察觉的微小开销。

---

### 4. [ ] 字符串优化

#### `string` 的不可变性与常量池

C# 的 `string` 是**引用类型**，且具有**不可变性 (Immutability)**。

* 一旦创建，字符串在堆上的内容不可修改。
* 每次执行 `str += "a"`，实际上是在堆上 **`new` 了一个全新的 `string` 对象**，旧的字符串沦为垃圾等待 GC！

#### 各种字符串优化技术手段

1. **StringBuilder：** 内部持有可变的 `char[]` 缓冲区。适合做**复杂的多段字符串拼接**。
2. **字符串常量池 (String Interning)：** 相同的字面量字符串在内存中只有一份副本。
3. **`ReadOnlySpan<char>` / `Memory<T>`（零分配解析）：**
* 传统字符串切割 `str.Split(',')` 会产生一个 `string[]` 以及大量子字符串对象，GC 灾难！
* 使用 `ReadOnlySpan<char>` 可以在**零堆内存分配**的情况下，直接以指针/视图的形式安全裁切、解析字符串（如解析 JSON / CSV / 配置文件）。



---

### 5. [ ] 委托、事件与 Lambda 表达式

#### 闭包 (Closure) 产生的隐式 GC 垃圾

当 Lambda 表达式捕获了**外部局部变量**时，编译器会在后台自动生成一个隐式的**委托类 (Closure Class)**！

```csharp
// 示例：
void InitButton() {
    int score = 100; // 局部变量
    // Lambda 捕获了外部的 score 变量！
    myButton.onClick.AddListener(() => {
        Debug.Log("Score: " + score);
    });
}

```

* **底层干了什么？** 编译器在后台 `new` 了一个带有 `public int score` 字段的密封类对象。**每次调用该函数，都会在堆上 `new` 一个闭包对象，产生 GC 内存！**
* **优化：** 尽可能使用静态方法，或者避免在 Lambda 中引用外层的局部变量。如果需要传参，通过显式参数传入。

#### 事件监听未取消导致的内存泄漏 (Memory Leak)

* **原理：** 当发布者（Publisher）包含一个 `event`，订阅者（Subscriber）通过 `pub.OnEvent += sub.Handle` 注册了回调。
* **隐患：** 订阅者的实例会被发布者的委托链表（Delegate Target）强引用。即使订阅者（比如一个 UI 面板）被 `Destroy()` 了，只要发布者（比如 `PlayerManager` 单例）还存活，**订阅者就永远无法被 GC 回收**，造成严重的内存泄漏！
* **解法：** 必须成对出现！`OnEnable` 时 `+=`，`OnDisable` / `OnDestroy` 时必须 `-=` 移除监听。

---

### 6. [ ] 反射 (Reflection)

#### 为什么反射性能差？

反射允许在运行时动态获取 Type、MethodInfo、FieldInfo，但它的代价极其昂贵：

1. **绕过了编译期优化：** 无法进行内联（Inlining）和类型推导。
2. **大量字符串查找与安全检查：** 需要遍历元数据表格匹配名称。
3. **装箱与参数数组创建：** 调用 `MethodInfo.Invoke(obj, new object[] { param })` 时，会强行创建 `object[]` 数组，如果参数是值类型还会引发**装箱**。

#### 优化方案：

1. **特性 (Attribute) 索引化：** 在启动时（如游戏初始化阶段）一次性扫描所有 Attribute 并存入字典（Cache），后续直接查表，禁止在 Update 中反射。
2. **泛型缓存 (Generic Cache)：** 利用 `StaticClass<T>` 的特性，为每个类型自动生成静态缓存。
3. **表达式树 (Expression Trees) / Delegate.CreateDelegate：** 在运行时用 Expression 生成 Lambda 表达式，并编译成强类型委托（C# Delegate），性能可达到原生调用 90% 以上的速度！

---

# 1.3 C++ 核心机制（UE / GDExtension 重点）

### 1. [ ] 指针与引用

#### 指针算术、野指针与悬空指针

* **指针算术：** `ptr + 1` 增长的字节数取决于类型的大小（如 `int`* 加 1 实际增加 4 字节）。这是连续数组遍历的基石。
* **野指针 (Wild Pointer)：** 未被初始化的指针，指向任意随机内存地址，读取会导致未定义行为 (UB) 或崩溃。
* **悬空指针 (Dangling Pointer)：** 指向的内存已被 `free` / `delete` 释放，但指针变量自身没有置为 `nullptr`。

#### `const` 关键字的严格语义

在 C++ 中，`const` 的位置决定了限制的对象（**“左定值，右定指”**）：

* `const int* p` 或 `int const* p`：**常量指针**。指向的内容不能改，指针自身的指向可以改。
* `int* const p`：**指针常量**。指针本身的指向不可改，但指向的内容可以改。
* `const int* const p`：指向和内容都**不可修改**。
* **成员函数后的 `const`：** `void Draw() const;` 承诺该函数绝不会修改当前类的任何成员变量（除非该变量被标记为 `mutable`）。

---

### 2. [ ] 面向对象底层实现（虚函数表 VTable）

#### 虚函数表 (VTable) 与虚表指针 (VPTR) 的内存布局

当一个 C++ 类包含至少一个 `virtual` 函数时：

1. 编译器会在该类的**内存首部（通常是前 4/8 字节）**隐式插入一个**虚表指针 (VPTR)**。
2. VPTR 指向一个全局只读的**虚函数表 (VTable)**，表里存放着该类所有虚函数的实际函数地址指针。

```
对象内存布局 (Object in Memory):
[ VPTR (8 Bytes) ] -------> VTable (只读数据段):
[ Member Variable 1 ]       [0]: &Base::Func1
[ Member Variable 2 ]       [1]: &Derived::Func2 (被重写覆盖)

```

#### 多态开销与性能损耗

1. **内存开销：** 每个对象额外多占 8 字节（VPTR），虚表本身占少量内存。
2. **两次间接寻址（Cache Miss 风险）：** 必须先通过对象找到 VPTR，再通过 VPTR 找到 VTable，最后取出函数地址跳转。
3. **彻底破坏内联 (Inlining)：** 编译器无法在编译期确定到底调用哪个函数，因此**虚函数默认无法内联**，失去了编译期优化的巨大红利。

---

### 3. [ ] RAII 机制与智能指针

#### RAII (Resource Acquisition Is Initialization)

C++ 的灵魂机制：**资源的获取即初始化，资源的释放即析构。**
将资源（内存、文件句柄、锁）封装在类中，利用**栈上对象退出作用域时必然会自动调用析构函数**的语言特性，确保资源绝对不会泄漏。

#### 三大智能指针 (`std::`)

1. **`std::unique_ptr<T>`：独占式智能指针**
* 零开销（Size 与裸指针完全相同）。
* **禁止拷贝**（拷贝构造函数已被 `= delete`），仅能通过 `std::move` 转移所有权。


2. **`std::shared_ptr<T>`：共享式智能指针**
* 内部使用**引用计数 (Reference Count)**。
* **性能开销：** 占用 2 个指针空间（一个指向对象，一个指向控制块 Control Block）；引用计数的加减必须是**原子操作 (Atomic operation)** 以保证线程安全，这在多线程下会导致 CPU 缓存锁竞争（Bus Lock/CAS）。


3. **`std::weak_ptr<T>`：弱引用智能指针**
* 不增加对象的强引用计数，仅观测 `shared_ptr`。
* **解决循环引用 (Circular Reference)：** A 持有 B 的 `shared_ptr`，B 也持有 A 的 `shared_ptr`，导致引用计数永远无法清零。将其中一方改为 `weak_ptr` 即可完美解决。



---

### 4. [ ] 移动语义与右值引用

#### 彻底消除大对象的不必要拷贝

在 C++11 之前，当函数返回一个大对象（如 `std::vector<int>` 包含 100 万个元素）时，会触发深拷贝：开辟新内存 $\rightarrow$ 逐个元素复制 $\rightarrow$ 销毁临时旧对象。

#### 左值 (Lvalue) 与 右值 (Rvalue)

* **左值：** 有名字、有固定内存地址、可以取地址的变量（如 `int a`）。
* **右值：** 临时的、没有名字的、用完即销的中间值（如 `10`, `a + b`, 函数返回的临时对象）。语法格式为 `T&&`。

#### `std::move` 与 `std::forward` (完美转发)

* **`std::move(x)`：** 并不移动任何数据！它只是一个**无条件的类型转换**，将一个左值强制转换为右值类型，从而触发该类的**移动构造函数 (Move Constructor)**。
* *移动构造函数的做法：* 直接偷走（Steal）临时对象的内部指针（例如让自己的指针指向对方的内存，然后把对方的指针设为 `nullptr`），开销为 $O(1)$！


* **`std::forward<T>(arg)`（完美转发）：** 配合模板万能引用（Universal Reference）使用，保持参数原始的“左值/右值”属性，将参数原封不动地传递给下一层函数。

---

# 💡 学习建议与后续路线

你可以把这套知识体系作为日常刷题、架构设计以及性能调优的 Check List。建议按照以下顺序建立正反馈：

1. **先在 Unity 中验证 C# 机制：** 使用 Profiler 观察 `string` 拼接、装箱、Lambda 闭包带来的内存 GC 峰值。
2. **结合 C++ 加深底层概念：** 写一段简单的 C++ 代码，观察结构体对齐、虚表指针偏移，体验 RAII 智能指针对内存释放的精准掌控。

> **提示：** 本模块（编程语言与底层原理）是基础。接下来还有 **渲染管线 (Render Pipeline)**、**物理/数学逻辑**、**Unity / UE 引擎架构** 以及 **性能优化 (Profiler/Memory Profiler)** 等核心模块。如需对特定模块进一步深入讨论，可随时提出！





# 模块二：游戏引擎核心机制与架构

# 2.1 引擎生命周期与主循环 (Main Loop)

### 1. [ ] 生命周期完整顺序

Unity 的生命周期本质上是一个**巨大的主循环（Main Loop）**，按照极其严格的顺序在每一帧依次调用各个脚本的方法。

```
[初始化阶段]
Awake ➔ OnEnable ➔ (Reset - 仅限编辑器) ➔ Start

[游戏逻辑 & 物理阶段 - 可能循环 0 次或多次]
FixedUpdate ➔ 物理引擎模拟 (Physics Simulation) ➔ Yield WaitForFixedUpdate ➔ OnTrigger/OnCollision

[输入 & 逻辑更新阶段]
Update ➔ 协程 yield null ➔ LateUpdate

[渲染阶段]
OnPreRender ➔ OnRenderObject ➔ OnPostRender ➔ OnGUI

[清理 & 销毁阶段]
OnDisable ➔ OnDestroy ➔ OnApplicationQuit
```

#### 关键节点剖析与避坑：

- **`Awake` vs `Start`：**
  - `Awake` 在脚本实例被加载时**立即**执行（即使 `enabled = false`），适合做**自身组件初始化**（如 `GetComponent<Rigidbody>()`）。
  - `Start` 在脚本被激活（`enabled = true`）且在第一帧 `Update` 调用前执行，适合做**跨脚本的依赖引用**（如查找其他 GameObject）。
  - ⚠️ **避坑：** 绝不要在 `Awake` 里去调用另一个未确立加载顺序的脚本的 `Awake` 数据，否则极易引发 `NullReferenceException`！
- **`OnEnable` / `OnDisable`：**
  - 对象池（Object Pool）复用组件时，这两个方法会被频繁触发。初始化数据重置逻辑必须写在 `OnEnable` 中，而非 `Start`。

### 2. [ ] 渲染帧 (Update) vs 固定物理帧 (FixedUpdate)

#### 变帧率与固定时间步长 (Fixed Timestep) 的本质区别

- **`Update`（渲染帧）：**
  - **受 GPU/CPU 渲染负载影响，时间步长不固定**（Delta Time 实时波动，如 60FPS 时约 16.6ms，30FPS 时约 33.3ms）。
  - 适合处理：**用户输入检测（Input）**、UI 更新、无物理属性的逻辑。
- **`FixedUpdate`（固定物理帧）：**
  - **时间步长绝对固定**（默认为 `Time.fixedDeltaTime = 0.02s`，即每秒固定执行 50 次）。
  - **与渲染帧脱钩：** 一帧渲染帧内，`FixedUpdate` **可能执行 0 次、1 次或多次**。
    - 若游戏卡顿（渲染帧长达 50ms），物理引擎为了追赶时间，会在单帧内连续触发 2~3 次 `FixedUpdate`；
    - 若渲染帧极快（200FPS，每帧 5ms），可能经过 3~4 个渲染帧才触发 1 次 `FixedUpdate`。

#### 插值 (Interpolation) 与 物理平滑

```
物理帧(50Hz): |------------|------------|------------|
渲染帧(144Hz):|--|--|--|--|--|--|--|--|--|--|--|--|--|
              ^
        物理位置在此更新，但渲染帧已绘制多次 -> 导致画面呈现“微小抖动/卡顿”
```

- **抖动问题根源：** 刚体（Rigidbody）在 `FixedUpdate` 中改变位置，但显示器的渲染是在 `Update` 之后。如果渲染帧率 (144Hz) 高于物理帧率 (50Hz)，很多渲染帧画出的刚体位置是没有发生改变的，视觉上就会产生画面切尺、抖动（Jitter）。
- **解决方案：** 开启 Rigidbody 的 **`Interpolation`（插值）** 属性。
  - **Interpolation（内插）：** 根据上一帧和当前帧的物理位置，根据渲染时间在两者之间做线性插值。平滑度极高，但有 1 帧物理延迟。
  - **Extrapolation（外推）：** 根据当前速度预判下一帧位置。适合高速移动物体，但预测失败时会发生位置跳变。

### 3. [ ] 摄像机更新逻辑：为什么摄像机跟随必须放 LateUpdate？

这是一个最常见的“画面抖动”Bug 来源！

#### 错误做法：

把角色移动逻辑放在 `Update`（或 `FixedUpdate`），把摄像机跟随逻辑也放在 `Update`。

#### 原因分析：

Unity 中脚本的 `Update` 执行顺序是默认**随机/未定义**的。

1. **情况 A：** 角色 `Update` 优先执行 ---> 角色移动新位置 ---> 摄像机 `Update` 执行 ---> 摄像机移到新位置。**(画面正常)**
2. **情况 B：** 摄像机 `Update` 优先执行 ---> 摄像机读取到了角色**上一帧**的旧位置 ---> 角色 `Update` 执行 ---> 角色移动到新位置。**(画面滞后 1 帧)**

由于渲染顺序不固定，情况 A 与 B 会交替出现，造成摄像机呈现严重的频繁拉扯与抖动！

#### 正确做法：

- **角色移动：** 放在 `Update`（非物理）或 `FixedUpdate`（物理刚体）。
- **摄像机跟随：** 必须放在 **`LateUpdate`**。因为 `LateUpdate` **保证在全场景所有 `Update` 执行完毕后才会被调用**，此时角色的最终位置已经完全确定，摄像机跟随绝对不会拿到上一帧的旧数据。

# 2.2 物理系统 (Physics 2D / 3D)

### 1. [ ] 碰撞三要素：Collision 与 Trigger 的发生条件

要让两个物体在 Unity 中触发物理回调，必须满足极其严格的组件组合条件：

#### 触发回调的必要条件矩阵：

| **场景**               | **物体 A**                                       | **物体 B**                  | **触发函数**                         |
| ---------------------- | ------------------------------------------------ | --------------------------- | ------------------------------------ |
| **硬碰撞 (Collision)** | Collider + **Rigidbody** (非 Kinematic 或运动中) | Collider                    | `OnCollisionEnter` / `Stay` / `Exit` |
| **触发器 (Trigger)**   | Collider (**IsTrigger = true**) + **Rigidbody**  | Collider (无论是否 Trigger) | `OnTriggerEnter` / `Stay` / `Exit`   |

#### 🔑 铁律总结（记心中）：

1. **必须至少有一方带有 `Rigidbody`（刚体）！** 如果两个 GameObject 都只有 `Collider`，无论怎么撞，引擎都不会调用任何 `OnCollision` 或 `OnTrigger` 回调。
2. **运动的一方最好带 `Rigidbody`：** 移动不带 Rigidbody 的静态碰撞体（Static Collider）会强制 PhysX 重新计算整个场景的物理树，带来巨大的 CPU 开销！

### 2. [ ] 高速穿模/漏碰撞 (Tunneling)

#### 离散检测 (Discrete) vs 连续检测 (Continuous)

```
离散检测 (Discrete):
帧 N  :  [ 子弹 ]  | 墙体 |
帧 N+1:          | 墙体 |  [ 子弹 ]   <--- 直接穿透！物理引擎未检测到碰撞！

连续检测 (Continuous):
帧 N ➔ N+1 扫掠出几何体: [ 子弹==================== ]
                         | 墙体 |    <--- 检测到相交！触发碰撞！
```

- **离散检测 (Discrete - 默认模式)：**
  - **原理：** 在每个物理帧，仅采样物体的当前位置是否与墙体重叠。
  - **缺点：** 当物体移动速度极快（如子弹、高速赛车），一帧内移动的距离超过了墙体厚度，物体会直接“瞬间移动”到墙的另一侧，发生**穿模（Tunneling）**。
- **连续碰撞检测 (Continuous / Continuous Dynamic)：**
  - **原理：** 采用**扫掠体（Swept Volume / CCD）** 技术，根据速度向量拉伸出一块空间几何体，检测该几何体在运动轨迹上是否穿越了任何碰撞体。
  - **开销：** CPU 资源消耗极大！

#### 各种 Continuous 模式的适用场景：

1. **Discrete：** 普通移动的玩家、NPC、箱子（性能最好）。
2. **Continuous：** 用于**高速移动去撞击静态物体**的对象（例如高速飞向墙壁的炮弹）。
3. **Continuous Dynamic：** 用于**高速移动去撞击其他同样高速移动的动态刚体**的对象（例如两辆高速相撞的赛车）。
4. **Continuous Speculative（推测性 CCD）：** 基于预测点判定，性能优于传统 CCD，且支持旋转物体的连续检测。

### 3. [ ] 射线检测 (Raycasting)

#### LayerMask 按位运算 (Bitwise Operators)

Unity 的层级（Layer）最多 32 个（0~31），底层使用一个 **32 位无符号整数（Int32 / Bitmask）** 来高效表示层级遮罩。

C#

```
// 1. 仅检测 Layer 8 (例如 "Enemy")
int mask = 1 << 8;

// 2. 检测 Layer 8 ("Enemy") 和 Layer 9 ("Obstacle")
int mask = (1 << 8) | (1 << 9);

// 3. 检测除了 Layer 8 之外的所有层 (按位取反 ~)
int mask = ~(1 << 8);

// 在射线检测中使用
if (Physics.Raycast(transform.position, transform.forward, out RaycastHit hit, 100f, mask)) {
    // 击中目标
}
```

#### 射线检测的 CPU 开销与射线缓存 (Raycast Command / NonAlloc)

- **内存垃圾 (GC) 优化：**
  - ❌ `Physics.RaycastAll(...)`：每次调用都会在堆上 `new` 一个 `RaycastHit[]` 数组，产生大量 GC 垃圾！
  - ✅ **`Physics.RaycastNonAlloc(...)`**：传入预先创建好的 `RaycastHit[]` 数组，做**零 GC** 填充检测，极大减轻 GC 压力。
- **PhysX 场景查询开销：**
  - 射线检测需要在 PhysX 的 BVH（Bounding Volume Hierarchy）树中逐级求交。在 `Update` 里盲目大量发射长射线会导致 CPU 显著卡顿。
  - **Job System 异步射线：** 在 Unity DOTS / Job System 中，可以使用 `PhysicsScene.RaycastCommand` 将上千条射线计算**分发到多线程 CPU 中并行计算**。

# 2.3 异步与分帧调度

### 1. [ ] 协程 (Coroutine) 底层

#### IEnumerator 状态机与 yield return 引擎调度流程

C# 的协程**并不是真正的多线程**！它完全运行在**主线程**上，本质是一个由编译器生成的**迭代器状态机 (Iterator State Machine)**。

C#

```
IEnumerator MyCoroutine() {
    Debug.Log("Step 1");
    yield return null; // 暂停，下一帧继续
    Debug.Log("Step 2");
}
```

- **编译器干了什么？** 带有 `yield` 的函数会被 C# 编译器编译为一个实现了 `IEnumerator` 接口的**隐式密封类**。每次执行到 `yield return` 时，代码状态和局部变量会被保存在该类中，并返回 `true`；执行完毕返回 `false`。
- **引擎如何调度？**
  - Unity 的主循环在每帧的特定节点（如 `Update` 之后），会遍历所有激活的协程列表，手动调用其 `.MoveNext()`。
  - 如果 `MoveNext()` 返回 `true`，引擎检查 `Current` 返回的对象：
    - `yield return null` $\rightarrow$ 下一帧 `Update` 之后继续 `.MoveNext()`；
    - `yield return new WaitForSeconds(1.0f)` $\rightarrow$ 引擎将其加入计时器队列，1 秒后继续 `.MoveNext()`；
    - `yield return new WWW()` / `UnityWebRequest` $\rightarrow$ 异步下载完成后继续。

#### ⚠️ 协程的经典大坑：

1. **`new` 对象的 GC 垃圾：** 每次 `yield return new WaitForSeconds(1f)` 都会在堆上 `new` 一个对象。在高频协程中，应该将 `WaitForSeconds` **预先缓存为静态/成员变量**！
2. **生命周期绑定：** 协程依赖于启动它的 `MonoBehaviour`。如果 GameObject 调用了 `SetActive(false)` 或被 `Destroy()`，该对象上的**所有协程会强行终止**。

### 2. [ ] 多线程与 Job System / Task

#### 主线程 (Main Thread) 与 渲染线程 (Render Thread) 的分工

为保证图形上下文的线程安全与高效，现代游戏引擎通常采用双线程架构：

```
[ 主线程 (Main Thread) ]   : 逻辑计算 ➔ C# 脚本 ➔ 物理 ➔ 生成 DrawCalls ➔ 写入 Command Buffer
                                                                        │ (提交绘制指令)
                                                                        ▼
[ 渲染线程 (Render Thread) ]: 读取 Command Buffer ➔ 驱动图形 API (DX12/Vulkan) ➔ 提交 GPU
```

#### 为什么不能在子线程中调用引擎 API？

Unity 的大部分 API（如 `Instantiate`, `Transform.position`, `GetComponent`, `GameObject.Find`）底层都是 C++ 引擎层的映射，且**非线程安全**。

- 如果允许子线程随机修改 `Transform.position`，会导致主线程在进行物理求交或渲染剔除时发生**内存竞争与数据读写冲突（Race Condition）**，引发致命的引擎崩溃！
- Unity 主线程内置了 `ThreadCheck`，任何在非 Main Thread 调用 Unity API 的行为都会直接抛出 `UnityException: Internal_CreateGameObject can only be called from the main thread.`

#### 如何通过线程安全队列回到主线程？

如果子线程（如网络 Socket 接收数据、复杂 Pathfinding）算出了结果，要用来更新 Unity UI 或创建物体：

1. **手写线程安全队列（MainThreadDispatcher）：**

   使用 `ConcurrentQueue<Action>`（并发队列）。子线程将要执行的 Action `Enqueue` 进队列，主线程在 `Update()` 中 `TryDequeue` 出来逐个执行。

2. **使用 async / await 与 `SynchronizationContext`：**

   C# 的 `UniTask` 库或 Unity 默认的 `SynchronizationContext` 可以在 `await` 子线程耗时任务后，**自动切回 Unity 主线程**继续执行后续代码！

3. **Unity C# Job System：**

   Unity 官方推行的多线程高性能框架。允许你在工作线程（Worker Threads）上高效处理海量纯数据计算（使用 `NativeArray` / `NativeList` 等无 GC 的 Struct），计算完成后将结果写回，供主线程读取。

# 💡 综合实践演练（Checklist）

在实际项目中，你可以对照此图检查自己的代码架构：

```
[ 用户输入 (Input) ] ──> Update
                            │
[ 物理动作 (Rigidbodies) ] ─┼──> FixedUpdate (Interpolate 开启)
                            │
[ 动画与位置最终确认 ] ──────┼──> Update
                            │
[ 摄像机镜头跟随 ] ──────────┼──> LateUpdate (彻底消除画面抖动)
                            │
[ 密集型计算(如寻路/AI) ] ───┴──> C# Job System (Worker Thread) ──> 返回主线程应用
```





# 模块三：图形学、渲染管线与性能优化

# 3.1 渲染管线基础 (Rendering Pipeline)

### 1. [ ] 应用阶段 ➔ 几何阶段 ➔ 光栅化阶段

图形渲染管线（Graphics Pipeline）是将 3D 场景中的点、线、面转化为屏幕上 2D 像素阵列的全过程。

```
[ 应用阶段 (CPU) ]
   └── 剔除(Culling) ➔ 准备 Mesh/Texture/Shader ➔ 设置渲染状态 ➔ 发送 DrawCall
                               │ (通过 VBO/EBO/Uniforms 传给 GPU)
                               ▼
[ 几何阶段 (GPU) ]
   └── 顶点着色器(Vertex Shader) ➔ [曲面细分/几何着色器] ➔ 裁剪/NDC投影 ➔ 屏幕映射
                               │ (输出 Clip Space 坐标 & 顶点属性)
                               ▼
[ 光栅化阶段 (GPU) ]
   └── 三角形遍历(光栅化) ➔ 片元着色器(Fragment Shader) ➔ 逐片元测试(Depth/Stencil/Alpha) ➔ 混合(Blending) ➔ FrameBuffer
```

#### 各阶段核心工作拆解：

1. **应用阶段 (CPU 主导)：**

   - **数据准备：** 将网格顶点数据（位置、法线、UV、顶点色）、材质贴图上传至显存（VRAM）。
   - **剔除 (Culling)：** 视锥体剔除、遮挡剔除，避免不必要的绘制提交。
   - **DrawCall 发送：** 设置 GPU 渲染状态（切换 Shader/贴图/混合模式，即 SetPass Call），向 GPU 发送绘制指令 `glDrawElements` 或 `DrawIndexedInstanced`。

2. **几何阶段 (GPU 主导，处理“点”和“面”)：**

   - **顶点着色器 (Vertex Shader)：** **逐顶点**处理。将顶点坐标从**模型空间 (Model Space)** 经过 MVP 矩阵变换到 **裁剪空间 (Clip Space)**：

     $$P_{clip} = M_{projection} \times M_{view} \times M_{model} \times P_{local}$$

   - **透视除法与屏幕映射：** 将坐标除以 w 转为 **NDC 规范化设备坐标** [-1, 1]，再映射到屏幕像素坐标 $(X_{screen}, Y_{screen})$。

3. **光栅化阶段 (GPU 主导，处理“像素/片元”)：**

   - **三角形光栅化 (Triangle Setup/Traversal)：** 确定哪些像素落在了某个三角形内部，并在顶点之间对 UV、法线、顶点色进行**双线性插值**，生成**片元 (Fragment)**。
   - **片元着色器 (Fragment/Pixel Shader)：** **逐片元**计算颜色，采样贴图，计算光照（Lambert, Blinn-Phong, PBR）。
   - **逐片元测试 (Output Merger)：** **模板测试 (Stencil Test)** ---> **深度测试 (Depth Test)** ---> **Alpha 混合 (Alpha Blending)** ---> 最终写入**帧缓冲区 (FrameBuffer)** 显示。

### 2. [ ] 前向渲染 (Forward) vs 延迟渲染 (Deferred)

#### 前向渲染 (Forward Rendering)

- **原理：** 遍历场景中的每一个物体，每个物体分别与其相交的所有光源进行光照计算。
- **计算复杂度：** **$O(M \times N)$**（$M$ 个物体， $N$ 个光源）。
  - 如果有 1000 个物体，10 个光源，片元着色器需要执行上万次光照计算！
- **致命缺点：** 被遮挡的无效像素（Depth Test 失败前）也会白白计算光照，造成严重的不可控开销。多光源下性能急剧下降。
- **优点：** 完美支持透明渲染（Alpha Blending），支持抗锯齿（MSAA），硬件兼容性极佳（适合移动端/VR）。

#### 延迟渲染 (Deferred Rendering)

- **原理：** 将光照计算解耦为 **两步（Pass）**：
  - **Pass 1 (G-Buffer 阶段)：** 不计算任何光照！只把场景中所有不透明物体的几何信息渲染并保存到多张高带宽贴图（**G-Buffer**，通常包含：世界空间法线、漫反射颜色/Albedo、高光/粗糙度/金属度、深度/Z-Buffer）。
  - **Pass 2 (Lighting 阶段)：** 遍历每个光源，利用 G-Buffer 存储的屏幕空间数据，只对**最终呈现在屏幕上的像素**计算一次光照！
- **计算复杂度：** **$O(\text{屏幕像素数} \times N)$**，光照计算与场景几何体复杂度（面数）彻底解耦！
- **缺点：**
  1. **高显存带宽与内存占用：** G-Buffer 需要同时读写多张渲染目标（MRT）。
  2. **天然不支持半透明物体：** 透明物体无法将深度与颜色正确写入 G-Buffer，必须在延迟渲染结束后，补充一个前向渲染 Pass 专门绘制透明物体。
  3. **无法直接使用硬件级 MSAA：** 只能采用 FXAA/TAA 等后处理抗锯齿。

# 3.2 渲染性能三大优化指标

> **性能瓶颈口诀：** CPU 怕 **DrawCall/SetPass**，GPU 怕 **Overdraw/复杂 Shader/高面数**。

### 1. [ ] DrawCall / SetPass Call 优化

#### 定义解析：

- **DrawCall：** CPU 向 GPU 发送“绘制这个 Mesh”的指令（如 `glDrawElements`）。
- **SetPass Call：** 当下一个 Mesh 需要**切换 Shader、材质或全局贴图**时，CPU 必须向 GPU 渲染管线重新提交渲染状态更新。**SetPass Call 的切换开销远高于纯粹的 DrawCall！**

#### 四大批处理方案对比：

| **批处理技术**              | **核心原理**                                                 | **优点**                                                     | **缺点与限制**                                               | **适用场景**                                |
| --------------------------- | ------------------------------------------------------------ | ------------------------------------------------------------ | ------------------------------------------------------------ | ------------------------------------------- |
| 2D 图集 (Sprite Atlas)      | 将多张散图（如 UI/2D 角色）合并到一张大贴图上，使其共享同一材质与贴图 | 彻底消除贴图切换导致的 DrawCall；极大降低 UI/2D 渲染开销     | 图集留白可能浪费显存；需要预先打包管理                       | 2D 游戏、UGUI / GUI 界面、粒子系统贴图      |
| 静态合批 (Static Batching)  | 运行时在 CPU/内存中将标记为 Static 的不同网格**永久合并为一个巨大网格**上传至显存 | 零 CPU 运行时计算开销；极高效率降低 DrawCall                 | **空间换时间**：相同物体合并后各自拥有独立顶点数据，**大幅暴增内存/显存**！ | 场景中静止不动的建筑、山石、道具            |
| 动态合批 (Dynamic Batching) | CPU 每一帧在运行时用软件计算，将多个相同材质的小网格顶点变换到世界空间并合并 | 支持动态移动的对象；不占用额外显存                           | **CPU 暴击**：CPU 逐顶点计算世界坐标开销极大，受限于顶点属性上限（如 Unity 限制 300 顶点） | 极小型的动态小物体（如少量碎石、字块）      |
| GPU Instancing (GPU 实例化) | 仅提交 **1 个 Mesh 顶点数据 + 1 个材质**，同时向 GPU 传进一个包含各实例世界变换矩阵的 **Array (Buffer)** | **极高效率**：零额外 CPU 顶点变换开销；显存占用极低；支持数万实例 | 必须是**完全相同的 Mesh**，且 Shader 必须支持 Instance ID 寻址 | 森林（成千上万棵树/草）、军队士兵、大量弹幕 |

### 2. [ ] Overdraw (过度绘制) 优化

Overdraw 指**同一个像素在单帧内被重复绘制/重写了多少次**。Overdraw 过高会导致 GPU 的 **Pixel/Fragment Shader 算力与显存带宽 (Fillrate)** 瞬间报废。

#### Alpha Testing (剪裁) vs Alpha Blending (混合)

- **Alpha Testing (Discard / Clip)：**
  - 在 Shader 里根据 Alpha 值判定，低于阈值直接调用 `clip()` 或 `discard` 抛弃像素。
  - **GPU 灾难（破坏 Early-Z）：** 现代 GPU 硬件拥有 **Early-Z（早期深度测试）** 机制（在 Executing Fragment Shader 之前先做深度判断，若被遮挡直接丢弃该像素）。但在 Fragment Shader 中一旦使用了 `discard/clip`，GPU 无法预先知道该像素是否会被丢弃，**必须强制关闭 Early-Z！** 这会导致后续原本可以被剔除的像素全部执行了完整的 Shader 计算。
- **Alpha Blending (混合)：**
  - 读取背景像素，按 Alpha 比例与当前颜色混合。
  - **Overdraw 杀手：** 透明物体必须关闭深度写入（`ZWrite Off`），并且必须**从后往前 (Back-to-Front)** 排序绘制。这意味着重叠在一起的所有透明层级（如几百层重叠的烟雾粒子）对应的片元着色器都会**100% 完整执行**，产生极高的 Overdraw！

```
Early-Z 正常流程:
[ 光栅化片元 ] ➔ [ Early-Z 测试 (被前物体挡住?)] ──Yes──> [ 丢弃 (0 算力浪费!) ]
                                  │ No
                                  ▼
                     [ 执行 Fragment Shader ]

若 Shader 中使用 clip/discard:
[ 光栅化片元 ] ➔ [ 强制跳过 Early-Z! ] ➔ [ 执行 Fragment Shader (消耗大量 GPU) ] ➔ [ 判断 clip() 丢弃 ]
```

#### 优化策略：

1. **渲染顺序调整：** 不透明物体严格保持 **从前向后 (Front-to-Back)** 排序绘制，充分发挥 Early-Z 作用，拦截后方被遮挡的像素。
2. **粒子系统透明贴图裁剪：** 默认粒子面片是一个矩形 Quad，四周大量透明区域也在执行 Shader。使用工具为粒子 Mesh 顺应形状**裁剪掉无用的透明边缘**（多加几个顶点，换取大幅减少透明像素填充）。

### 3. [ ] 几何与空间剔除

#### 1. 视锥体剔除 (Frustum Culling)

- **原理：** CPU 侧将场景物体的包围盒（AABB 轴对齐包围盒 / OBB 有向包围盒）与摄像机的平截头体（由 6 个平面围成的锥体区域）进行数学求交计算。
- **结果：** 完全落在视锥体外部的物体，直接被 CPU 拦下，**不向 GPU 发送其 DrawCall**。

#### 2. 遮挡剔除 (Occlusion Culling)

- **原理：** 视锥体内的物体，如果完全被前方的巨大不透明物体（如高墙、山体，称为 **Occluder**）挡住，后方的物体（称为 **Occludee**）依然不应该绘制。
- **机制：** Unity 使用 **Umbra** 引擎，通过预先烘焙场景生成遮挡数据图，运行时在 CPU 侧快速查表判定，裁切掉看不见的物体。

#### 3. LOD (Level of Detail - 多细节层次)

- **原理：** 根据物体距离摄像机的像素像素占比或距离，动态切换不同精细度的 Mesh。
  - **近处 (LOD 0)：** 10,000 面高模 + 复杂材质。
  - **中距离 (LOD 1)：** 2,000 面中模。
  - **远距离 (LOD 2)：** 200 面低模。
  - **超远距离 (Culled)：** 彻底不渲染。
- **作用：** 极大地降低 GPU 几何阶段的顶点 processing 与三角形光栅化开销。

# 3.3 贴图与 Shader 基础

### 1. [ ] 贴图压缩格式：ASTC、ETC2、DXT5/BC3

#### 为何不能在运行时使用 PNG / JPG？

- **磁盘空间 vs 显存空间：** PNG/JPG 是**磁盘传输压缩格式**（采用 Huffman/LZ77 变长编码算法）。
- **GPU 寻址机制：** GPU 渲染时需要根据 UV 坐标随机访问（Random Access）贴图的任意像素。PNG/JPG 无法做到 $O(1)$ 随机解压某个像素，必须在加载时整张图在 CPU 解压为未压缩的 **RGBA32（每个像素占 4 字节）** 填入显存。
- **结果：** 一张 $2048 \times 2048$ 的 PNG 磁盘上可能只有 2MB，但填入显存会**暴增到 16MB ($2048 \times 2048 \times 4$ Bytes)**！

#### 硬件级块状压缩格式 (Block-based Compression)

块压缩算法（如 ASTC, ETC2, DXT）将贴图切割为 **$4 \times 4$ 像素块 (Tile Block)**，对每个块单独压缩，存储基准颜色与权重索引。GPU 硬件内部内置了专用解码芯片，可以对任意 $4 \times 4$ 块做 **$O(1)$ 实时硬件级解码寻址**！

| **压缩格式**                                     | **适用平台**                | **压缩率 / 内存开销**                                        | **核心特点**                                                 |
| ------------------------------------------------ | --------------------------- | ------------------------------------------------------------ | ------------------------------------------------------------ |
| **ASTC** (Adaptive Scalable Texture Compression) | 现代移动端 (iOS / Android)  | 可自定义 Block 尺寸 ($4 \times 4$ 到 $12 \times 12$) 4x4 格式每像素占 1 Byte (8bpp) | **目前移动端最佳首选**！质量与压缩率可微调，完美支持 Alpha 通道 |
| **ETC2**                                         | 旧版 Android / OpenGLES 3.0 | 4bpp (无 Alpha) 或 8bpp (含 Alpha)                           | Android 标准兼容格式；必须是 2 的幂次方尺寸                  |
| **DXT5 / BC3**                                   | PC (DirectX / Vulkan)       | 8bpp (1 Byte/Pixel)                                          | PC 平台的标准高品质 Alpha 贴图压缩格式                       |

### 2. [ ] Mipmap (多级渐远纹理)

#### 原理：

在贴图导入时，引擎会自动预先生成一组缩小比例的贴图序列（原图 $1024 \to 512 \to 256 \to \dots \to 1$）。

```
Mip 0 : [ 1024 x 1024 ] (原始贴图)
Mip 1 : [  512 x  512 ] (面积 1/4)
Mip 2 : [  256 x  256 ] (面积 1/16)
...
```

#### 为什么显存多占用 33%？

数学等比数列求和：

$$\sum_{k=1}^{\infty} \left(\frac{1}{4}\right)^k = \frac{1}{4} + \frac{1}{16} + \frac{1}{64} + \dots = \frac{1}{3} \approx 33.3\%$$

Mipmap 会将所有缩放图附加在原图后，导致显存刚好增加原图的 **$\frac{1}{3}$ (33%)**。

#### 为什么又能提升渲染效率并减少锯齿？

1. **消除摩尔纹/走样 (Aliasing/Jitter)：** 当一个远处的物体在屏幕上只占 $10 \times 10$ 像素，而贴图是 $1024 \times 1024$ 时，邻近像素的 UV 跨度极大，会导致相邻像素采样的颜色剧烈跳动（噪点与锯齿）。Mipmap 会自动匹配最贴近当前屏幕像素密度的 Mip 层级（如 Mip 6: $16 \times 16$），输出平滑颜色。
2. **极大提升 CPU/GPU Cache Hit Rate：** 访问连续极小的 Mipmap 贴图，数据可以完美放入 GPU 的 **L1/L2 Cache Line** 中，大幅减少从高延迟显存 (VRAM) 重新加载大图的 Cache Miss，**渲染性能不降反升**！

> **⚠️ 选型策略：** 3D 场景中除 2D UI 界面（UI 尺寸恒定且要求绝对清晰，禁止开启 Mipmap）外，**所有 3D 物体贴图必须开启 Mipmap**！

### 3. [ ] 基础 Shader 编写

#### 1. 漫反射 (Lambert / Half-Lambert)

- **Lambert 光照模型：** 认为表面向所有方向均匀反射光线。强度取决于表面法线 $\vec{N}$ 与光照方向 $\vec{L}$ 的夹角余弦值：

  $$I_{Lambert} = C_{light} \cdot m_{diffuse} \cdot \max(0, \vec{N} \cdot \vec{L})$$

  - *局限：* 背光面（点积 $\le 0$）会变成一片死黑。

- **Half-Lambert (半兰伯特 - Valve 提出)：**

  将点积结果映射到 $[0, 1]$ 范围：

  $$I_{HalfLambert} = C_{light} \cdot m_{diffuse} \cdot \left( (\vec{N} \cdot \vec{L}) \times 0.5 + 0.5 \right)^2$$

  - *效果：* 避免了背光死黑，呈现出通透温暖的暗部过渡（常用于卡通/角色渲染）。

High-level shader language

```
// HLSL 核心代码实现
float3 N = normalize(i.worldNormal);
float3 L = normalize(_WorldSpaceLightPos0.xyz);

// Half-Lambert 计算
float NdotL = dot(N, L);
float halfLambert = NdotL * 0.5 + 0.5;
float3 diffuse = _LightColor0.rgb * _DiffuseColor.rgb * (halfLambert * halfLambert);
```

#### 2. 高光反射 (Phong / Blinn-Phong)

- **Phong 模型：** 依赖**反射光线方向 $\vec{R}$** 与 **视角方向 $\vec{V}$** 的夹角：

  $$I_{Phong} = C_{light} \cdot m_{specular} \cdot \max(0, \vec{R} \cdot \vec{V})^{Gloss}$$

  - *缺点：* 每帧需要实时计算反射向量 $\vec{R} = 2(\vec{N} \cdot \vec{L})\vec{N} - \vec{L}$，计算量大。

- **Blinn-Phong 模型 (优化版)：** 引入 **半角向量 $\vec{H}$**（光照方向与视角方向的中间平分向量）：

  $$\vec{H} = \frac{\vec{L} + \vec{V}}{\Vert{}\vec{L} + \vec{V}\Vert{}}$$

  $$I_{Blinn-Phong} = C_{light} \cdot m_{specular} \cdot \max(0, \vec{N} \cdot \vec{H})^{Gloss}$$

  - *优点：* 计算极其高效，且在低视角高光过渡表现比 Phong 更自然，是 Mobile 端的工业标准。

High-level shader language

```
// Blinn-Phong 核心代码实现
float3 V = normalize(_WorldSpaceCameraPos - i.worldPos);
float3 H = normalize(L + V); // 半角向量

float3 specular = _LightColor0.rgb * _SpecularColor.rgb * pow(max(0.0, dot(N, H)), _Gloss);
```

#### 3. PBR (Physically Based Rendering - 基于物理的渲染) 三大贴图

现代游戏渲染的标配，遵循**能量守恒定律**与真实物理光学特性：

1. **Albedo / Base Color (固有色贴图)：**
   - 记录材质在无光照下的纯粹颜色。
   - **禁忌：** 绝对不能把阴影（AO）、高光或环境光烘焙在 Albedo 贴图里！
2. **Normal Map (法线贴图)：**
   - **原理：** 用 RGB 三个通道分别存储表面微观法线的 $(X, Y, Z)$ 偏移方向（基准法线指向 $Z$ 轴，即 RGB 中的 B 通道，因此法线贴图整体偏**浅蓝色**）。
   - **作用：** 不增加任何几何多边形/顶点的代价下，欺骗光照计算，呈现出极度丰富的凹凸细节。
3. **Metallic / Roughness (金属度 / 粗糙度贴图)：**
   - **Metallic (金属度)：** 0 代表绝缘体（木头、塑料），1 代表导体/金属。金属具有高反射率，且漫反射几乎为 0，高光会被固有色染色。
   - **Roughness (粗糙度 / Smoothness 光滑度)：** 控制微表面分布函数 (D-Term / GGX)。粗糙度越低，微表面越平整，高光点越小越锐利；粗糙度越高，高光越分散混浊。

# 💡 优化排查 Check List（建议收藏）

在遇到帧率卡顿性能问题时，按以下步骤定位并解决：

```
                   [ 帧率下降 / 卡顿排查 ]
                              │
             ┌────────────────┴────────────────┐
             ▼                                 ▼
      【 CPU 瓶颈 】                    【 GPU 瓶颈 】
  • Batch 数 > 1000?                • 打开 Frame Debugger 观察 Overdraw
    ➔ 开启 GPU Instancing/图集        ➔ 裁切透明区域，减少 Alpha Test/Blend
  • Main Thread 单帧 > 16ms?        • Profiler 显示 ShadowMap 耗时极高?
    ➔ 检查 GC 垃圾与物理 Raycast      ➔ 降低阴影级联 (Cascades)，缩小阴影距离
  • 触发 GC Frame Drop?             • 三角形/面数过多 (> 2,000,000)?
    ➔ 对象池替换 new/Destroy          ➔ 开启 LOD Group，简化远处模型
```





# 模块四：数据结构、数学与游戏常用算法

# 4.1 核心数据结构与复杂度

在游戏开发中，选择数据结构**绝不能只看渐进时间复杂度（$O(1)$ vs $O(N)$）**，更要结合前面提到的 **CPU Cache Line 局部性** 与 **内存分配 GC** 进行综合权衡。

### 1. 数组 (Array / Vector)

- **内存特性：** 物理连续存储。
- **优点：** 完美适配 CPU Cache Line，下标访问极快 ($O(1)$)。
- **缺点：** 中间插入/删除需要平移数据 ($O(N)$)，动态扩容需要重新分配并拷贝。
- **游戏优化场景：**
  - **对象池 / 静态实体列表：** 删除元素时，如果不要求维持顺序，可以将**待删除元素与最后一个元素交换（Swap with Back），然后删除最后一个元素**，将删除复杂度直接优化至 **$O(1)$**！

### 2. 链表 (LinkedList)

- **内存特性：** 物理地址极度分散，每个节点包含数据及指向前后节点的指针。
- **缺点：** 极易引发 **CPU Cache Miss**；每个节点独立 `new` 会产生严重的内存碎片与 GC 压力。
- **游戏优化场景：**
  - 在现代游戏开发中，**尽量弃用 C# 默认的 `LinkedList<T>`**。如果确实需要链表结构，建议使用基于数组的**隐式链表/池化链表**（用数组下标充当指针）。

### 3. 哈希表 (Dictionary / Map)

- **原理：** 内部维护一个 `Buckets` 数组和一个 `Entries` 数组，通过 `GetHashCode()` 将 Key 映射到桶位置。碰撞解决通常采用**拉链法**或**开放寻址法**。
- **避坑指南：**
  1. **GC 隐患：** 如果 Key 是自定义 Struct 且未实现 `IEquatable<T>` 接口，字典每次查找都会引发**装箱 (Boxing)**！
  2. **Enum 作为 Key：** 旧版 C# 中以 `Enum` 为 Key 会导致装箱，建议使用自定义 `IEqualityComparer<T>` 或显式转为 `int`。

### 4. 二叉堆 / 优先队列 (Binary Heap / Priority Queue)

- **原理：** 用**连续数组**模拟完全二叉树。父节点与子节点的索引可以通过简单的位运算计算（$Parent = \lfloor \frac{i-1}{2} \rfloor$）。
- **特点：** 能够在 $O(1)$ 时间内取得最小值/最大值，插入与弹出调节只需要 $O(\log N)$。
- **游戏优化场景：** A* 寻路的 OpenList 优化、定时器/任务调度系统。

# 4.2 游戏高频数学

### 1. [ ] 向量运算 (Vector Math)

#### 点乘 (Dot Product)

几何定义：$\vec{A} \cdot \vec{B} = \vert{}A\vert{}\vert{}B\vert{}\cos\theta$

如果 $\vec{A}$ 和 $\vec{B}$ 是单位向量，则 $\vec{A} \cdot \vec{B} = \cos\theta$。

```
              ^ N (法线)
              │   / L (光照)
              │  /
              │ /  θ
              │/______ 
```

- **游戏高频用途：**
  1. **判断前/后方向：**
     - $\vec{A} \cdot \vec{B} > 0$ $\rightarrow$ 夹角 $< 90^\circ$（在朝向前）
     - $\vec{A} \cdot \vec{B} < 0$ $\rightarrow$ 夹角 $> 90^\circ$（在朝向后）
  2. **计算视野角度 (FOV)：** $\theta = \arccos(\hat{A} \cdot \hat{B})$。
  3. **计算向量投影长度：** 向量 $\vec{A}$ 在单位向量 $\hat{B}$ 上的投影长度 $L = \vec{A} \cdot \hat{B}$。

#### 叉乘 (Cross Product)

几何定义：$\vec{C} = \vec{A} \times \vec{B}$，结果是一个**垂直于 $\vec{A}$ 与 $\vec{B}$ 所在平面的新向量**。

模长为：$\vert{}\vec{A} \times \vec{B}\vert{} = \vert{}A\vert{}\vert{}B\vert{}\sin\theta$（数值上等于两向量围成的平行四边形面积）。

```
3D 空间 (右手定则):                  2D 空间 (Y轴朝上):
      C (A x B)                      A x B > 0 -> B 在 A 的左侧
      ▲                              A x B < 0 -> B 在 A 的右侧
      │  
      │   / B
      │  /                           A ------> 
      │ / θ                            \
──────┴──────> A                        \ B
```

- **游戏高频用途：**

  1. **计算法向量：** 知道三角形三个顶点 $P_0, P_1, P_2$，其表面法线 $\vec{N} = \text{normalize}((P_1 - P_0) \times (P_2 - P_0))$。

  2. **判断左/右方向（非常关键）：**

     在 2D/3D 平面上，计算 `Vector3.Cross(transform.forward, targetDir).y`：

     - 结果 $> 0$ $\rightarrow$ 目标在角色的**右侧**。
     - 结果 $< 0$ $\rightarrow$ 目标在角色的**左侧**。

#### 距离优化 (sqrMagnitude)

两点求距离公式为 $d = \sqrt{(x_2-x_1)^2 + (y_2-y_1)^2 + (z_2-z_1)^2}$。

- **开根号 ($\sqrt{x}$) 是极其昂贵的 CPU 指令！**
- **优化策略：** 判断“玩家是否在怪物 10 米范围内”时，不要用 `Vector3.Distance(a, b) < 10f`，改用 **`(a - b).sqrMagnitude < 100f`**（对比距离平方），直接消除开根号开销。

### 2. [ ] 四元数 (Quaternion) 与 旋转

#### 欧拉角 (Euler Angles) 与 万向节死锁 (Gimbal Lock)

- **欧拉角：** 按照一定顺序（如 $Z-X-Y$）依次绕三个轴旋转。
- **万向节死锁原理：** 当中间轴的旋转角度达到 $90^\circ$ 时（例如俯仰角 Pitch 向上看 $90^\circ$），**第一个旋转轴与第三个旋转轴的旋转平面会重合**，导致系统丧失了一个自由度（失去 Roll 或 Yaw），物体旋转会出现异常翻转。
- **优点：** 占用空间小（3个 float），符合人类直觉。

#### 四元数 (Quaternion)

为了彻底解决万向节死锁，同时支持平滑旋转，引入四元数：

$$\mathbf{q} = w + x\mathbf{i} + y\mathbf{j} + z\mathbf{k} = (w, \vec{v})$$

四元数可以表示为绕任意单位轴 $\vec{u}$ 旋转 $\theta$ 角度：

$$\mathbf{q} = \left(\cos\frac{\theta}{2}, \ \vec{u}\sin\frac{\theta}{2}\right)$$

#### Slerp (球面线性插值) vs Lerp (线性插值)

- **Lerp (Linear Interpolation)：** 直接对四元数各分量做线性混合，然后归一化（NLerp）。
  - *特点：* 计算极快，但角速度**不均匀**（中间快两头慢），适用于夹角很小的情况。
- **Slerp (Spherical Linear Interpolation)：** 沿着四元数构成的超球面大圆弧进行**等角速度**插值。
  - *特点：* 旋转平滑完美，角速度恒定；但包含 $\sin/\cos$ 计算，开销比 Lerp 稍高。

### 3. [ ] 矩阵变换 (Transform Matrix)

将 3D 点从模型坐标转为屏幕像素，依赖 **$4 \times 4$ 齐次变换矩阵**。

#### SRT 顺序与矩阵乘法

点 $P$ 施加变换的正确顺序必须是：**先缩放 (Scale)，再旋转 (Rotate)，最后平移 (Translate)**。

$$P_{world} = \mathbf{M}_{Translate} \times \mathbf{M}_{Rotate} \times \mathbf{M}_{Scale} \times P_{local}$$

> **⚠️ 必须注意：** 矩阵乘法**不满足交换律**！$\mathbf{M}_T \mathbf{M}_R \mathbf{M}_S \neq \mathbf{M}_S \mathbf{M}_R \mathbf{M}_T$。先平移再旋转会导致物体绕世界原点公转，而非自转！

#### MVP 坐标系转换全流程

```
[ 模型空间 (Local Space) ] 
       │  x Model Matrix (SRT)
       ▼
[ 世界空间 (World Space) ]
       │  x View Matrix (LookAt)
       ▼
[ 视图/相机空间 (View Space) ]
       │  x Projection Matrix (透视/正交)
       ▼
[ 裁剪空间 (Clip Space) ]
       │  除以 W (透视除法)
       ▼
[ NDC 规范化设备坐标 ] ──> 屏幕映射 ──> [ 屏幕像素坐标 (Screen Space) ]
```

# 4.3 游戏必考与常用算法

### 1. [ ] 寻路算法

#### A* 寻路与堆优化

- 核心公式：$F = G + H$
  - $G$：起点到当前节点的**实际代价**。
  - $H$：当前节点到终点的**启发式预估代价**（曼哈顿距离或对角线距离）。
- **OpenList 的二叉堆优化：**
  - 传统实现中，每一轮要从 OpenList 找出 $F$ 值最小的节点，如果用数组/链表，遍历查找是 **$O(N)$**。
  - **优化：** 将 OpenList 改用**小顶堆（Min-Heap / PriorityQueue）**，获取最小值缩短为 **$O(1)$**，插入与更新变为 **$O(\log N)$**。

#### NavMesh (导航网格) 与 漏斗算法 (Funnel Algorithm)

网格（Grid）寻路会产生巨大的节点数，而 **NavMesh** 将地形划分为凸多边形（Convex Polygons）。

```
多边形中心点连接 (产生折线/锯齿):        漏斗算法 (Funnel Algorithm 裁直线):
  [ Polygon A ]                        [ Polygon A ]
      \                                    \
       \---> [ Polygon B ]                  \────────> [ Polygon C ] (直线通过门)
               \
                \---> [ Polygon C ]
```

- **漏斗算法 (Funnel Algorithm / String Pulling)：**
  - NavMesh 寻路出相连的多边形通道后，初次连接多边形共享边（Portals）中心点会得到一条“锯齿折线”。
  - 漏斗算法从起点向终点维护一个逐渐收窄的“视线漏斗”，拉紧路径“绳子”，找到穿越所有共享边的**最短平滑直线路径**。

### 2. [ ] 空间划分算法 (Spatial Partitioning)

在碰撞检测或范围搜索时，如果对场景内 $N$ 个物体两两求交，复杂度是 **$O(N^2)$**（1000 个物体需要 50 万次检测）。空间划分将复杂度降为 **$O(N \log N)$**。

```
四叉树 (QuadTree - 2D)             八叉树 (Octree - 3D)
┌───────┬───────┐                  ┌───────┬───────┐
│  NW   │  NE   │                  │ /  NW │ /  NE │
├───────┼───────┤                  ├───────┼───────┤
│  SW   │  SE   │                  │ /  SW │ /  SE │
└───────┴───────┘                  └───────┴───────┘
```

#### 四叉树 (QuadTree) / 八叉树 (Octree)

- **原理：** 将 2D(四叉)/3D(八叉) 区域递归等分为 4 或 8 个子块。只有当物体落在某个子块内时，才将其存入该节点。
- **碰撞查询：** 检测对象只需要与其所在节点及相邻节点内的物体求交即可。

#### 宽松八叉树 (Loose Octree)

- **传统八叉树的死穴：** 如果一个物体正好跨越了划分边界，它会被强行放入更高的根节点，导致根节点堆积大量物体，降级为 $O(N^2)$；或者物体在边界移动时，导致节点频繁跨越、频繁重新插入。
- **宽松八叉树解法：** 给每个子节点的包围盒加上一个扩张系数 $k$（如 $k=1.5$），让子节点边界相互**重叠**。物体即使微小移动也不用频繁切换节点，极大提升动态物体的更新效率。

#### BVH (Bounding Volume Hierarchy - 层次包围盒树)

与八叉树（划分空间）不同，BVH 是**划分物体**：

1. 计算所有物体的总 AABB 包围盒。
2. 将物体集递归一分为二，构建一颗二叉树。
3. **射线检测应用：** 射线先与根包围盒求交，若未命中，直接剔除其下的整棵子树！Raycast 效率极高（Ray Tracing 光线追踪的核心机制）。

### 3. [ ] 程序化生成算法 (ProcGen)

#### 波函数坍缩算法 (WFC - Wave Function Collapse)

WFC 是近年来应用极其广泛的程序化关卡/地形生成算法（如《量子破碎》《赤痕》等游戏）。

```
初始状态 (叠加态):                      邻接约束传递 (坍缩过程):
每个格子可以是 [草地, 砂石, 水面, 墙体]     [ 确定的水面 ] ➔ 限制相邻格子不能是 [砂石]
                                                    ➔ 相邻格子缩减叠加态
```

- **核心逻辑流程：**
  1. **初始化叠加态：** 场景中的每个单元格（Tile/Grid）都处于“未定状态”，拥有所有可能的 Tile 选项（如草地、河流、墙壁）。
  2. **定义邻接规则（Rules）：** 规定哪些 Tile 的边可以拼在一起（如“水面”的右边只能接“水面”或“沙滩”）。
  3. **计算熵 (Entropy)：** 熵代表“不确定性”。计算哪个单元格剩余的合法 Tile 选项最少（熵最小）。
  4. **坍缩 (Collapse)：** 选中熵最小的单元格，根据概率**随机选择**一个确定的 Tile。
  5. **约束传递 (Propagation)：** 由于该单元格确定了，依据邻接规则影响其周围的邻居，把邻居不可能的选项踢出（降低邻居的熵）。
  6. **重复 3~5：** 直到全图所有格子坍缩完毕。

#### 柏林噪声 (Perlin Noise) 与 细胞自动机 (Cellular Automata)

- **柏林噪声：** 生成连续平滑的梯度噪声，用于生成自然起伏的 3D 山脉地形、云彩、纹理。
- **细胞自动机：** 基于简单规则（如“周围墙壁多于4个则自己变墙”）迭代更新二维网格，用于快速生成洞穴、随机迷宫。





# 模块五：Gameplay 架构设计模式与组件化

这是决定游戏项目**代码维护性、可扩展性以及可读性**的“软件工程基石”。

无论是在 Unity 项目中搭建 UI 框架、战斗系统，还是在商业大厂开发万人同屏的 RTS/MMO 游戏，**如果架构设计糟糕，项目会在中后期陷入“改 A 坏 B”、内存泄漏、GC 暴涨以及难以维护的深渊**。

下面将为你对“模块五：Gameplay 架构设计模式与组件化”**中的每一个知识点进行**深入剖析、架构演进推导与硬核实践指南。

# 5.1 常用设计模式

### 1. [ ] 单例模式 (Singleton)

单例模式是游戏开发中使用频率最高、也是**最容易被滥用**的模式。

#### 1. 线程安全的 C# 泛型单例 (MonoBehaviour 扩展)

在 Unity 中，全局管理器（如 `SoundManager`, `GameManager`）通常需要继承自 `MonoBehaviour` 以便挂载和使用协程/生命周期。

C#

```
public class MonoSingleton<T> : MonoBehaviour where T : MonoBehaviour 
{
    private static T _instance;
    private static readonly object _lock = new object();

    public static T Instance 
    {
        get 
        {
            lock (_lock) // 保证多线程读取时的线程安全
            {
                if (_instance == null) 
                {
                    _instance = FindObjectOfType<T>();
                    if (_instance == null) 
                    {
                        GameObject go = new GameObject(typeof(T).Name);
                        _instance = go.AddComponent<T>();
                        DontDestroyOnLoad(go); // 跨场景不销毁
                    }
                }
                return _instance;
            }
        }
    }

    protected virtual void Awake() 
    {
        if (_instance == null) 
        {
            _instance = this as T;
            DontDestroyOnLoad(gameObject);
        } 
        else if (_instance != this) 
        {
            Destroy(gameObject); // 防止场景切换时生成重复的单例
        }
    }
}
```

#### 2. 单例的致命缺点与解耦优化

- **滥用后果（代码硬编码与强耦合）：**

  如果在 `Player.cs` 中直接写 `EnemyManager.Instance.RemoveEnemy(this)`、`UIManager.Instance.UpdateHP()`、`AudioManager.Instance.PlaySound()`，类与类之间会交织成一张复杂的网，**任何单元测试和独立模块解耦都将无从谈起**。

- **最佳实践：**

  1. **服务定位器模式 (Service Locator) / 依赖注入 (DI)：** 如使用 VContainer / Zenject 替代硬编码单例。
  2. **事件总线解耦：** 玩家死亡时只抛出 `PlayerDiedEvent`，经理类各自监听该事件，而非直接互相调用。

### 2. [ ] 对象池模式 (Object Pool)

#### 预分配、激活/回收逻辑与扩容策略

对象池的核心目标是**消除运行时 `new` 与 `Destroy` 带来的 CPU 峰值与 GC 内存碎片**。

```
                    ┌────────────────────────┐
                    │      Object Pool       │
                    └───────────┬────────────┘
                                │
          Spawn() (出池)        │        Despawn() (入池)
    ┌───────────────────────────┴───────────────────────────┐
    ▼                                                       ▼
[ 从 Stack/Queue 弹出 ]                             [ 重置数据 & SetActive(false) ]
[ SetActive(true) ]                                 [ Push/Enqueue 回池 ]
```

#### 核心要素设计：

1. **数据结构选型：** 优先选择 `Stack<T>` 或 `Queue<T>`，底层基于连续数组，Cache 友好。
2. **回收（Despawn）数据重置：** 归还对象时，必须重置其所有状态（如 Transform 归零、刚体速度清零、状态机复位），否则下次取出时会带着上次销毁时的“残余状态”。
3. **动态扩容与上限管理：**
   - **策略 A（自动扩容）：** 当池子为空且再次申请时，动态 `Instantiate` 新对象填充。
   - **策略 B（容量封顶保护）：** 为防止内存无限制暴涨，设置 `MaxPoolSize`。超过上限的回收对象直接 `Destroy`，防止积压堆内存。

### 3. [ ] 有限状态机 (FSM) 与 行为树 (Behavior Tree)

#### 1. 有限状态机 (FSM - Finite State Machine)

- **架构设计：** 将对象的行为拆分为独立的状态类（实现 `IState`），包含 `OnEnter()`、`OnUpdate()`、`OnExit()` 三个生命周期。
- **状态转换表 (Transition Table)：** 由状态机统一管理当前状态，并在切换时严格调用旧状态的 `Exit()` 与新状态的 `Enter()`。

C#

```
public interface IState {
    void OnEnter();
    void OnUpdate();
    void OnExit();
}

public class FSM {
    private IState _currentState;

    public void TransitionTo(IState newState) {
        _currentState?.OnExit();
        _currentState = newState;
        _currentState?.OnEnter();
    }

    public void Update() {
        _currentState?.OnUpdate();
    }
}
```

- **适用场景：** 玩家角色控制器（Idle, Run, Jump, Attack）、简易 NPC 逻辑、UI 状态切换。
- **局限：** 状态过多时会产生“状态组合爆炸”（$N$ 个状态可能有 $N^2$ 条连线），逻辑极易混乱。

#### 2. 行为树 (Behavior Tree)

针对复杂怪物与高级 AI，行为树采用**树状逻辑节点**，每次 Tick 从根节点向下遍历，返回三种状态之一：`SUCCESS`（成功）、`FAILURE`（失败）、`RUNNING`（运行中）。

```
                        [ Root (Selector) ]
                                 │
           ┌─────────────────────┴─────────────────────┐
           ▼                                           ▼
 [ Sequence: 追击与攻击 ]                       [ Sequence: 巡逻 ]
   ├── Decorator: 玩家在范围内?                   ├── Action: 寻找巡逻点
   ├── Action: 移动向玩家                         └── Action: 移动至巡逻点
   └── Action: 执行攻击
```

#### 四大核心节点类型：

| **节点类型**                       | **逻辑判断规则**                                             | **对应编程语法**     |
| ---------------------------------- | ------------------------------------------------------------ | -------------------- |
| **选择节点 (Selector / Fallback)** | **逐个尝试：** 只要有一个子节点返回 `SUCCESS`，立即返回 `SUCCESS`；全失败才返回 `FAILURE` | `逻辑或 (OR)`        |
| **顺序节点 (Sequence)**            | **环环相扣：** 只要有一个子节点返回 `FAILURE`，立即返回 `FAILURE`；全成功才返回 `SUCCESS` | `逻辑与 (AND)`       |
| **平行节点 (Parallel)**            | **同时执行：** 多个子节点同时 Tick（如：一边向玩家移动，一边播放喊叫动画） | 多任务并行           |
| **装饰节点 (Decorator)**           | **条件过滤 / 逆转：** 附着在子节点上，充当 `If` 条件判断，或将结果取反 (`Inverter`) | `If-Else / 取反 (!)` |

### 4. [ ] 观察者模式 / 事件总线 (Event System)

#### 全局消息解耦与类型安全事件总线

传统的 `Action<T>` 虽好，但如果事件太多，定义和维护成本极高。现代游戏架构倾向于使用**基于强类型 (Type-Based) 的事件总线**。

C#

```
// 1. 定义事件结构体 (零 GC 堆开销)
public struct PlayerHealthChangedEvent {
    public int CurrentHP;
    public int MaxHP;
}

// 2. 类型安全的事件总线核心实现
public static class EventBus 
{
    private static readonly Dictionary<Type, Delegate> _events = new Dictionary<Type, Delegate>();

    public static void Subscribe<T>(Action<T> listener) where T : struct 
    {
        Type type = typeof(T);
        if (!_events.ContainsKey(type)) _events[type] = null;
        _events[type] = (Action<T>)_events[type] + listener;
    }

    public static void Unsubscribe<T>(Action<T> listener) where T : struct 
    {
        Type type = typeof(T);
        if (_events.ContainsKey(type) && _events[type] != null) 
        {
            _events[type] = (Action<T>)_events[type] - listener;
        }
    }

    public static void Raise<T>(T eventData) where T : struct 
    {
        Type type = typeof(T);
        if (_events.TryGetValue(type, out Delegate del) && del != null) 
        {
            ((Action<T>)del)(eventData);
        }
    }
}
```

- **优点：** 彻底取消了字符串事件名映射，编译期类型检查，零装箱/拆箱开销，实现了**发布者与订阅者的完全解耦**。

### 5. [ ] 命令模式 (Command Pattern)

#### 撤销/重做 (Undo/Redo) 与 回放/输入映射

命令模式将“请求”或“动作”封装为一个对象，使得你可以用不同的请求对客户进行参数化、将请求排队或记录请求日志，支持可撤销的操作。

C#

```
public interface ICommand {
    void Execute();
    void Undo();
}

// 移动命令
public class MoveCommand : ICommand {
    private Transform _transform;
    private Vector3 _translation;

    public MoveCommand(Transform transform, Vector3 translation) {
        _transform = transform;
        _translation = translation;
    }

    public void Execute() => _transform.position += _translation;
    public void Undo() => _transform.position -= _translation; // 反向撤销
}
```

#### 游戏核心应用：

1. **输入映射 (Input Rebinding)：** 将按键输入（如按 A 键）映射为 `ICommand` 对象（如 `JumpCommand`），方便随时修改按键绑定。
2. **战棋 / 策略游戏 Undo 栈：** 维护一个 `Stack<ICommand>`，每次玩家走格子或攻击时将 Command 入栈，点击“撤销”按钮时弹出并执行 `Undo()`。
3. **网络同步与战报回放 (Replay System)：** 将玩家每帧的命令序列记录下来保存为文件，重新播放时依次执行，即可完美复现游戏过程。

# 5.2 软件设计原则与数据驱动

### 1. [ ] SOLID 原则在游戏中的应用

- **单一职责原则 (SRP - Single Responsibility Principle)：**
  - *反面教材：* 一个 `Player.cs` 写了 3000 行，既管输入控制、血量计算、UI 显示，又管音效播放和物理碰撞。
  - *重构：* 拆分为 `PlayerInput`、`PlayerHealth`、`PlayerAudio`、`PlayerUI` 等独立组件。
- **开闭原则 (OCP - Open/Closed Principle)：**
  - **对扩展开放，对修改关闭。** 增加新技能时，不应该去改动原本的 `SkillManager.cs` 里的巨大 `switch-case`，而是派生一个新的 `Skill` 子类，实现 `Execute()` 接口。
- **依赖倒置原则 (DIP - Dependency Inversion Principle)：**
  - **高层模块不应该依赖低层模块，两者都应该依赖其抽象。**
  - 逻辑层不要直接依赖具体 UI 脚本 `HpBarUIView`，而是依赖 `IHealthView` 接口，方便后续替换 UI 实现或做单元测试。

### 2. [ ] 数据与表现分离 (MVC / MVP / Data-Driven)

游戏开发中最忌讳的是将**数值/配置数据硬编码 (Hardcode) 在 C# 脚本**中。

```
                    ┌─────────────────────────┐
                    │    配置数据 (Data)      │
                    │  (ScriptableObject/JSON)│
                    └────────────┬────────────┘
                                 │
                   数据驱动      │ 驱动
                                 ▼
                    ┌─────────────────────────┐
                    │    逻辑层 (Model/Presenter)│
                    │   (HP, 属性计算, 状态)   │
                    └────────────┬────────────┘
                                 │
                   事件广播      │ (Event Bus)
                                 ▼
                    ┌─────────────────────────┐
                    │    表现层 (View/Render) │
                    │ (UI 界面, 粒子, 音效)   │
                    └─────────────────────────┘
```

#### 架构三层职责划分：

1. **数据层 (Data / Model)：** 存储数值、配置、游戏状态。可序列化为 **ScriptableObject**（Unity 原生极佳的数据载体）、JSON 或 Excel 导表。**绝不包含任何渲染、UI 或 Mono API 代码！**
2. **逻辑层 (Presenter / Controller)：** 负责计算伤害、冷却判定、扣减血量。**不直接访问 UI 组件**，只通过抛出事件通知外层。
3. **表现层 (View)：** 监听逻辑层的事件，播放受击动画、音效、扣血数字飘字与 UI 血条更新。

> **💡 带来的巨大好处：** 数值策划可以随意调整 Excel 或 ScriptableObject 中的属性，而无需重新编译代码；可以在关闭所有 UI 表现的情况下，对逻辑层做极速的纯自动化测试！

### 3. [ ] ECS 架构思想 (Entity Component System)

传统的面向对象 (OOP) 在游戏物体极多（如数万个单位的 RTS 游戏）时会陷入两大绝境：

1. **继承树过深与菱形继承问题：** `GameObject -> Character -> Monster -> FlyingMonster -> Dragon`。如果要加一个“会飞的机械箱子”，继承关系会彻底崩溃。
2. **内存与 CPU 缓存灾难：** OOP 的对象随机分布在堆中，遍历上万个对象会导致 CPU Cache Miss 率高达 90%+（即前面模块一提到的**分散内存**问题）。

#### ECS (Data-Oriented Design - DOD) 拆解：

```
传统 OOP (Object-Oriented):
[ Entity A ] ➔ 包含位置, 包含血量, 包含渲染 (内存分散)
[ Entity B ] ➔ 包含位置, 包含血量, 包含渲染 (内存分散)

ECS 模式 (Data-Oriented):
Components (纯数据 Struct，连续数组):
Positions : [ PosA | PosB | PosC | PosD | ... ]  <--- 内存绝对连续!
Healths   : [ HP_A | HP_B | HP_C | HP_D | ... ]  <--- 内存绝对连续!

Systems (纯逻辑):
MovementSystem ➔ 遍历 Positions 数组进行计算 (CPU Cache Line 100% 命中!)
```

#### ECS 三大组件：

- **Entity（实体）：** 仅仅是一个**全局唯一的整数 ID**，没有任何数据或逻辑，只充当组件组合的容器。
- **Component（组件）：** **纯数据结构体 (Struct)**，只存放数值（如 `PositionComponent { float x, y, z; }`），没有任何方法逻辑！所有组件按类型连续排列存放在巨型内存数组中。
- **System（系统）：** **纯逻辑函数**，没有任何状态。它通过筛选包含特定 Component 组合的 Entity 集合，以极其高效的循环批处理数据（配合多线程 Job System 与 SIMD 指令集）。

# 💡 架构设计 Check List

在搭建项目 Gameplay 框架时，可对照下表自查：

```
                   [ 项目架构质量评估 ]
                             │
             ┌───────────────┴───────────────┐
             ▼                               ▼
      【 耦合度评估 】                 【 扩展性与数据驱动 】
  • 核心逻辑类是否满屏写着           • 角色数值/技能参数是否
    xxxManager.Instance?               硬编码在 C# 中?
    ➔ 改用事件总线/依赖注入            ➔ 提取为 ScriptableObject/Excel
  • UI 脚本里是否在计算战斗伤害?       • 增加一个新怪物是否需要
    ➔ 剥离为 Model/Presenter           修改主逻辑 switch-case?
  • 动画/音效与逻辑强绑定?             ➔ 抽象为状态机/行为树节点
    ➔ 改为事件监听触发
```





# 模块六：多人网络同步与工程化

# 6.1 网络同步技术 (Netcode)

网络同步的核心矛盾是：**物理世界的网络延迟（Latency）、抖动（Jitter）和丢包（Packet Loss）与玩家对“实时响应”和“绝对公平”的极高要求之间的冲突**。

### 1. TCP vs UDP 与 KCP/ENet

#### 传输层协议对比

| **特性**      | **TCP (Transmission Control Protocol)**                    | **UDP (User Datagram Protocol)**   |
| ------------- | ---------------------------------------------------------- | ---------------------------------- |
| **连接机制**  | 面向连接（三次握手、四次挥手）                             | 无连接（直接发包）                 |
| **可靠性**    | **100% 可靠**（丢包重传、顺序保证）                        | **不可靠**（可能丢包、乱序、重复） |
| **包头开销**  | 较大（20 ~ 60 字节）                                       | 极小（8 字节）                     |
| **延迟/卡顿** | **队头阻塞 (Head-of-Line Blocking)**：前包丢失，后包全堵住 | 无队头阻塞，收到即处理             |
| **适用场景**  | 登录、大厅聊天、回合制、商城交易                           | 实时动作、FPS、MOBA、 Racing       |

```
TCP 队头阻塞 (Head-of-Line Blocking):
发送端 ➔ [包1] [包2] [包3] 
接收端 ➔ [包1] [丢失!] [包3 (被强行挡住，必须等待包2重传!)] ──> 造成画面大幅抖动/卡顿

UDP + 应用层可靠性 (如 KCP):
发送端 ➔ [包1] [包2] [包3]
接收端 ➔ [包1] [丢失!] [包3 (立即处理! 包2选择性重传)] ──> 保证极低延迟与画面平滑
```

#### 为什么实时游戏偏爱基于 UDP 的 KCP / ENet？

- **TCP 致命伤：** TCP 的重传机制非常保守（RTT 翻倍算法），且拥有强顺序保证。一旦发生丢包，**后续所有已到达的数据包必须停留在缓冲区等待重传包**，导致游戏出现“网络卡顿-突然快进”的糟糕体验。
- **KCP / ENet 优势：** 在 UDP 之上通过应用层算法实现了**选择性重传 (Selective Repeat)、非退让流控、快速重传**。它们用 10%~20% 的带宽浪费换取了比 TCP **降低 30%~40% 的平均延迟**，且不会触发队头阻塞。

### 2. [ ] 状态同步 (State Synchronization)

#### 核心思想与服务器权威 (Server Authoritative)

- **核心逻辑：** **“服务器算一切，客户端只负责展示”**。客户端将按键输入发送给服务器，服务器运行逻辑并运算出最新状态（如位置、HP、状态机），定期（如 20Hz~60Hz）广播给所有客户端。
- **防作弊：** 由于逻辑完全在服务器计算，客户端无法通过篡改本地内存来透视外挂或一键锁头，**安全性极高**。

```
[ 客户端 A ] ─── 发送输入 (MoveRight) ───> [ 权威服务器 (Server) ]
     ▲                                                │
     │                                     计算位置，广播最新 State
     └─────────── 收到 Transform (Pos=10,2) ──────────┘
```

#### 关键平滑技术：

1. **客户端预测 (Client-side Prediction)：** 玩家按下移动键时，本地角色**不需要等待服务器返回**，立即先行移动；当收到服务器状态包时，若位置相符则继续，若有偏差则进行纠正。
2. **快照插值 (Snapshot Interpolation)：** 对方玩家的位置以 20Hz 频率发来时是断续的，客户端维护一个短暂的缓冲队列，在本地渲染帧（60Hz/120Hz）间做**Lerp/Slerp 平滑插值**，消除卡顿感。
3. **拉回/服务器补偿 (Lag Compensation)：** 服务器在计算 FPS 射击命中时，根据玩家的 RTT **将历史碰撞体倒退回射击时的位置**进行判定，解决“明明瞄准了却打不中”的问题。

### 3. [ ] 帧同步 (Deterministic Frame Sync)

#### 核心思想与确定性 (Determinism)

- **核心逻辑：** 服务器不计算逻辑，只充当“输入转发器”。服务器按固定 Tick（如 15ms/帧）收集所有玩家的输入指令并广播，**各客户端从相同的初始状态出发，在相同的逻辑帧执行相同的输入，推导出完全一致的结果**。
- **优点：** 网络传输数据量极小（仅传输按键指令），非常适合 RTS（万人同屏）、MOBA、格斗游戏；非常天然地支持战局录像回放 (Replay)。

#### 确定性计算的“最大杀手”：浮点数不一致 (Floating-Point Non-Determinism)

不同 CPU 架构（x86 vs ARM）、不同编译器/优化选项甚至不同操作系统的 IEEE 754 浮点数指令（如 FMA 累加乘法）计算结果可能有微小差异（如 `1.0000001` vs `1.0000002`）。经过上万帧累积，会导致严重的不同步（Desync）。

- **解决方案（定点数化 Fixed-Point Math）：** 彻底禁用 `float` / `double`，使用整数（`int` / `long`）模拟小数，统一封装自定义的定点数数学库（如用后 12 位表示小数部分）。

C#

```
// 定点数示例 (Fixed-Point Integer Math)
public struct Fixed32 
{
    public int RawValue;
    private const int SHIFT = 10; // 2^10 = 1024 放大系数

    public static Fixed32 FromInt(int v) => new Fixed32 { RawValue = v << SHIFT };
    public static Fixed32 operator +(Fixed32 a, Fixed32 b) 
        => new Fixed32 { RawValue = a.RawValue + b.RawValue };
    // 强制全局跨平台统一结果
}
```

#### 回滚同步 (Rollback Netcode / GGPO)

传统的等帧同步（Delay-based）在网络延迟高时会强制暂停游戏等待网络包，造成“按键响应极其粘手”。

```
正常预测:  帧 10 (本地按键) ───> 预测执行 帧 11 ───> 预测执行 帧 12
                                                        │ (收到服务器确切指令：帧 11 对方发了技能!)
回滚与重算:                                              ▼
                                                  [ 1. 状态快照回滚至 帧 10 ]
                                                  [ 2. 注入对方 帧 11 指令 ]
                                                  [ 3. 极速重算 帧 11 ➔ 帧 12 ]
                                                  [ 4. 渲染最新的 帧 12 画面 ]
```

- **实现机制：**
  1. **预测 (Predict)：** 本地输入立即响应，对于未到来的对手指令，**假设对手保持上一帧状态**并继续向前模拟。
  2. **保存快照 (Save Snapshot)：** 每一帧保存当前游戏逻辑状态的轻量快照。
  3. **回滚 (Rollback)：** 当收到延迟到达的对手真实指令发现与预测不一致时，**恢复到发生产生偏差的那一帧快照，注入真实指令，在单帧内 CPU 极速重新推算至当前帧**。

# 6.2 工具链与工程化

### 1. [ ] 版本控制 (Git / LFS) 与 分支管理

#### Git LFS (Large File Storage) 配置

游戏工程中包含大量二进制大文件（如 `.png`, `.psd`, `.fbx`, `.mp3`, `.wav`, `.prefab`）。直接存入普通 Git 会导致 `.git` 仓库体积迅速膨胀至数十 GB，检出速度极慢。

Ini, TOML

```
# .gitattributes 典型配置
*.png filter=lfs diff=lfs merge=lfs -text
*.fbx filter=lfs diff=lfs merge=lfs -text
*.psd filter=lfs diff=lfs merge=lfs -text
*.wav filter=lfs diff=lfs merge=lfs -text
*.asset filter=lfs diff=lfs merge=lfs -text
```

- **原理：** Git LFS 将真实的大文件存在单独的指针服务器上，Git 仓库内部仅保存极其微小的**文本指针 (Pointer File)**。

#### 规范的 Git Flow 分支管理策略

```
[ main / master ] ─────────────● (生产环境发布版本 v1.0)
                                ▲
                                │ Merge
[ release ] ──────────────●─────┴─ (预发布测试分支)
                          ▲
                          │ Merge
[ develop ] ───●──────────┼───────● (主开发分支)
               │          │       ▲
               │ Branch   │       │ Pull Request / Code Review
[ feature/* ]  └──●───────┴───────┘ (个人功能分支: feature/player-combat)
```

### 2. [ ] 性能分析工具 (Profiler) 与 Frame Debugger

#### Unity Profiler / Godot Profiler 核心关注指标

```
                        [ Profiler 性能瓶颈诊断 ]
                                    │
         ┌──────────────────────────┼──────────────────────────┐
         ▼                          ▼                          ▼
   【 CPU 耗时 】            【 GC Alloc 内存 】        【 GPU 渲染 】
 • Physics.Simulate        • GC.Collect 频率          • GFX.WaitForPresent
 • GC.Alloc 导致的停顿       • 避免 Update 内           • Vertices / Batches
 • Mono 脚本耗时高           写 string / 临时数组      • 过多 Overdraw
```

- **CPU 采样重点：**
  - `GC.Alloc`：查找哪一行代码在频繁申请临时堆内存（如 `Update` 里 `GetComponent`、`string` 拼接或 LINQ）。
  - `Physics.Simulate`：检查碰撞体设置是否过密，或射线检测 (Raycast) 数量超标。
- **Frame Debugger (帧分析器)：**
  - **逐 DrawCall 拆解：** 沿渲染流水线查看当前帧的绘制顺序，检查材质是否成功批处理（Dynamic Batching / SRP Batcher / GPU Instancing），定位导致 DrawCall 飙升的罪魁祸首（如多余的材质球、UI 叠层破碎）。

### 3. [ ] 自动化构建与打包 (CI/CD) 与 资源热更新

#### 1. 命令行自动化打包 (Batchmode)

借助 Jenkins、GitHub Actions 或 GitLab CI，结合 Unity 的 `-batchmode -nographics` 命令行参数实现无人值守构建。

Bash

```
# 自动化打包命令行脚本示例
Unity.exe -batchmode -quit \
  -projectPath "D:/Project/MyGame" \
  -executeMethod BuildScript.BuildAndroid \
  -logFile "D:/Project/build.log"
```

#### 2. 可寻址资源系统 (Addressables / AssetBundle) 与热更新

传统安装包每次更新哪怕 1 个纹理都需要重新下载安装包，而 Addressables 实现了**资源与代码/安装包的解耦**。

```
                    ┌─────────────────────────┐
                    │    Addressables System  │
                    └────────────┬────────────┘
                                 │
                   资源寻址 (Key: "HeroPrefab")
                                 │
         ┌───────────────────────┴───────────────────────┐
         ▼                                               ▼
[ 本地资源包 (Local AB) ]                         [ 远端服务器 (Remote CDN) ]
(打在 APK/IPA 内部)                              (线上热更新下载目录)
```

#### 核心工作流：

1. **资源寻址 (Address Key)：** 不再依赖 `Resources.Load("path")` 字符串路径，而是为每个 Prefab / 音效指定唯一 Key（如 `"Character/Warrior"`）。
2. **依赖分析与打包 (Dependency Analysis)：** Addressables 自动分析资源之间的共同依赖（如多个 Prefab 共享同一材质球），避免重复打包导致的包体膨胀。
3. **增量更新 (Content Update)：** 游戏运行时向 CDN 发送配置对比请求；若有资源更新，仅下载变化的 `.bundle` 字节文件存入本地沙盒目录，**实现无需重新下包的静默热更新**。

# 🛠️ 网络与工程化评估表格

在进行架构设计或技术选型时，可根据游戏类型与团队规格进行参考：

| **维度**         | **状态同步 (State Sync)**      | **帧同步 (Frame Sync)**           |
| ---------------- | ------------------------------ | --------------------------------- |
| **计算位置**     | 服务器端权威计算               | 各客户端独立本地计算              |
| **网络带宽高低** | 较高（随视野内物体增加而增加） | 极低（仅传输玩家输入操作）        |
| **CPU/算力开销** | 集中在服务器                   | 集中在客户端                      |
| **防作弊能力**   | **极高**（客户端无逻辑）       | 较弱（需逻辑校验或录像回放）      |
| **技术难点**     | 客户端预测、插值与延迟补偿     | **定点数数学库、回滚状态快照**    |
| **典型游戏**     | 《绝地求生》《MMORPG》《原神》 | 《王者荣耀》《星际争霸》《街霸6》 |







# 模块七：Unity与UE商业级游戏框架

# 7.1 Unity 常见商业框架体系与架构

Unity 官方给的是极度自由的 Component 架构（`MonoBehaviour`），如果在大型项目中“想怎么挂脚本就怎么挂”，项目后期会导致**场景加载巨慢、内存泄漏严重、UI 各种穿透卡顿、代码交织成乱麻**。因此，商业项目必须建立强约束的全局框架。

### 1. 资源管理框架（Asset Management & Hotfix）

#### AssetBundle (AB包) 依赖链与打包避坑

在 Unity 中，如果 2 个独立的 Prefab 都引用了同一个材质球或贴图，而这个材质球没有被**显式指定**打包进某个 AB 包，Unity 打包时会把这个材质球**分别打入这两个 Prefab 的 AB 包中**，造成**冗余包（Redundant Assets）与内存翻倍**！

```
错误写法 (冗余打包):
Prefab A.ab ───► [ 包含 Texture_X (10MB) ]
Prefab B.ab ───► [ 包含 Texture_X (10MB) ]  ───► 浪费 10MB 磁盘与内存!

正确做法 (依赖提取):
Common.ab   ───► [ 包含 Texture_X (10MB) ]
Prefab A.ab ───► 依赖 Common.ab
Prefab B.ab ───► 依赖 Common.ab
```

- **依赖循环 (Circular Dependency)：** 包 A 依赖包 B，包 B 又依赖包 A。加载包 A 时会触发无限循环加载导致死锁。
- **解决方案：** 打包前必须通过脚本分析 Asset 依赖图，提取公共资源到公共包，并做无向环检测。

#### Addressables (可寻址资源系统) 引用计数与内存释放

Addressables 底层依然基于 AB 包，但它引入了**强类型的引用计数 (Reference Counting) 管理系统**。

C#

```
// 1. 异步加载资源并递增引用计数 (+1)
AsyncOperationHandle<GameObject> handle = Addressables.LoadAssetAsync<GameObject>("HeroPrefab");
yield return handle;

GameObject hero = Instantiate(handle.Result);

// 2. 销毁实例与释放资源
Destroy(hero);
// 必须显式释放 Handle！递减引用计数 (-1)，当计数为 0 时，底层 AB 包才会被真正的 Unload
Addressables.Release(handle); 
```

> **⚠️ 致命坑点：** 如果遗漏了 `Addressables.Release()`，底层 AB 包的内存将永久残留，造成严重的内存泄漏！

#### 热更新框架机制 (AOT + Interpreter)

由于 iOS 平台禁止动态生成可执行代码（JIT / AOT 限制），现代 Unity 热重载普遍采用 **HybridCLR (补充元数据 AOT 方案)** 或 **ILRuntime (虚拟机解释执行方案)**。

```
[ 运行时加载 (Hotfix.dll.bytes) ]
               │
               ▼
┌──────────────────────────────┐
│  HybridCLR / ILRuntime 解释器│ ───► 执行热更逻辑 (无需重新下包/换 APK)
└──────────────┬───────────────┘
               │
               ▼  通过 MD5 / CRC 校验文件一致性
┌──────────────────────────────┐
│    远端 CDN 资源服务器       │ ───► 下载最新的 Addressable Bundles
└──────────────────────────────┘
```

### 2. UI 框架（UI Framework / UI Manager）

#### UI 窗体层级管理与 Sorting Order

商业级 UI 框架通常会将 UI 窗口统一划分为明确的渲染层级，防止弹窗被普通界面遮挡：

```
Top (系统顶层 - 遮罩/Loading/断线重连)  ──────── Order Range: 4000 ~ 5000
 PopUp (弹窗层 - 确认框/提示/获得奖励)  ──────── Order Range: 3000 ~ 3999
 Fixed (固定层 - 主界面 HUD/血条/小地图) ──────── Order Range: 2000 ~ 2999
 Normal (普通层 - 背包/商店/任务界面)   ──────── Order Range: 1000 ~ 1999
```

#### UI 栈 (UI Stack) 与导航逻辑

针对全屏界面（如从主界面 $\rightarrow$ 背包 $\rightarrow$ 道具详情），采用 **UI 栈** 统一管理：

- `PushUI("BagWindow")`：将当前主界面挂起/隐去，将背包压入栈顶并播放显示动画。
- `PopUI()`：关闭当前栈顶界面，**自动唤醒并恢复**上一个界面（无需硬编码切换逻辑）。

#### Canvas 动静分离优化

在 Unity UGUI 中，**只要 Canvas 下有一个 UI 元素改变（如文字更新、平移），整个 Canvas 内部的所有顶点都会重新计算合并（Rebatch）**，造成 CPU 峰值！

- **优化方案（动静分离）：** 将绝不改变的静态背景、框体放在 `Static Canvas`；将频繁变动的文本、血条、CD 冷却圈放在单独的 `Dynamic Canvas`，减少 Rebatch 范围。

### 3. 网络与数据传输框架

#### 网络消息派发器与 Heartbeat 机制

```
[ 网络 Socket 字节流 ]
         │
         ▼
[ 长包解包 (LengthHeader) ] ───► 解出 [ MsgID | Payload ]
                                        │
                                        ▼  派发器 (MessageDispatcher)
                          ┌─────────────┴─────────────┐
                          ▼                           ▼
                 [ PlayerMoveHandler ]       [ ItemUpdateHandler ]
```

- **粘包与断包：** 基于 TCP / UDP 传输时，必须在包头加入 4 字节的包体长度 Header。解包时先读取 Length，数据不够则等待，多余则截断。
- **心跳检测 (Heartbeat) 与断线重连：** 定时（如每 3~5 秒）向服务端发送 Ping 包，超时 3 次未收到 Pong 即判定断线，自动触发后台静默重连与 Session 恢复。

#### 为什么弃用 JSON/XML，全面转向 Protobuf / FlatBuffers？

1. **数据体积极其微小：** Protobuf 采用 **Varint & ZigZag 压缩编码**，去除了所有 Key 字符串字段名，体积仅为 JSON 的 1/5 ~ 1/10。
2. **序列化速度极快：** 彻底去除了文本解析与字符串匹配开销。
3. **FlatBuffers 特性（零拷贝）：** 甚至无需 Deserialize 操作，直接访问二进制内存块，常用于大地图网格与大型存档。

### 4. 常用开源框架思想

- **GameFramework (GF)：** 极其严谨的单例与模块化架构。通过 `Procedure`（流程节点）掌控游戏主循环（如：`SplashProcedure` $\rightarrow$ `CheckVersionProcedure` $\rightarrow$ `LoginProcedure` $\rightarrow$ `MainCityProcedure`）。
- **UniTask：** 零 GC 堆开销的 `async/await` 异步解决方案，彻底替代传统 Unity 协程（Coroutine）。
- **Zenject (Extenject) / VContainer：** 依赖注入（DI）框架。不再通过 `GetComponent` 或 `Instance` 获取依赖，而是通过构造函数或 `[Inject]` 自动注入，彻底实现低耦合。

# 7.2 Unreal Engine (UE) 官方 Gameplay 框架

与 Unity 的“自由无约束”不同，Unreal Engine 官方提供了一套**高度规范、功能强大且强制遵守的官方 Gameplay 框架**。不懂这套框架分工，就无法真正编写正确的 UE 代码。

### 核心 Architecture 概览

```
                       ┌─────────────────────────┐
                       │      UGameInstance      │ (全局唯一，跨 Level 存在，不随切关卡销毁)
                       └────────────┬────────────┘
                                    │
                                    ▼
                          ┌──────────────────┐
                          │    AGameMode     │ (服务器/关卡规则，单机/Server 独占)
                          └─────────┬────────┘
                                    │
          ┌─────────────────────────┴─────────────────────────┐
          ▼                                                   ▼
┌──────────────────┐                                ┌──────────────────┐
│   APlayerState   │ (玩家数据/得分/Ping)             │    AGameState    │ (全网广播的全局状态/比分)
└──────────────────┘                                └──────────────────┘
          ▲                                                   ▲
          │                                                   │
┌──────────────────┐    控制 (Possess)     ┌──────────────────┐│
│ APlayerController├──────────────────────►│      APawn       ││ (世界物理实体/角色)
└──────────────────┘                      │   (ACharacter)   │┘
```

### 1. UE 核心 Actor 与组件继承树（必考底层）

```
UObject  (UE 极低层基类，提供 GC、反射系统 UPROPERTY、序列化)
  └── AActor  (可放入/生成在 3D 场景中的基本单位，支持 Replicate 网络同步)
        └── UActorComponent  (挂载在 Actor 上的功能组件，无 Transform 变换)
              └── USceneComponent  (带有 Transform 坐标、旋转、缩放的组件)
                    └── UPrimitiveComponent  (带有物理碰撞、网格体 Render 的渲染组件)
```

#### Actor 生命周期关键函数顺序：

1. **`PostLoad()` / `PostActorCreated()`：** 序列化加载完成或动态 Spawn 后触发。
2. **`InitializeComponent()`：** Actor 上挂载的所有组件初始化完毕。
3. **`BeginPlay()`：** 游戏逻辑正式开始运行（仅触发一次），相当于 Unity 的 `Start()`。
4. **`Tick(float DeltaSeconds)`：** 每帧轮询逻辑，相当于 Unity 的 `Update()`。
5. **`EndPlay(EEndPlayReason::Type)`：** 从世界中销毁、切关卡时触发，用于清理引用。

### 2. UE 官方 Gameplay 核心类分工

在多人网络游戏中，每个类的**存在范围与网络同步（Replication）属性**是面试的核心：

| **类名**                          | **存在的端 (Multiplayer)**               | **核心职责与分工**                                           |
| --------------------------------- | ---------------------------------------- | ------------------------------------------------------------ |
| **`UGameInstance`**               | Server & 所有 Clients 独立存在           | 贯穿整个进程，**切关卡不销毁**。用于存储全局 UI、跨关卡数据、玩家账号 Token |
| **`AGameModeBase / AGameMode`**   | **仅 Server 端存在**（Client 为 `null`） | **绝对的裁判与规则**。负责胜利判定、生成角色、规则限制。客户端无法篡改 |
| **`AGameStateBase / AGameState`** | Server & 所有 Clients 同步               | **全场共享的记分板**。保存比赛进度、剩余时间、团队总得分等所有玩家可见的数据 |
| **`APlayerController`**           | 本地 Server & **对应的** 本地 Client     | **玩家的大脑/玩家本人**。处理键盘鼠标输入、UI 打开关闭、摄像机控制 |
| **`APawn / ACharacter`**          | Server & 所有 Clients 同步               | **世界里的肉身**。`ACharacter` 包含 `CharacterMovementComponent`（处理复杂的预测移动） |
| **`APlayerState`**                | Server & 所有 Clients 同步               | **跨肉身的数据**。存储玩家名字、KDA、Ping 值。即使角色死亡肉身销毁，此数据依然保留 |

### 3. 输入与高级 Gameplay 模块

#### Enhanced Input System (增强输入系统)

UE5 推荐的标准输入方案，将输入彻底模块化：

- **Input Action (IA)：** 定义动作概念（如 `IA_Jump`、`IA_Move`）。
- **Input Mapping Context (IMC)：** 定义具体的按键绑定与修饰器（如 W/A/S/D 或手柄摇杆）。
- *优势：* 可在运行时根据场景**动态切换 IMC**（例如：玩家登上载具时，瞬间将“步兵按键映射”切换为“驾驶载具按键映射”）。

#### GAS (Gameplay Ability System - 技能系统框架)

UE 官方出品的最强技能与战斗框架（曾用于《Paragon》《堡垒之夜》）：

```
                       ┌─────────────────────────┐
                       │  Ability System Comp.   │ (ASC 组件)
                       └────────────┬────────────┘
                                    │
         ┌──────────────────────────┼──────────────────────────┐
         ▼                          ▼                          ▼
┌──────────────────┐       ┌──────────────────┐       ┌──────────────────┐
│ GameplayAbility  │       │  AttributeSet    │       │  GameplayEffect  │
│  (GA: 技能逻辑)  │       │(属性集: HP/MP/Atk│       │(GE: 扣血/Buff/眩晕│
└──────────────────┘       └──────────────────┘       └──────────────────┘
```

- **GameplayTag：** 基于分层标签（如 `State.Debuff.Stun`）的解耦标记，方便判断技能互斥与免疫。

### 4. C++ 与 蓝图 (Blueprints) 混编架构

在商业级 UE 项目中，**绝对不要纯用蓝图，也绝不要纯写 C++**，最佳方案是 **“C++ 做底层骨架，蓝图做上层派生”**。

```
                    ┌─────────────────────────┐
                    │   C++ 基类 (ACharacter) │  ───► 性能敏感逻辑、网络同步、算法、GAS 逻辑
                    └────────────┬────────────┘
                                 │ 继承
                                 ▼
                    ┌─────────────────────────┐
                    │ 蓝图派生类 (BP_Warrior) │  ───► 动画蓝图指定、特效/音效挂载、UI 绑定
                    └─────────────────────────┘
```

#### 反射系统 (UPROPERTY & UFUNCTION) 常用宏解析：

C++

```
UCLASS()
class MYGAME_API AMyCharacter : public ACharacter
{
    GENERATED_BODY()

public:
    // 在编辑器细节面板可任意编辑，蓝图可读取/修改
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Attributes")
    float Health = 100.0f;

    // 网络同步属性：当服务器修改此变量时，自动广播同步给所有客户端
    UPROPERTY(ReplicatedUsing = OnRep_Health)
    float CurrentHealth;

    // 属性同步回调函数
    UFUNCTION()
    void OnRep_Health();

    // 蓝图可调用的 C++ 函数
    UFUNCTION(BlueprintCallable, Category = "Combat")
    void CastSkill();
};
```

# 🛠️ 商业级游戏框架选型与诊断自查

```
                   [ 商业项目框架设计自查 ]
                              │
             ┌────────────────┴────────────────┐
             ▼                                 ▼
      【 Unity 生态诊断 】              【 UE 生态诊断 】
  • 是否存在裸露的 LoadAsset?       • 是否在 ACharacter 里写了
    ➔ 统一接入 Addressables            全场计分板数据?
  • Canvas 是否频繁 Rebatch?          ➔ 移动至 AGameState / APlayerState
    ➔ 动静分离分层控制                 • 是否在 Client 执行权威逻辑?
  • 是否依赖单例导致强耦合?           ➔ 移动至 AGameMode (仅 Server)
    ➔ 引入 UniTask / 依赖注入/事件总线  • 蓝图是否过于庞大臃肿 (Spaghetti)?
                                       ➔ 将核心逻辑重构回 C++ 基类
```
