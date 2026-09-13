using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CodeGraphToDgml.Core;
using CodeGraphToDgml.Roslyn;

namespace CodeGraphToDgml.Tests;

/// <summary>
/// <see cref="CallGraphSyntaxWalker.FindCallSitesAsync"/>: C# control-flow context per call site.
/// Each site renders as "Callee[path]" where path is "kind#instance.section:label" per level.
/// </summary>
[TestClass]
public sealed class CallSiteFlowContextTests
{
    private static async Task<IReadOnlyList<CallSiteInfo>> SitesAsync(string body, string helpers = "", TraversalOptions? options = null, string className = "Worker")
    {
        var source = @"
using System;
using System.Collections.Generic;
using System.Linq;

public class Svc
{
    public void A() { }
    public void B() { }
    public void C() { }
    public bool Check() => true;
    public bool Check2() => true;
    public int Count() => 1;
    public IEnumerable<int> Items() => new int[0];
    public Svc Self() => this;
}
" + helpers + @"
public class " + className + @"
{
    private readonly Svc _svc = new Svc();

    public void Run(int x, string s, object o, bool flag, System.Collections.Generic.List<int> list)
    {
" + body + @"
    }
}
";
        var (solution, _) = RoslynTestFixture.CreateSolution(source);
        var method = await RoslynTestFixture.GetMethodSymbolAsync(solution, className, "Run");
        return await CallGraphSyntaxWalker.FindCallSitesAsync(method, solution, options ?? new TraversalOptions(), default);
    }

    private static string Render(IEnumerable<CallSiteInfo> sites)
    {
        static string Scope(CallSequenceFragmentScope s)
            => s.Kind.ToString().ToLowerInvariant() + "#" + s.InstanceId + "." + s.SectionIndex + ":" + s.Label;

        return string.Join("\n", sites.Select(site => site.Symbol.Name + "[" + string.Join(" > ", site.FragmentPath.Select(Scope)) + "]"));
    }

    [TestMethod]
    public async Task StraightLineCode_HasEmptyPaths()
    {
        var sites = await SitesAsync("_svc.A(); _svc.B();");

        Assert.AreEqual("A[]\nB[]", Render(sites));
    }

    [TestMethod]
    public async Task IfWithoutElse_IsOpt()
    {
        var sites = await SitesAsync("if (x > 0) { _svc.A(); }");

        Assert.AreEqual("A[opt#1.0:x > 0]", Render(sites));
    }

    [TestMethod]
    public async Task IfElse_IsAltWithTwoSections()
    {
        var sites = await SitesAsync("if (x > 0) _svc.A(); else _svc.B();");

        Assert.AreEqual("A[alt#1.0:x > 0]\nB[alt#1.1:else]", Render(sites));
    }

    [TestMethod]
    public async Task ElseIfChain_IsOneInstanceWithThreeSections()
    {
        var sites = await SitesAsync("if (x == 1) _svc.A(); else if (x == 2) _svc.B(); else _svc.C();");

        Assert.AreEqual("A[alt#1.0:x == 1]\nB[alt#1.1:x == 2]\nC[alt#1.2:else]", Render(sites));
    }

    [TestMethod]
    public async Task EmptyThenBranch_LoneElseBecomesOptNot()
    {
        var sites = await SitesAsync("if (flag) { } else { _svc.A(); }");

        Assert.AreEqual("A[opt#1.0:not (flag)]", Render(sites));
    }

    [TestMethod]
    public async Task CallInCondition_SitsOutsideTheFragment()
    {
        var sites = await SitesAsync("if (_svc.Check()) { _svc.A(); }");

        Assert.AreEqual("Check[]\nA[opt#1.0:_svc.Check()]", Render(sites));
    }

    [TestMethod]
    public async Task NestedIfInForeach_IsLoopThenAlt()
    {
        var sites = await SitesAsync("foreach (var i in list) { if (i > 0) _svc.A(); else _svc.B(); }");

        Assert.AreEqual("A[loop#1.0:foreach i in list > alt#2.0:i > 0]\nB[loop#1.0:foreach i in list > alt#2.1:else]", Render(sites));
    }

