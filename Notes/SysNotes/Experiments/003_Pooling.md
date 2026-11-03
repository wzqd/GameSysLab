---
title: Pooling 对象复用与所有权
status: implemented
stage: minimal
tags: [pooling, lifecycle]
---
# Pooling 对象复用与所有权

实现 ObjectPool<T>、独立引用租约 PoolLease<T> 和 GameObjectPoolHost。Runtime 不引用 Core、Timer 或 Unity。池拥有实例，外部拥有资源。归还先使租约失效，再重置；空闲超限释放。关闭清理空闲和借出对象，错误汇总后报告。

每次借出创建独立租约，旧租约不能干扰新借用。缓存上限默认 32，可为 0，不限制借出总数。外部保存的裸引用无法撤销，归还后不得继续使用。所有 Runtime 操作与查询限创建线程，回调禁止重入修改同池。

Unity 适配增加 Discard(lease) 淘汰已失效实例：绕过重置并释放；外来有效租约拒绝。Unity 被外部销毁的实例在借出或归还后续使用时处理，不实时更新计数。Host 仅在 Unity 主线程调用。

## 验证

2026-10-30 Windows Unity 6000.6.0f1 编译通过，全项目 EditMode 61/61，其中 Pooling 新增 6 项。全项目 PlayMode 30/30 通过，其中 Pooling 新增 1 项。不声称性能、Player、IL2CPP、长期压力已验证。

## 后续候选

引用租约每次借出会分配；值类型租约和代次校验只作为后续优化候选，须处理复制、默认值和失效语义。尚未完成性能测量、完整故障矩阵及演示视觉验收。

[[Reference/Pooling/ObjectPool]] · [[Reference/Pooling/PoolLease]] · [[Reference/Pooling/GameObjectPoolHost]] · [[Guides/Pooling/游戏中的实例复用]]

本轮全项目 PlayMode 30/30 通过，其中 Pooling 新增 1 项；覆盖禁用实例、复用、外部销毁缓存和关闭释放。完整场景卸载重进与性能验证尚待补齐。
