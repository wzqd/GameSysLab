---
title: ObjectPool
tags: [pooling, reference]
---
# ObjectPool<T>

源码：SysLab/Assets/_Lab/Modules/Pooling/Runtime/ObjectPool.cs。命名空间与程序集 GameSysLab.Modules.Pooling。公共普通类，不挂载。拥有所有创建实例，创建者负责 Dispose。全部操作与查询限创建线程。

## 成员

ActiveCount 为借出数，InactiveCount 为空闲数，IsClosed 为永久关闭状态；均只读。内部 Stack 保存空闲实例，按引用身份的 Dictionary 保存拥有实例和当前租约。busy 防止回调重入。

## 方法

| 方法 | 作用与调用时机 | 参数与返回 | 失败语义 |
| --- | --- | --- | --- |
| 构造函数 | 关卡或业务拥有者创建池 | Func<T> create、Action<T> reset/release 必填；int maxInactive 默认 32，非负 | 空回调 ArgumentNullException；负容量 ArgumentOutOfRangeException |
| Rent() | 使用者需要实例时借出 | 返回独立 PoolLease<T> | 关闭 ObjectDisposedException；工厂空值/重复实例 InvalidOperationException；工厂异常原样传播 |
| Discard(lease) | 适配层发现对象不可再用时淘汰 | PoolLease<T>，无返回；无效租约幂等 | 空值 ArgumentNullException；外来有效租约 ArgumentException；释放异常传播 |
| Dispose() | 拥有者退出时释放全部实例 | 无参数返回；幂等 | 单项错误继续，最后 AggregateException |

内部 Return 先失效租约、删除借出登记，再重置并缓存或释放；重置失败淘汰，双失败汇总。CheckThread / Enter 检查线程和回调重入。池关闭后计数为 0。回调重入修改拒绝，重复关闭可安全返回。

使用流程见 [[Guides/Pooling/游戏中的实例复用]]，验证见 [[Experiments/003_Pooling]]。
