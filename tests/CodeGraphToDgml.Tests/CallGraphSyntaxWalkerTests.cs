using System.Linq;
using System.Threading.Tasks;
using CodeGraphToDgml.Core;
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

    // ──────────────────────────────────────────────────────────────────────────
    // Items 1-2 — delegate-creation-wrapped and cast-wrapped method groups
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task FindCalleesAsync_ExplicitDelegateCreationInEventSubscription_HandlerIsDetected()
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
        p.Changed += new System.EventHandler(OnChanged);
    }
}
";
        var (solution, _) = RoslynTestFixture.CreateSolution(source);
        var callees = await RoslynTestFixture.GetCalleesAsync(solution, "Sub", "Wire");

        Assert.IsTrue(callees.Any(c => c.Name == "OnChanged"),
            "Expected OnChanged wrapped in `new EventHandler(...)` to be detected (classic WebForms/WinForms wiring style).");
    }

    [TestMethod]
    public async Task FindCalleesAsync_ExplicitDelegateCreationAsArgument_IsDetected()
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
        System.Threading.ThreadPool.QueueUserWorkItem(new System.Threading.WaitCallback(_factory.Execute));
    }
}
";
        var (solution, _) = RoslynTestFixture.CreateSolution(source);
        var callees = await RoslynTestFixture.GetCalleesAsync(solution, "Worker", "Run");

        Assert.IsTrue(callees.Any(c => c.Name == "Execute" && c.ContainingTypeName == "Factory"),
            "Expected Execute wrapped in `new WaitCallback(...)` to be detected as a deferred call.");
    }

    [TestMethod]
    public async Task FindCalleesAsync_CastWrappedMethodGroupArgument_IsDetected()
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
        Runner.Invoke((System.Action<int>)_handler.OnDone);
    }
}
";
        var (solution, _) = RoslynTestFixture.CreateSolution(source);
        var callees = await RoslynTestFixture.GetCalleesAsync(solution, "Caller", "Run");

        Assert.IsTrue(callees.Any(c => c.Name == "OnDone" && c.ContainingTypeName == "Handler"),
            "Expected OnDone wrapped in a delegate cast to be detected as a deferred call.");
    }

    [TestMethod]
    public async Task FindCalleesAsync_ExplicitDelegateCreationAssignedToVariableThenInvoked_Redirects()
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
        System.Action a = new System.Action(_h.DoWork);
        a();
    }
}
";
        var (solution, _) = RoslynTestFixture.CreateSolution(source);
        var callees = await RoslynTestFixture.GetCalleesAsync(solution, "Caller", "Run");

        Assert.IsTrue(callees.Any(c => c.Name == "DoWork" && c.ContainingTypeName == "Handler"),
            "Expected a() to be redirected to DoWork through the explicit delegate creation.");
        Assert.IsFalse(callees.Any(c => c.Name == "Invoke"), "Should not surface the delegate's own Invoke() as a callee.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Item 3 — constructor calls and : this/base(...) chaining
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task FindCalleesAsync_ObjectCreation_ConstructorIsDetected()
    {
        const string source = @"
public class Service
{
    public Service(int x) { }
    public void M() { }
}

public class Caller
{
    public void Run()
    {
        var s = new Service(1);
        s.M();
    }
}
";
        var (solution, _) = RoslynTestFixture.CreateSolution(source);
        var callees = await RoslynTestFixture.GetCalleesAsync(solution, "Caller", "Run");

        Assert.IsTrue(callees.Any(c => c.Name == ".ctor" && c.ContainingTypeName == "Service"),
            "Expected `new Service(1)` to surface the Service constructor as a callee.");
        Assert.IsTrue(callees.Any(c => c.Name == "M"), "Direct method calls must still be detected alongside the constructor.");
    }

    [TestMethod]
    public async Task FindCalleesAsync_TargetTypedNew_ConstructorIsDetected()
    {
        const string source = @"
public class Service
{
    public Service(int x) { }
}

public class Caller
{
    public void Run()
    {
        Service s = new(1);
    }
}
";
        var (solution, _) = RoslynTestFixture.CreateSolution(source);
        var callees = await RoslynTestFixture.GetCalleesAsync(solution, "Caller", "Run");

        Assert.IsTrue(callees.Any(c => c.Name == ".ctor" && c.ContainingTypeName == "Service"),
            "Expected target-typed `new(1)` to surface the Service constructor as a callee.");
    }

    [TestMethod]
    public async Task FindCalleesAsync_ThisConstructorChaining_IsDetected()
    {
        const string source = @"
public class Chained
{
    public Chained() : this(1) { }
    public Chained(int x) { }
}
";
        var (solution, _) = RoslynTestFixture.CreateSolution(source);
        var callees = await RoslynTestFixture.GetCalleesAsync(solution, "Chained", ".ctor");

        Assert.IsTrue(callees.Any(c => c.Name == ".ctor" && c.ContainingTypeName == "Chained"),
            "Expected `: this(1)` to surface the chained constructor overload as a callee.");
    }

    [TestMethod]
    public async Task FindCalleesAsync_BaseConstructorChaining_IsDetected()
    {
        const string source = @"
public class BaseType
{
    public BaseType(int x) { }
}

public class DerivedType : BaseType
{
    public DerivedType() : base(5) { }
}
";
        var (solution, _) = RoslynTestFixture.CreateSolution(source);
        var callees = await RoslynTestFixture.GetCalleesAsync(solution, "DerivedType", ".ctor");

        Assert.IsTrue(callees.Any(c => c.Name == ".ctor" && c.ContainingTypeName == "BaseType"),
            "Expected `: base(5)` to surface the base constructor as a callee.");
    }

    [TestMethod]
    public async Task FindCalleesAsync_MethodGroupPassedToConstructor_IsDetected()
    {
        const string source = @"
public class Caller
{
    public void Run()
    {
        var t = new System.Threading.Thread(Work);
        var t2 = new System.Threading.Thread(new System.Threading.ThreadStart(Work2));
    }

    public void Work() { }
    public void Work2() { }
}
";
        var (solution, _) = RoslynTestFixture.CreateSolution(source);
        var callees = await RoslynTestFixture.GetCalleesAsync(solution, "Caller", "Run");

        Assert.IsTrue(callees.Any(c => c.Name == "Work"),
            "Expected the bare method group passed to `new Thread(...)` to be detected as a deferred call.");
        Assert.IsTrue(callees.Any(c => c.Name == "Work2"),
            "Expected the `new ThreadStart(...)`-wrapped method group passed to `new Thread(...)` to be detected as a deferred call.");
    }

    [TestMethod]
    public async Task CallGraphFilters_IncludeConstructorsOff_FiltersConstructorCallees()
    {
        const string source = @"
public class Service
{
    public Service(int x) { }
}
";
        var (solution, _) = RoslynTestFixture.CreateSolution(source);
        var ctor = await RoslynTestFixture.GetMethodSymbolAsync(solution, "Service", ".ctor");

        Assert.IsTrue(CallGraphFilters.IsAllowed(ctor, new TraversalOptions()),
            "Constructors must be included by default.");
        Assert.IsFalse(CallGraphFilters.IsAllowed(ctor, new TraversalOptions { IncludeConstructors = false }),
            "IncludeConstructors=false must filter constructor callees.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Item 4 — event raises
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task FindCalleesAsync_ConditionalEventRaise_MapsToEventSymbol()
    {
        const string source = @"
public class Publisher
{
    public event System.EventHandler Changed;

    public void Raise()
    {
        Changed?.Invoke(this, System.EventArgs.Empty);
    }
}
";
        var (solution, _) = RoslynTestFixture.CreateSolution(source);
        var callees = await RoslynTestFixture.GetCalleesAsync(solution, "Publisher", "Raise");

        Assert.IsTrue(callees.Any(c => c.Name == "Changed"),
            "Expected `Changed?.Invoke(...)` to map the raise to the Changed event symbol.");
        Assert.IsFalse(callees.Any(c => c.Name == "Invoke"),
            "The delegate's own Invoke() must not appear once the raise maps to the event.");
    }

    [TestMethod]
    public async Task FindCalleesAsync_DirectEventRaise_MapsToEventSymbol()
    {
        const string source = @"
public class Publisher
{
    public event System.EventHandler Changed;

    public void Raise()
    {
        Changed(this, System.EventArgs.Empty);
    }
}
";
        var (solution, _) = RoslynTestFixture.CreateSolution(source);
        var callees = await RoslynTestFixture.GetCalleesAsync(solution, "Publisher", "Raise");

        Assert.IsTrue(callees.Any(c => c.Name == "Changed"),
            "Expected `Changed(...)` to map the raise to the Changed event symbol.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Item 5 — BeginInvoke/DynamicInvoke on delegates, delegate variables as arguments
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task FindCalleesAsync_DelegateBeginInvoke_RedirectsToOriginalMethod()
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
        a.BeginInvoke(null, null);
    }
}
";
        var (solution, _) = RoslynTestFixture.CreateSolution(source);
        var callees = await RoslynTestFixture.GetCalleesAsync(solution, "Caller", "Run");

        Assert.IsTrue(callees.Any(c => c.Name == "DoWork" && c.ContainingTypeName == "Handler"),
            "Expected a.BeginInvoke(...) to be redirected to Handler.DoWork.");
        Assert.IsFalse(callees.Any(c => c.Name == "BeginInvoke"),
            "The delegate's own BeginInvoke must not appear once redirected.");
    }

    [TestMethod]
    public async Task FindCalleesAsync_DelegateDynamicInvoke_RedirectsToOriginalMethod()
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
        a.DynamicInvoke();
    }
}
";
        var (solution, _) = RoslynTestFixture.CreateSolution(source);
        var callees = await RoslynTestFixture.GetCalleesAsync(solution, "Caller", "Run");

        Assert.IsTrue(callees.Any(c => c.Name == "DoWork" && c.ContainingTypeName == "Handler"),
            "Expected a.DynamicInvoke() to be redirected to Handler.DoWork.");
    }

    [TestMethod]
    public async Task FindCalleesAsync_NonDelegateBeginInvoke_IsNotHijacked()
    {
        const string source = @"
public class Dispatcher
{
    public void BeginInvoke(System.Action action) { }
}

public class Handler
{
    public void DoWork() { }
}

public class Caller
{
    private Dispatcher _d = new Dispatcher();
    private Handler _h = new Handler();

    public void Run()
    {
        _d.BeginInvoke(_h.DoWork);
    }
}
";
        var (solution, _) = RoslynTestFixture.CreateSolution(source);
        var callees = await RoslynTestFixture.GetCalleesAsync(solution, "Caller", "Run");

        Assert.IsTrue(callees.Any(c => c.Name == "BeginInvoke" && c.ContainingTypeName == "Dispatcher"),
            "A regular method named BeginInvoke (non-delegate receiver) must remain a direct callee.");
        Assert.IsTrue(callees.Any(c => c.Name == "DoWork"),
            "The method group argument must still be detected as a deferred call.");
    }

    [TestMethod]
    public async Task FindCalleesAsync_TrackedDelegateVariablePassedAsArgument_RedirectsToOriginalMethod()
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
        System.Action<int> a = _handler.OnDone;
        Runner.Invoke(a);
    }
}
";
        var (solution, _) = RoslynTestFixture.CreateSolution(source);
        var callees = await RoslynTestFixture.GetCalleesAsync(solution, "Caller", "Run");

        Assert.IsTrue(callees.Any(c => c.Name == "OnDone" && c.ContainingTypeName == "Handler"),
            "Expected the tracked delegate variable passed to a delegate-typed parameter to redirect to OnDone.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Item 6 — property writes and indexer accesses
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task FindCalleesAsync_PropertyWrite_SetterIsDetected()
    {
        const string source = @"
public class Model
{
    public int Value { get; set; }
}

public class Caller
{
    public void Run(Model m)
    {
        m.Value = 5;
    }
}
";
        var (solution, _) = RoslynTestFixture.CreateSolution(source);
        var callees = await RoslynTestFixture.GetCalleesAsync(solution, "Caller", "Run");

        Assert.IsTrue(callees.Any(c => c.Name == "Value" && c.ContainingTypeName == "Model"),
            "Expected the property write to surface the Value property as a callee.");
    }

    [TestMethod]
    public async Task FindCalleesAsync_StandalonePropertyRead_IsNotDetected()
    {
        const string source = @"
public class Model
{
    public int Value { get; set; }
}

public class Caller
{
    public void Run(Model m)
    {
        var v = m.Value;
    }
}
";
        var (solution, _) = RoslynTestFixture.CreateSolution(source);
        var callees = await RoslynTestFixture.GetCalleesAsync(solution, "Caller", "Run");

        Assert.IsFalse(callees.Any(c => c.Name == "Value"),
            "Standalone property reads are deliberately excluded (only writes and reads feeding invocation chains count).");
    }

    [TestMethod]
    public async Task FindCalleesAsync_IndexerAccess_IsDetected()
    {
        const string source = @"
public class Store
{
    public int this[int i] => 0;
}

public class Caller
{
    public void Run(Store s, int[] arr)
    {
        var x = s[0];
        var y = arr[0];
    }
}
";
        var (solution, _) = RoslynTestFixture.CreateSolution(source);
        var callees = await RoslynTestFixture.GetCalleesAsync(solution, "Caller", "Run");

        Assert.IsTrue(callees.Any(c => c.Name == "this[]" && c.ContainingTypeName == "Store"),
            "Expected the custom indexer access to surface the indexer property as a callee.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Item 7 — object and collection initializers
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task FindCalleesAsync_ObjectInitializer_SetterAndConstructorAreDetected()
    {
        const string source = @"
public class Widget
{
    public int Size { get; set; }
}

public class Caller
{
    public void Run()
    {
        var w = new Widget { Size = 10 };
    }
}
";
        var (solution, _) = RoslynTestFixture.CreateSolution(source);
        var callees = await RoslynTestFixture.GetCalleesAsync(solution, "Caller", "Run");

        Assert.IsTrue(callees.Any(c => c.Name == ".ctor" && c.ContainingTypeName == "Widget"),
            "Expected the implicit Widget constructor to be detected.");
        Assert.IsTrue(callees.Any(c => c.Name == "Size" && c.ContainingTypeName == "Widget"),
            "Expected the object-initializer assignment to surface the Size property setter.");
    }

    [TestMethod]
    public async Task FindCalleesAsync_CollectionInitializer_AddCallsAreDetected()
    {
        const string source = @"
public class Bag : System.Collections.IEnumerable
{
    public void Add(int item) { }
    public System.Collections.IEnumerator GetEnumerator() => null;
}

public class Caller
{
    public void Run()
    {
        var b = new Bag { 1, 2 };
    }
}
";
        var (solution, _) = RoslynTestFixture.CreateSolution(source);
        var callees = await RoslynTestFixture.GetCalleesAsync(solution, "Caller", "Run");

        Assert.IsTrue(callees.Any(c => c.Name == "Add" && c.ContainingTypeName == "Bag"),
            "Expected collection-initializer elements to surface Bag.Add as a callee.");
        Assert.IsFalse(callees.Any(c => c.Name == "GetEnumerator"),
            "GetEnumerator is not called by a collection initializer.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Item 12 — extension-method identity (reduced vs unreduced form)
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task FindCalleesAsync_ExtensionMethodCalledBothStyles_DedupedToSingleCallee()
    {
        const string source = @"
public static class Extensions
{
    public static void Ext(this string s) { }
}

public class Caller
{
    public void Run(string s)
    {
        s.Ext();
        Extensions.Ext(s);
    }
}
";
        var (solution, _) = RoslynTestFixture.CreateSolution(source);
        var callees = await RoslynTestFixture.GetCalleesAsync(solution, "Caller", "Run");

        Assert.AreEqual(1, callees.Count(c => c.Name == "Ext"),
            "Instance-style and static-style calls to the same extension method must normalize to one callee.");
    }
}
