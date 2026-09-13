using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CodeGraphToDgml.Core;
using CodeGraphToDgml.Roslyn;

namespace CodeGraphToDgml.Tests;

/// <summary>
/// <see cref="CallGraphSyntaxWalker.FindCallSitesAsync"/>: Visual Basic control-flow context,
/// mirroring <see cref="CallSiteFlowContextTests"/>.
/// </summary>
[TestClass]
public sealed class VisualBasicFlowContextTests
{
    private static async Task<IReadOnlyList<CallSiteInfo>> SitesAsync(string body, TraversalOptions? options = null)
    {
        var source = @"
Public Class Svc
    Public Sub A()
    End Sub
    Public Sub B()
    End Sub
    Public Sub C()
    End Sub
    Public Function Check() As Boolean
        Return True
    End Function
    Public Function Count() As Integer
        Return 1
    End Function
    Public Function Items() As List(Of Integer)
        Return New List(Of Integer)()
    End Function
End Class

Public Class Worker
    Private ReadOnly _svc As New Svc()

    Public Sub Run(x As Integer, s As String, flag As Boolean, list As List(Of Integer))
" + body + @"
    End Sub
End Class
";
        var (solution, _) = RoslynTestFixture.CreateVisualBasicSolution(source);
        var method = await RoslynTestFixture.GetMethodSymbolAsync(solution, "Worker", "Run");
        return await CallGraphSyntaxWalker.FindCallSitesAsync(method, solution, options ?? new TraversalOptions(), default);
    }

    private static string Render(IEnumerable<CallSiteInfo> sites)
    {
        static string Scope(CallSequenceFragmentScope s)
            => s.Kind.ToString().ToLowerInvariant() + "#" + s.InstanceId + "." + s.SectionIndex + ":" + s.Label;

        return string.Join("\n", sites.Select(site => site.Symbol.Name + "[" + string.Join(" > ", site.FragmentPath.Select(Scope)) + "]"));
    }

    [TestMethod]
    public async Task IfElseIfElse_IsOneAltWithThreeSections()
    {
        var sites = await SitesAsync(@"
        If x = 1 Then
            _svc.A()
        ElseIf x = 2 Then
            _svc.B()
        Else
            _svc.C()
        End If");

        Assert.AreEqual("A[alt#1.0:x = 1]\nB[alt#1.1:x = 2]\nC[alt#1.2:else]", Render(sites));
    }

    [TestMethod]
    public async Task IfWithoutElse_IsOpt_AndConditionCallIsOutside()
    {
        var sites = await SitesAsync(@"
        If _svc.Check() Then
            _svc.A()
        End If");

        Assert.AreEqual("Check[]\nA[opt#1.0:_svc.Check()]", Render(sites));
    }

    [TestMethod]
    public async Task SingleLineIfElse_IsAlt()
    {
        var sites = await SitesAsync("        If flag Then _svc.A() Else _svc.B()");

        Assert.AreEqual("A[alt#1.0:flag]\nB[alt#1.1:else]", Render(sites));
    }

    [TestMethod]
    public async Task SelectCase_LabelsSimpleRangeRelationalAndElse()
    {
        var sites = await SitesAsync(@"
        Select Case x
            Case 1
                _svc.A()
            Case 2 To 5, Is > 10
                _svc.B()
            Case Else
                _svc.C()
        End Select");

        Assert.AreEqual("A[alt#1.0:x = 1]\nB[alt#1.1:x in 2 To 5 or x > 10]\nC[alt#1.2:else]", Render(sites));
    }

    [TestMethod]
    public async Task Loops_ForForEachWhileDo()
    {
        var sites = await SitesAsync(@"
        For i = 1 To x
            _svc.A()
        Next
        For Each item In list
            _svc.B()
        Next
        While flag
            _svc.C()
        End While
        Do Until x > 0
            _svc.Count()
        Loop
        Do
            _svc.Check()
        Loop While flag");

        Assert.AreEqual(
            "A[loop#1.0:for i = 1 to x]\nB[loop#2.0:for each item in list]\nC[loop#3.0:while flag]\nCount[loop#4.0:do until x > 0]\nCheck[loop#5.0:loop while flag]",
            Render(sites));
    }

    [TestMethod]
    public async Task TryCatchFinally_CatchIsBreak_OthersTransparent()
    {
        var sites = await SitesAsync(@"
        Try
            _svc.A()
        Catch ex As InvalidOperationException When ex.Message IsNot Nothing
            _svc.B()
        Catch
            _svc.C()
        Finally
            _svc.Count()
        End Try", new TraversalOptions { MaxConditionLabelLength = 80 });

        Assert.AreEqual("A[]\nB[break#1.0:catch InvalidOperationException when ex.Message IsNot Nothing]\nC[break#2.0:catch]\nCount[]", Render(sites));
    }

    [TestMethod]
    public async Task TernaryIf_IsAlt_BinaryIfIsTransparent()
    {
        var sites = await SitesAsync(@"
        Dim v = If(flag, _svc.Count(), _svc.Count() * 2)
        Dim w = If(s, _svc.Check().ToString())");

        Assert.AreEqual("Count[alt#1.0:flag]\nCount[alt#1.1:else]\nCheck[]", Render(sites));
    }

    [TestMethod]
    public async Task NestedIfInForEach_IsLoopThenAlt()
    {
        var sites = await SitesAsync(@"
        For Each item In list
            If item > 0 Then
                _svc.A()
            Else
                _svc.B()
            End If
        Next");

        Assert.AreEqual("A[loop#1.0:for each item in list > alt#2.0:item > 0]\nB[loop#1.0:for each item in list > alt#2.1:else]", Render(sites));
    }

    [TestMethod]
    public async Task WithBlockAndLambda_AreTransparent()
    {
        var sites = await SitesAsync(@"
        With _svc
            If flag Then .A()
        End With
        Dim q = list.Where(Function(i) _svc.Check()).ToList()");

        Assert.AreEqual("A[opt#1.0:flag]", Render(sites.Where(s => s.Symbol.ContainingType?.Name == "Svc" && s.Symbol.Name == "A")));
        Assert.IsTrue(sites.Any(s => s.Symbol.Name == "Check" && s.FragmentPath.Count == 0), Render(sites));
    }
}
