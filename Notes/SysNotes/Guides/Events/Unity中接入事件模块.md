---
title: 在 Unity 中接入事件模块
updated: 2026-09-27
tags: [guide, events, unity]
---

# 在 Unity 中接入事件模块

目标：关卡里有一条公共的事件总线。角色用它通知生命值变化，血条监听同一条总线；退出关卡时清理。这里需要你自己的关卡启动脚本，不需要任何演示工程的入口类。

## 哪个脚本要挂到物体上

| 类型 | 要不要挂载 | 谁创建 / 使用 |
| --- | --- | --- |
| `EventBusHost` | 要，挂在关卡的常驻对象上 | 关卡启动代码持有它的明确引用 |
| `EventBus` | 不挂载，是普通 C# 对象 | Host.Initialize 创建，角色和血条借用 |
| `Lifetime` | 不挂载，是普通 C# 对象 | 每个需要统一清理的对象自己创建 |

为使用这些类型的程序集添加需要的引用：`GameSysLab.Core.Events`、`GameSysLab.Core.Events.Unity`；管理订阅时再引用 `GameSysLab.Core.Lifecycle`。

## 在 Inspector 中配置

1. 在关卡场景创建一个名为“关卡事件”的 GameObject，添加 EventBusHost。
2. 你的关卡启动脚本保存 `[SerializeField] private EventBusHost levelEventHost;`，把“关卡事件”上的组件拖到这个字段。
3. 在启动代码里调用 `EventBus levelEvents = levelEventHost.Initialize();`，再把 levelEvents 传给角色、血条等使用者。

Host 不会在 Awake 自动创建总线。应先 Initialize，再让业务对象开始订阅或发通知。组件可能在你的启动代码前就收到 OnEnable，所以组件未获得依赖时应该等待配置，不在 OnEnable 里自行找总线。

| Host 参数 | 默认值 | 调整后有什么影响 |
| --- | --- | --- |
| Queue Capacity | 4096 条 | 最多能排队多少事件；初始化时固定，改 Inspector 不会给已有总线扩容 |
| Events Per Frame | 256 条 | 每次 Update 最多处理多少已排队事件；运行时修改，下次 Update 生效 |

只使用 Publish 的游戏逻辑通常不需要调整队列参数。这两个数都至少为 1；每帧条数不等于毫秒预算，一个慢监听仍会卡住主线程。

## 把总线交给角色和血条

普通 C# 角色类通过构造参数接收 EventBus，保存在字段里。MonoBehaviour 血条可以提供 `BindEvents(EventBus events)` 方法，由关卡启动脚本调用。BindEvents 是业务组件自己编写的方法，不是 Core 内置 API。

```csharp
// 以下放在你自己的关卡启动脚本中。
// levelEventHost 是 Inspector 引用；角色构造函数和血条绑定方法由业务实现。
EventBus levelEvents = levelEventHost.Initialize();
// var player = new PlayerCharacter(levelEvents);
// healthBar.BindEvents(levelEvents);
```

传同一个实例就能通信。没有静态 Instance，不需要 Find，也不用把整个游戏入口对象传给角色。血条完整的订阅启停写法见 [[Guides/Lifecycle/界面关闭时清理订阅]]。

## 禁用和离开关卡分别做什么

- 血条关闭：释放血条自己的订阅。其他对象仍可继续收事件。
- Host 被禁用：暂停每帧处理排队事件；总线仍存在，Publish 仍立即通知，后台仍可能把队列填满。
- 退出关卡：启动代码先停止新业务与后台任务，再清理角色、血条的订阅，最后调用 `levelEventHost.Shutdown()`。

Host 的 OnDestroy 会兜底关闭总线，但业务清理应明确完成，不依赖多个物体的销毁顺序。不要对借用的 `levelEventHost.Bus` 直接 Dispose；由拥有这个 Host 的启动代码调用 Shutdown。Shutdown 之后这个 Host 不能重新初始化，重新进入关卡需要新 Host。

## 跨关卡通知用哪一条总线

角色受伤属于当前关卡，放在关卡总线。语言变化或账号状态变化可能需要贯穿整局游戏，可由游戏启动代码创建另一条更长寿的总线，只交给确实需要它的对象。

两条总线都是 EventBus，不需要另写 GlobalEventBus / SceneEventBus。关键是由谁持有、什么时候关闭。场景血条即使监听全局通知，退出时也必须清理自己的监听。详细归属见 [[Architecture/002_游戏与关卡的服务归属]]。

## 常见问题

| 现象 | 先检查什么 |
| --- | --- |
| 血条收不到消息 | 两边是否用同一个总线、同一个泛型事件类型；血条是否已绑定并启用 |
| 打开血条没有显示当前血量 | 事件不保存状态，也不重放；打开时先读取角色当前血量 |
| 访问 Bus 抛错 | Host 是否先 Initialize，或者已经 Shutdown |
| 切关卡后旧血条还收到通知 | 是否把全局订阅放进自己的 Lifetime，并在退出时释放 |
| 排队事件不处理 | Host 是否初始化并启用；是否误以为 TryQueueEvent 会自动执行监听 |

全部方法、参数与错误条件见 [[Reference/Events/EventBusHost]] 和 [[Reference/Events/EventBus]]。