    [TestMethod]
    public async Task LoopLabels_ForWhileDo()
    {
        var sites = await SitesAsync(@"
            for (int i = 0; i < x; i++) _svc.A();
            while (flag) _svc.B();
            do { _svc.C(); } while (x > 0);");

        Assert.AreEqual("A[loop#1.0:for i < x]\nB[loop#2.0:while flag]\nC[loop#3.0:do while x > 0]", Render(sites));
    }

    [TestMethod]
    public async Task CallInForeachExpression_IsOutside_CallInForCondition_IsInside()
    {
        var sites = await SitesAsync(@"
            foreach (var i in _svc.Items()) _svc.A();
            for (int j = 0; j < _svc.Count(); j++) _svc.B();");

        Assert.AreEqual("Items[]\nA[loop#1.0:foreach i in _svc.Items()]\nCount[loop#2.0:for j < _svc.Count()]\nB[loop#2.0:for j < _svc.Count()]", Render(sites));
    }

    [TestMethod]
    public async Task SwitchStatement_LabelsCasesAndDefault_MultiLabelJoinedWithOr()
    {
        var sites = await SitesAsync(@"
            switch (x)
            {
                case 1: _svc.A(); break;
                case 2:
                case 3: _svc.B(); break;
                default: _svc.C(); break;
            }");

        Assert.AreEqual("A[alt#1.0:x == 1]\nB[alt#1.1:x == 2 or x == 3]\nC[alt#1.2:default]", Render(sites));
    }

    [TestMethod]
    public async Task SwitchStatement_PatternWithWhen()
    {
        var sites = await SitesAsync(@"
            switch (o)
            {
                case string t when t.Length > 0: _svc.A(); break;
                default: _svc.B(); break;
            }");

        Assert.AreEqual("A[alt#1.0:o is string t when t.Length > 0]\nB[alt#1.1:default]", Render(sites));
    }

    [TestMethod]
    public async Task SwitchExpressionArms_AreAltSections_DiscardIsDefault()
    {
        var sites = await SitesAsync("var r = x switch { 1 => _svc.Count(), _ => _svc.Count() + 1 };");

        Assert.AreEqual("Count[alt#1.0:x is 1]\nCount[alt#1.1:default]", Render(sites));
    }

    [TestMethod]
    public async Task TernaryInArgument_WrapsOnlyTheBranches()
    {
        var sites = await SitesAsync("_svc.A(); var v = flag ? _svc.Count() : _svc.Count() * 2; _svc.B();");

        Assert.AreEqual("A[]\nCount[alt#1.0:flag]\nCount[alt#1.1:else]\nB[]", Render(sites));
    }

    [TestMethod]
    public async Task Catch_IsBreakWithTypeAndFilter_TryAndFinallyAreTransparent()
    {
        var sites = await SitesAsync(@"
            try { _svc.A(); }
            catch (InvalidOperationException ex) when (ex.Message != null) { _svc.B(); }
            catch { _svc.C(); }
            finally { _svc.Count(); }", options: new TraversalOptions { MaxConditionLabelLength = 80 });

        Assert.AreEqual("A[]\nB[break#1.0:catch InvalidOperationException when ex.Message != null]\nC[break#2.0:catch]\nCount[]", Render(sites));
    }

    [TestMethod]
    public async Task Lambda_IsTransparent_ButConstructsInsideItCount()
    {
        var sites = await SitesAsync("var q = list.Where(i => { if (i > 0) return _svc.Check(); return false; }).ToList();");

        Assert.AreEqual("Check[opt#1.0:i > 0]", Render(sites.Where(s => s.Symbol.ContainingType?.Name == "Svc")));
    }

    [TestMethod]
    public async Task SameCalleeInTwoBranches_IsTwoSites_TwiceInOneBranch_IsOneSite()
    {
        var sites = await SitesAsync("if (flag) { _svc.A(); _svc.A(); } else { _svc.A(); }");

        Assert.AreEqual("A[alt#1.0:flag]\nA[alt#1.1:else]", Render(sites));
    }

    [TestMethod]
    public async Task MultiLineConditionWithComment_IsCompacted()
    {
        var sites = await SitesAsync(@"
            if (x > 0 // positive
                && flag)
            {
                _svc.A();
            }");

        Assert.AreEqual("A[opt#1.0:x > 0 && flag]", Render(sites));
    }

    [TestMethod]
    public async Task LongLabel_IsTruncatedWithEllipsis()
    {
        var sites = await SitesAsync("if (x > 1000000 && flag && s != null && o != null) _svc.A();", options: new TraversalOptions { MaxConditionLabelLength = 12 });

        var label = sites.Single().FragmentPath.Single().Label;
        Assert.AreEqual(12, label.Length, label);
        Assert.IsTrue(label.EndsWith("…"), label);
    }

    [TestMethod]
    public async Task FilteredCallee_DoesNotKeepABranchAlive()
    {
        // Constructors excluded: the `else` branch holds only `new Svc()`, so the alt downgrades to opt.
        var sites = await SitesAsync("if (flag) _svc.A(); else { var s2 = new Svc(); }", options: new TraversalOptions { IncludeConstructors = false });

        Assert.AreEqual("A[opt#1.0:flag]", Render(sites));
    }

    [TestMethod]
    public async Task FluentChainMembers_ShareThePath()
    {
        var sites = await SitesAsync("if (flag) _svc.Self().A();");

        Assert.AreEqual("Self[opt#1.0:flag]\nA[opt#1.0:flag]", Render(sites));
        Assert.AreEqual("Self", sites[1].FluentReceiver?.Name);
    }

    [TestMethod]
    public async Task EventSubscription_IsReportedInPlaceWithItsPath()
    {
        const string helpers = @"
public class Publisher { public event EventHandler Changed; }
";
        var sites = await SitesAsync("var p = new Publisher(); if (flag) { p.Changed += OnChanged; } _svc.A();", helpers + @"
public partial class Worker { public void OnChanged(object s, EventArgs e) { } }
");

        Assert.AreEqual(".ctor[]\nOnChanged[opt#1.0:flag]\nA[]", Render(sites));
    }
}
