using System.Linq;
using System.Threading.Tasks;
using CodeGraphToDgml.Roslyn;
using Microsoft.CodeAnalysis;

namespace CodeGraphToDgml.Tests;

[TestClass]
public sealed class CallGraphDispatchTests
{
    private const string HierarchySource = @"
namespace App
{
    public interface IWorker { void Run(); }

    public abstract class Base
    {
        public virtual void Foo() { }
        public abstract void Bar();
        public void Plain() { }
        public virtual void Sealed() { }
    }

    public class Derived : Base
    {
        public override void Foo() { }
        public override void Bar() { }
        public sealed override void Sealed() { }
    }

    public class DerivedAgain : Derived
    {
        public override void Foo() { }
    }

    public class Unrelated : Base
    {
        public override void Bar() { }
    }

    public class WorkerA : IWorker { public void Run() { } }
    public class WorkerB : IWorker { public void Run() { } }
}
";

    [TestMethod]
    public async Task FindOverridesAsync_VirtualMember_ReturnsDirectAndIndirectOverridesOrdered()
    {
        var (solution, _) = RoslynTestFixture.CreateSolution(HierarchySource);
        var foo = await RoslynTestFixture.GetMethodSymbolAsync(solution, "App.Base", "Foo");

        var overrides = await CallGraphDispatch.FindOverridesAsync(foo, solution, default);

        CollectionAssert.AreEqual(
            new[] { "App.Derived.Foo()", "App.DerivedAgain.Foo()" },
            overrides.Select(o => o.ToDisplayString()).ToArray());
    }

    [TestMethod]
    public async Task FindOverridesAsync_AbstractMember_ReturnsOverridesAndExcludesSelf()
    {
        var (solution, _) = RoslynTestFixture.CreateSolution(HierarchySource);
        var bar = await RoslynTestFixture.GetMethodSymbolAsync(solution, "App.Base", "Bar");

        var overrides = await CallGraphDispatch.FindOverridesAsync(bar, solution, default);

        CollectionAssert.AreEqual(
            new[] { "App.Derived.Bar()", "App.Unrelated.Bar()" },
            overrides.Select(o => o.ToDisplayString()).ToArray());
    }

    [TestMethod]
    public async Task FindOverridesAsync_IntermediateOverride_ReturnsOnlyDeeperOverrides()
    {
        var (solution, _) = RoslynTestFixture.CreateSolution(HierarchySource);
        var derivedFoo = await RoslynTestFixture.GetMethodSymbolAsync(solution, "App.Derived", "Foo");

        var overrides = await CallGraphDispatch.FindOverridesAsync(derivedFoo, solution, default);

        CollectionAssert.AreEqual(
            new[] { "App.DerivedAgain.Foo()" },
            overrides.Select(o => o.ToDisplayString()).ToArray());
    }

    [TestMethod]
    public async Task CanBeOverridden_ClassifiesMembersByModifiers()
    {
        var (solution, _) = RoslynTestFixture.CreateSolution(HierarchySource);

        Assert.IsTrue(CallGraphDispatch.CanBeOverridden(await RoslynTestFixture.GetMethodSymbolAsync(solution, "App.Base", "Foo")), "virtual");
        Assert.IsTrue(CallGraphDispatch.CanBeOverridden(await RoslynTestFixture.GetMethodSymbolAsync(solution, "App.Base", "Bar")), "abstract");
        Assert.IsTrue(CallGraphDispatch.CanBeOverridden(await RoslynTestFixture.GetMethodSymbolAsync(solution, "App.Derived", "Foo")), "override");
        Assert.IsFalse(CallGraphDispatch.CanBeOverridden(await RoslynTestFixture.GetMethodSymbolAsync(solution, "App.Base", "Plain")), "non-virtual");
        Assert.IsFalse(CallGraphDispatch.CanBeOverridden(await RoslynTestFixture.GetMethodSymbolAsync(solution, "App.Derived", "Sealed")), "sealed override");
        Assert.IsFalse(CallGraphDispatch.CanBeOverridden(await RoslynTestFixture.GetMethodSymbolAsync(solution, "App.IWorker", "Run")), "interface member");
    }

    [TestMethod]
    public async Task FindDispatchTargetsAsync_InterfaceMember_ReturnsImplementations()
    {
        var (solution, _) = RoslynTestFixture.CreateSolution(HierarchySource);
        var run = await RoslynTestFixture.GetMethodSymbolAsync(solution, "App.IWorker", "Run");

        var targets = await CallGraphDispatch.FindDispatchTargetsAsync(run, solution, default);

        CollectionAssert.AreEqual(
            new[] { "App.WorkerA.Run()", "App.WorkerB.Run()" },
            targets.Select(t => t.ToDisplayString()).ToArray());
    }

    [TestMethod]
    public async Task FindDispatchTargetsAsync_NonVirtualMember_ReturnsEmpty()
    {
        var (solution, _) = RoslynTestFixture.CreateSolution(HierarchySource);
        var plain = await RoslynTestFixture.GetMethodSymbolAsync(solution, "App.Base", "Plain");

        var targets = await CallGraphDispatch.FindDispatchTargetsAsync(plain, solution, default);

        Assert.IsEmpty(targets);
    }
}
