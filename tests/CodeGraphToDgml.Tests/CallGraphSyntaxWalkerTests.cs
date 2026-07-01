using System.Linq;
using System.Threading.Tasks;
using CodeGraphToDgml.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace CodeGraphToDgml.Tests;

[TestClass]
public sealed class CallGraphSyntaxWalkerTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // 1a — method group passed to a delegate-typed parameter
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task FindCalleesAsync_MethodGroupPassedToWaitCallbackParameter_IsDetected()
    {
        const string source = @"
public class Factory
{
    public void Execute(object state) { }
}

public class Worker
{
    private Factory _factory = new Factory();

    public void Run()
    {
        System.Threading.ThreadPool.QueueUserWorkItem(_factory.Execute);
    }
}
";
        var (solution, _) = RoslynTestFixture.CreateSolution(source);
        var callees = await RoslynTestFixture.GetCalleesAsync(solution, "Worker", "Run");

        Assert.IsTrue(callees.Any(c => c.Name == "Execute" && c.ContainingTypeName == "Factory"),
            "Expected Execute to be detected as a deferred call via ThreadPool.QueueUserWorkItem.");
    }

    [TestMethod]
    public async Task FindCalleesAsync_MethodGroupPassedToGenericActionParameter_IsDetected()
    {
        const string source = @"
public class Handler
{
    public void OnDone(int x) { }
}

public static class Runner
{
    public static void Invoke(System.Action<int> callback) { }
}

public class Caller
{
    private Handler _handler = new Handler();

    public void Run()
    {
        Runner.Invoke(_handler.OnDone);
    }
}
";
        var (solution, _) = RoslynTestFixture.CreateSolution(source);
        var callees = await RoslynTestFixture.GetCalleesAsync(solution, "Caller", "Run");

        Assert.IsTrue(callees.Any(c => c.Name == "OnDone" && c.ContainingTypeName == "Handler"),
            "Expected OnDone to be detected as a deferred call via Action<int> parameter.");
    }

    [TestMethod]
    public async Task FindCalleesAsync_MethodGroupPassedAsPlainArgument_IsNotTreatedAsDelegateCall()
    {
        // "GetValue" is passed as a *value* (its result), not a method group — sanity check
        // that only genuinely delegate-typed parameters trigger detection.
        const string source = @"
public class Provider
{
    public int GetValue() => 42;
}

public static class Printer
{
    public static void Show(int value) { }
}

public class Caller
{
    private Provider _provider = new Provider();

    public void Run()
    {
        Printer.Show(_provider.GetValue());
    }
}
";
        var (solution, _) = RoslynTestFixture.CreateSolution(source);
        var callees = await RoslynTestFixture.GetCalleesAsync(solution, "Caller", "Run");

        Assert.IsTrue(callees.Any(c => c.Name == "GetValue"), "GetValue should still be detected as a direct invocation.");
        Assert.IsFalse(callees.Any(c => c.Name == "Show" && c.ContainingTypeName != "Printer"),
            "Show should only appear as the direct call target, not be confused with a delegate redirect.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 1b — event subscription (+=/-=)
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task FindCalleesAsync_EventSubscription_HandlerIsDetected()
    {
        const string source = @"
public class Publisher
{
    public event System.EventHandler Changed;
}

public class Sub
{
    public void OnChanged(object sender, System.EventArgs e) { }

    public void Wire(Publisher p)
    {
        p.Changed += OnChanged;
    }
}
";
        var (solution, _) = RoslynTestFixture.CreateSolution(source);
        var callees = await RoslynTestFixture.GetCalleesAsync(solution, "Sub", "Wire");

        Assert.IsTrue(callees.Any(c => c.Name == "OnChanged"), "Expected OnChanged to be detected via event subscription.");
    }

    [TestMethod]
    public async Task FindCalleesAsync_EventUnsubscription_HandlerIsDetected()
    {
        const string source = @"
public class Publisher
{
    public event System.EventHandler Changed;
}

public class Sub
{
    public void OnChanged(object sender, System.EventArgs e) { }

    public void Unwire(Publisher p)
    {
        p.Changed -= OnChanged;
    }
}
";
        var (solution, _) = RoslynTestFixture.CreateSolution(source);
        var callees = await RoslynTestFixture.GetCalleesAsync(solution, "Sub", "Unwire");

        Assert.IsTrue(callees.Any(c => c.Name == "OnChanged"), "Expected OnChanged to be detected via event unsubscription.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 1c — local delegate variable invoked indirectly (same method body only)
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task FindCalleesAsync_LocalDelegateVariableInvoked_RedirectsToOriginalMethod()
    {
        const string source = @"
public class Handler
{
    public void DoWork() { }
}

public class Caller
{
    private Handler _h = new Handler();

    public void Run()
    {
        System.Action a = _h.DoWork;
        a();
    }
}
";
        var (solution, _) = RoslynTestFixture.CreateSolution(source);
        var callees = await RoslynTestFixture.GetCalleesAsync(solution, "Caller", "Run");

        Assert.IsTrue(callees.Any(c => c.Name == "DoWork" && c.ContainingTypeName == "Handler"),
            "Expected a() to be redirected to Handler.DoWork.");
        Assert.IsFalse(callees.Any(c => c.Name == "Invoke"), "Should not surface the delegate's own Invoke() as a callee.");
    }

    [TestMethod]
    public async Task FindCalleesAsync_FieldDelegateAssignedThenInvoked_RedirectsToOriginalMethod()
    {
        const string source = @"
public class Handler
{
    public void DoWork() { }
}

public class Caller
{
    private Handler _h = new Handler();
    private System.Action _callback;

    public void Run()
    {
        _callback = _h.DoWork;
        _callback();
    }
}
";
        var (solution, _) = RoslynTestFixture.CreateSolution(source);
        var callees = await RoslynTestFixture.GetCalleesAsync(solution, "Caller", "Run");

        Assert.IsTrue(callees.Any(c => c.Name == "DoWork" && c.ContainingTypeName == "Handler"),
            "Expected _callback() to be redirected to Handler.DoWork.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 1g — fluent invocation-return chains (metadata for sequence-diagram nesting only)
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task FindCalleesAsync_FluentInvocationChain_RecordsFluentReceiver()
    {
        const string source = @"
public class Product { }

public class Client
{
    public Product GetProduct(int id) => null;
}

public class Obj1Type
{
    public Client GetClient(int id) => null;
}

public class Caller
{
    private Obj1Type _obj1 = new Obj1Type();

    public void Run()
    {
        _obj1.GetClient(1).GetProduct(1);
    }
}
";
        var (solution, _) = RoslynTestFixture.CreateSolution(source);
        var callees = await RoslynTestFixture.GetCalleesAsync(solution, "Caller", "Run");

        var getClient = callees.SingleOrDefault(c => c.Name == "GetClient");
        var getProduct = callees.SingleOrDefault(c => c.Name == "GetProduct");

        Assert.IsNotNull(getClient, "GetClient should still be a direct callee of Run (DGML edge unaffected).");
        Assert.IsNotNull(getProduct, "GetProduct should still be a direct callee of Run (DGML edge unaffected).");
        Assert.IsNull(getClient!.FluentReceiverName, "GetClient is the start of the chain — no fluent receiver.");
        Assert.AreEqual("GetClient", getProduct!.FluentReceiverName,
            "GetProduct should record GetClient as its fluent receiver for sequence-diagram nesting.");
    }

    [TestMethod]
    public async Task FindCalleesAsync_NonChainedCalls_NoFluentReceiverRecorded()
    {
        const string source = @"
public class A { public void M1() { } }
public class B { public void M2() { } }

public class Caller
{
    private A _a = new A();
    private B _b = new B();

    public void Run()
    {
        _a.M1();
        _b.M2();
    }
}
";
        var (solution, _) = RoslynTestFixture.CreateSolution(source);
        var callees = await RoslynTestFixture.GetCalleesAsync(solution, "Caller", "Run");

        Assert.IsTrue(callees.All(c => c.FluentReceiverName is null),
            "Two independent statements should not be linked as a fluent chain.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 1e — empirical check of SymbolFinder.FindCallersAsync coverage (Traverse Up)
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task FindCallersAsync_MethodGroupArgument_IsSurfacedAsCaller()
    {
        const string source = @"
public class Factory
{
    public void Execute(object state) { }
}

public class Worker
{
    private Factory _factory = new Factory();

    public void Run()
    {
        System.Threading.ThreadPool.QueueUserWorkItem(_factory.Execute);
    }
}
";
        var (solution, _) = RoslynTestFixture.CreateSolution(source);
        var target = await RoslynTestFixture.GetMethodSymbolAsync(solution, "Factory", "Execute");

        var callers = await SymbolFinder.FindCallersAsync(target, solution);

        Assert.IsTrue(callers.Any(c => c.CallingSymbol.Name == "Run"),
            "SymbolFinder.FindCallersAsync should surface Run as a caller of Execute via the method-group argument " +
            "(FindReferencesAsync finds all syntactic symbol bindings, not just direct invocations).");
    }

    [TestMethod]
    public async Task FindCallersAsync_EventSubscription_IsSurfacedAsCaller()
    {
        const string source = @"
public class Publisher
{
    public event System.EventHandler Changed;
}

public class Sub
{
    public void OnChanged(object sender, System.EventArgs e) { }

    public void Wire(Publisher p)
    {
        p.Changed += OnChanged;
    }
}
";
        var (solution, _) = RoslynTestFixture.CreateSolution(source);
        var target = await RoslynTestFixture.GetMethodSymbolAsync(solution, "Sub", "OnChanged");

        var callers = await SymbolFinder.FindCallersAsync(target, solution);

        Assert.IsTrue(callers.Any(c => c.CallingSymbol.Name == "Wire"),
            "SymbolFinder.FindCallersAsync should surface Wire as a caller of OnChanged via the event subscription.");
    }
}
