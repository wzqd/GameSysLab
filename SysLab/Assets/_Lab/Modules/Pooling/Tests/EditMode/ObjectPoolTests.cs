using System;
using NUnit.Framework;

namespace GameSysLab.Modules.Pooling.Tests
{
    public class ObjectPoolTests
    {
        private sealed class Item { public int Value; }
        [Test] public void ReuseResetsAndOldLeaseCannotReturnNewBorrow()
        {
            using (var pool = new ObjectPool<Item>(() => new Item(), x => x.Value = 0, x => { }))
            {
                var old = pool.Rent(); var item = old.Value; item.Value = 7; old.Dispose();
                var next = pool.Rent(); old.Dispose();
                Assert.AreSame(item, next.Value); Assert.AreEqual(0, next.Value.Value);
                Assert.AreEqual(1, pool.ActiveCount); Assert.IsFalse(old.IsValid);
                Assert.Throws<ObjectDisposedException>(() => { var unused = old.Value; });
            }
        }
        [TestCase(0)] [TestCase(1)] public void CapacityAndShutdownReleaseExactlyOnce(int capacity)
        {
            int released = 0;
            var pool = new ObjectPool<Item>(() => new Item(), x => { }, x => released++, capacity);
            var first = pool.Rent(); var second = pool.Rent(); first.Dispose(); second.Dispose();
            Assert.AreEqual(capacity, pool.InactiveCount); Assert.AreEqual(2 - capacity, released);
            var active = pool.Rent(); pool.Dispose(); pool.Dispose(); active.Dispose();
            Assert.IsFalse(active.IsValid); Assert.AreEqual(0, pool.ActiveCount);
            Assert.Throws<ObjectDisposedException>(() => pool.Rent());
            Assert.AreEqual(capacity == 0 ? 3 : 2, released);
        }
        [Test] public void ResetAndReleaseFailuresKeepBothErrorsAndRemoveObject()
        {
            var pool = new ObjectPool<Item>(() => new Item(), x => throw new Exception("reset"), x => throw new Exception("release"));
            var lease = pool.Rent();
            Assert.AreEqual(2, Assert.Throws<AggregateException>(() => lease.Dispose()).InnerExceptions.Count);
            Assert.IsFalse(lease.IsValid); Assert.AreEqual(0, pool.ActiveCount); pool.Dispose();
        }
        [Test] public void ShutdownContinuesAfterFailure()
        {
            int released = 0;
            var pool = new ObjectPool<Item>(() => new Item(), x => { }, x => { released++; throw new Exception(); });
            var a = pool.Rent(); var b = pool.Rent(); b.Dispose();
            Assert.AreEqual(2, Assert.Throws<AggregateException>(() => pool.Dispose()).InnerExceptions.Count);
            Assert.AreEqual(2, released); Assert.IsFalse(a.IsValid); Assert.AreEqual(0, pool.InactiveCount);
        }
        [Test] public void DuplicateFactoryAndReentryDoNotCorruptExistingBorrow()
        {
            var item = new Item();
            ObjectPool<Item> pool = null;
            pool = new ObjectPool<Item>(() => item, x => Assert.Throws<InvalidOperationException>(() => pool.Rent()), x => { });
            var lease = pool.Rent(); Assert.Throws<InvalidOperationException>(() => pool.Rent());
            Assert.AreSame(item, lease.Value); lease.Dispose(); pool.Dispose();
        }
    }
}
