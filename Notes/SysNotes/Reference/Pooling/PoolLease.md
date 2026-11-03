---
title: PoolLease
tags: [pooling, reference]
---
# PoolLease<T>

源码：SysLab/Assets/_Lab/Modules/Pooling/Runtime/PoolLease.cs。命名空间与程序集 GameSysLab.Modules.Pooling。公共普通类，不挂载。池内部构造，每次借出新建，不复用；使用者负责 Dispose，不拥有池。

## 成员与方法

| 成员 | 作用与调用时机 | 返回与失败 |
| --- | --- | --- |
| Value | 借用有效时访问实例 | T；失效抛 ObjectDisposedException |
| IsValid | 操作前检查借用状态 | bool，只读 |
| Dispose() | 使用结束归还 | 无参数返回，幂等；转发池的重置与释放错误 |

全部公开调用限创建线程，错误线程 InvalidOperationException。内部构造保存 owner/value/threadId；Invalidate 清除 owner/value，避免持有旧资源；CheckThread 负责线程校验。外部保存的裸引用不会被撤销。引用租约会分配，不声称零分配。

[[Reference/Pooling/ObjectPool]] · [[Guides/Pooling/游戏中的实例复用]] · [[Experiments/003_Pooling]]
