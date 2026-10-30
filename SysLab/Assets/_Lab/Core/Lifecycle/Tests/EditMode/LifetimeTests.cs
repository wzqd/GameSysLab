using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;

namespace GameSysLab.Core.Lifecycle.Tests
{
    public sealed class LifetimeTests
    {
        private sealed class Resource : IDisposable
        {
            private readonly Action action;
            public Resource(Action action) { this.action = action; }
            public void Dispose() => action();
        }

        [Test] public void ReverseOrderAndIdentityDeduplication()
        {
            var calls = new List<int>(); var lifetime = new Lifetime();
            var first = new Resource(() => calls.Add(1));
            Assert.That(lifetime.Add(first), Is.SameAs(first)); lifetime.Add(new Resource(() => calls.Add(2))); lifetime.Add(first);
            lifetime.Dispose(); lifetime.Dispose(); lifetime.Add(first);
            Assert.That(calls, Is.EqualTo(new[] { 2, 1 }));
        }

        [Test] public void CleanupContinuesAfterFailureAndReportsAllErrors()
        {
            var lifetime = new Lifetime(); int calls = 0;
            lifetime.Add(new Resource(() => calls++)); lifetime.Add(new Resource(() => throw new Exception("fail")));
            Assert.That(Assert.Throws<AggregateException>(() => lifetime.Dispose()).InnerExceptions.Count, Is.EqualTo(1));
            Assert.That(calls, Is.EqualTo(1)); Assert.DoesNotThrow(() => lifetime.Dispose());
        }

        [Test] public void AddAfterCloseAndDuringCleanupDisposesImmediately()
        {
            var calls = new List<int>(); var lifetime = new Lifetime();
            var late = new Resource(() => calls.Add(2));
            lifetime.Add(new Resource(() => { calls.Add(1); lifetime.Add(late); lifetime.Add(late); lifetime.Dispose(); }));
            lifetime.Dispose(); lifetime.Add(new Resource(() => calls.Add(3)));
            Assert.That(calls, Is.EqualTo(new[] { 1, 2, 3 }));
        }

        [Test] public void FailingLateCleanupIsReportedAndRemainingResourcesAreReleased()
        {
            var lifetime = new Lifetime(); int calls = 0;
            lifetime.Add(new Resource(() => calls++));
            lifetime.Add(new Resource(() => lifetime.Add(new Resource(() => throw new Exception("late")))));
            Assert.Throws<AggregateException>(() => lifetime.Dispose()); Assert.That(calls, Is.EqualTo(1));
            Assert.Throws<Exception>(() => lifetime.Add(new Resource(() => throw new Exception("closed"))));
        }

        [Test] public void NullAndWrongThreadAreRejected()
        {
            var lifetime = new Lifetime();
            Assert.Throws<ArgumentNullException>(() => lifetime.Add<IDisposable>(null));
            Action[] operations = { () => lifetime.Add(new Resource(() => { })), () => lifetime.Dispose() };
            foreach (var operation in operations)
            {
                var error = Task.Run(() => { try { operation(); return null; } catch (Exception e) { return e; } }).Result;
                Assert.That(error, Is.TypeOf<InvalidOperationException>());
            }
            lifetime.Dispose();
        }
    }
}
