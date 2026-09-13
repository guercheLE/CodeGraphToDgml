using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CodeGraphToDgml.Roslyn;

namespace CodeGraphToDgml.Tests;

/// <summary>
/// Visual Basic parity for <see cref="CallGraphSyntaxWalker.FindCalleesAsync"/>. Each test
/// mirrors a C# case in <see cref="CallGraphSyntaxWalkerTests"/>.
/// </summary>
[TestClass]
public sealed class VisualBasicCallGraphSyntaxWalkerTests
{
    private static async Task<IReadOnlyList<CalleeSymbolInfo>> GetCalleesAsync(string source, string typeName, string methodName)
    {
        var (solution, _) = RoslynTestFixture.CreateVisualBasicSolution(source);
        return await RoslynTestFixture.GetCalleesAsync(solution, typeName, methodName);
    }

    private static string Render(IEnumerable<CalleeSymbolInfo> callees)
    {
        return string.Join("\n", callees.Select(c => $"{c.Name}:{c.ContainingTypeName}:{c.FluentReceiverName}"));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Body resolution — the declaring syntax is the header, the body is the parent block
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task FindCalleesAsync_SubBody_IsWalkedFromTheMethodBlock()
    {
        const string source = @"
Public Class Helper
    Public Sub Assist()
    End Sub
End Class

Public Class Worker
    Private _helper As New Helper()

    Public Sub Run()
        _helper.Assist()
    End Sub
End Class
";
        var callees = await GetCalleesAsync(source, "Worker", "Run");

        Assert.AreEqual("Assist:Helper:", Render(callees));
    }

    [TestMethod]
    public async Task FindCalleesAsync_ConstructorBody_IsWalked_IncludingMyBaseNew()
    {
        const string source = @"
Public Class Logger
    Public Sub Log()
    End Sub
End Class

Public Class BaseType
    Public Sub New(x As Integer)
    End Sub
End Class

Public Class Derived
    Inherits BaseType

    Public Sub New()
        MyBase.New(1)
        Dim l As New Logger()
        l.Log()
    End Sub
End Class
";
        var callees = await GetCalleesAsync(source, "Derived", ".ctor");

        Assert.AreEqual(".ctor:BaseType:\n.ctor:Logger:\nLog:Logger:", Render(callees));
    }

    [TestMethod]
    public async Task FindCalleesAsync_PropertyGetterBody_IsWalked()
    {
        const string source = @"
Public Class Source
    Public Function Read() As Integer
        Return 1
    End Function
End Class

Public Class Holder
    Private _source As New Source()

    Public ReadOnly Property Value As Integer
        Get
            Return _source.Read()
        End Get
    End Property
End Class
";
        var (solution, _) = RoslynTestFixture.CreateVisualBasicSolution(source);
        var compilation = await solution.Projects.Single().GetCompilationAsync();
        var property = compilation!.GetTypeByMetadataName("Holder")!.GetMembers("Value").Single();

        var callees = await CallGraphSyntaxWalker.FindCalleesAsync(property, solution, default);

        Assert.AreEqual("Read", callees.Single().Symbol.Name);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Invocation shapes
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task FindCalleesAsync_ParenlessAndCallStatementInvocations_AreDetected()
    {
        const string source = @"
Public Class Worker
    Public Sub First()
    End Sub

    Public Sub Second()
    End Sub

    Public Function Third() As Integer
        Return 3
    End Function

    Public Sub Run()
        First
        Call Second
        Dim x = Third
    End Sub
End Class
";
        var callees = await GetCalleesAsync(source, "Worker", "Run");

        Assert.AreEqual("First:Worker:\nSecond:Worker:\nThird:Worker:", Render(callees));
    }

    [TestMethod]
    public async Task FindCalleesAsync_ObjectCreation_ConstructorIsDetected_ForNewAndAsNew()
    {
        const string source = @"
Public Class ServiceA
    Public Sub New(x As Integer)
    End Sub
End Class

Public Class ServiceB
End Class

Public Class Worker
    Public Sub Run()
        Dim a = New ServiceA(1)
        Dim b As New ServiceB()
    End Sub
End Class
";
        var callees = await GetCalleesAsync(source, "Worker", "Run");

        Assert.AreEqual(".ctor:ServiceA:\n.ctor:ServiceB:", Render(callees));
    }

    [TestMethod]
    public async Task FindCalleesAsync_PropertyWrite_SetterIsDetected_ButStandaloneReadIsNot()
    {
        const string source = @"
Public Class Model
    Public Property Name As String
    Public Property Count As Integer
End Class

Public Class Worker
    Public Sub Run(m As Model)
        m.Name = ""x""
        Dim c = m.Count
    End Sub
End Class
";
        var callees = await GetCalleesAsync(source, "Worker", "Run");

        Assert.AreEqual("Name:Model:", Render(callees));
    }

    [TestMethod]
    public async Task FindCalleesAsync_DefaultPropertyAccess_IsDetected_ArrayAccessIsNot()
    {
        const string source = @"
Public Class Store
    Default Public Property Item(index As Integer) As Integer
        Get
            Return index
        End Get
        Set(value As Integer)
        End Set
    End Property
End Class

Public Class Worker
    Public Sub Run(s As Store, arr As Integer())
        Dim v = s(0)
        Dim w = arr(0)
    End Sub
End Class
";
        var callees = await GetCalleesAsync(source, "Worker", "Run");

        Assert.AreEqual("Item:Store:", Render(callees));
    }

    [TestMethod]
    public async Task FindCalleesAsync_ChainedMemberAccess_PropertyComesBeforeMethod()
    {
        const string source = @"
Public Class Helper
    Public Sub Assist()
    End Sub
End Class

Public Class Client
    Public ReadOnly Property Helper As Helper = New Helper()
End Class

Public Class Worker
    Public Sub Run(c As Client)
        c.Helper.Assist()
    End Sub
End Class
";
        var callees = await GetCalleesAsync(source, "Worker", "Run");

        Assert.AreEqual("Helper:Client:\nAssist:Helper:", Render(callees));
    }

    [TestMethod]
    public async Task FindCalleesAsync_FluentChain_RecordsFluentReceiver_WithAndWithoutParentheses()
    {
        const string source = @"
Public Class Product
    Public Sub Ship()
    End Sub
End Class

Public Class Client
    Public Function GetClient() As Client
        Return Me
    End Function

    Public Function GetProduct() As Product
        Return New Product()
    End Function
End Class

Public Class Worker
    Public Sub Run(c As Client)
        c.GetClient().GetProduct().Ship()
    End Sub

    Public Sub RunParenless(c As Client)
        c.GetClient.GetProduct.Ship()
    End Sub
End Class
";
        var withParens = await GetCalleesAsync(source, "Worker", "Run");
        var parenless = await GetCalleesAsync(source, "Worker", "RunParenless");

        const string expected = "GetClient:Client:\nGetProduct:Client:GetClient\nShip:Product:GetProduct";
        Assert.AreEqual(expected, Render(withParens));
        Assert.AreEqual(expected, Render(parenless));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Events and delegates
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task FindCalleesAsync_RaiseEvent_MapsToEventSymbol()
    {
        const string source = @"
Public Class Publisher
    Public Event Changed As EventHandler

    Public Sub Raise()
        RaiseEvent Changed(Me, EventArgs.Empty)
    End Sub
End Class
";
        var callees = await GetCalleesAsync(source, "Publisher", "Raise");

        Assert.AreEqual("Changed:Publisher:", Render(callees));
    }

    [TestMethod]
    public async Task FindCalleesAsync_AddHandlerAndRemoveHandler_HandlersAreDetected_Last()
    {
        const string source = @"
Public Class Publisher
    Public Event Changed As EventHandler
    Public Sub Touch()
    End Sub
End Class

Public Class Subscriber
    Public Sub OnChanged(sender As Object, e As EventArgs)
    End Sub

    Public Sub OnRemoved(sender As Object, e As EventArgs)
    End Sub

    Public Sub Wire(p As Publisher)
        AddHandler p.Changed, AddressOf OnChanged
        p.Touch()
        RemoveHandler p.Changed, AddressOf OnRemoved
    End Sub
End Class
";
        var callees = await GetCalleesAsync(source, "Subscriber", "Wire");

        Assert.AreEqual("Touch:Publisher:\nOnChanged:Subscriber:\nOnRemoved:Subscriber:", Render(callees));
    }

    [TestMethod]
    public async Task FindCalleesAsync_AddressOfPassedToDelegateParameter_IsDetected()
    {
        const string source = @"
Public Class Handler
    Public Sub OnDone(x As Integer)
    End Sub
End Class

Public Module Runner
    Public Sub Invoke(callback As Action(Of Integer))
    End Sub

    Public Sub Show(value As Integer)
    End Sub
End Module

Public Class Worker
    Private _handler As New Handler()

    Public Function GetValue() As Integer
        Return 1
    End Function

    Public Sub Run()
        Runner.Invoke(AddressOf _handler.OnDone)
        Runner.Show(GetValue())
    End Sub
End Class
";
        var callees = await GetCalleesAsync(source, "Worker", "Run");

        Assert.AreEqual("Invoke:Runner:\nOnDone:Handler:\nGetValue:Worker:\nShow:Runner:", Render(callees));
    }

    [TestMethod]
    public async Task FindCalleesAsync_AddressOfPassedToConstructor_IsDetected()
    {
        const string source = @"
Public Class Worker
    Public Sub Work()
    End Sub

    Public Sub Run()
        Dim t As New System.Threading.Thread(AddressOf Work)
        t.Start()
    End Sub
End Class
";
        var callees = await GetCalleesAsync(source, "Worker", "Run");

        Assert.AreEqual(".ctor:Thread:\nWork:Worker:\nStart:Thread:", Render(callees));
    }

    [TestMethod]
    public async Task FindCalleesAsync_DelegateVariableInvoked_RedirectsToOriginalMethod()
    {
        const string source = @"
Public Class Worker
    Public Sub DoWork()
    End Sub

    Public Sub Run()
        Dim d As Action = AddressOf DoWork
        d()
        d.Invoke()
        Dim e As New Action(AddressOf DoWork)
        e.Invoke()
    End Sub
End Class
";
        var callees = await GetCalleesAsync(source, "Worker", "Run");

        Assert.AreEqual("DoWork:Worker:", Render(callees));
    }

    [TestMethod]
    public async Task FindCalleesAsync_DelegateBeginInvoke_RedirectsToOriginalMethod_NonDelegateIsNotHijacked()
    {
        const string source = @"
Public Class Control
    Public Sub BeginInvoke(a As Action)
    End Sub
End Class

Public Class Worker
    Private _control As New Control()

    Public Sub DoWork()
    End Sub

    Public Sub Run()
        Dim d As Action = AddressOf DoWork
        d.BeginInvoke(Nothing, Nothing)
        _control.BeginInvoke(AddressOf DoWork)
    End Sub
End Class
";
        var callees = await GetCalleesAsync(source, "Worker", "Run");

        Assert.AreEqual("DoWork:Worker:\nBeginInvoke:Control:", Render(callees));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Initializers, lambdas, self-calls, extension methods
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task FindCalleesAsync_ObjectAndCollectionInitializers_AreDetected()
    {
        const string source = @"
Public Class Model
    Public Property Name As String
End Class

Public Class Bag
    Public Sub Add(item As Integer)
    End Sub
End Class

Public Class Worker
    Public Sub Run()
        Dim m = New Model With {.Name = ""x""}
        Dim b = New Bag From {1, 2}
    End Sub
End Class
";
        var callees = await GetCalleesAsync(source, "Worker", "Run");

        // Post-order: initializer members are visited before the creation itself, as in C#.
        Assert.AreEqual("Name:Model:\n.ctor:Model:\nAdd:Bag:\n.ctor:Bag:", Render(callees));
    }

    [TestMethod]
    public async Task FindCalleesAsync_CallsInsideLambda_AttachToEnclosingMethod()
    {
        const string source = @"
Public Class Filter
    Public Function Keep(x As Integer) As Boolean
        Return True
    End Function
End Class

Public Class Worker
    Private _filter As New Filter()

    Public Sub Run(items As List(Of Integer))
        Dim kept = items.Where(Function(x) _filter.Keep(x)).ToList()
    End Sub
End Class
";
        var callees = await GetCalleesAsync(source, "Worker", "Run");

        Assert.IsTrue(callees.Any(c => c.Name == "Keep" && c.ContainingTypeName == "Filter"), Render(callees));
        Assert.IsFalse(callees.Any(c => string.IsNullOrEmpty(c.Name)), "Lambdas must not appear as callees.");
    }

    [TestMethod]
    public async Task FindCalleesAsync_SelfRecursion_IsExcluded()
    {
        const string source = @"
Public Class Worker
    Public Sub Other()
    End Sub

    Public Sub Run(n As Integer)
        If n > 0 Then Run(n - 1)
        Other()
    End Sub
End Class
";
        var callees = await GetCalleesAsync(source, "Worker", "Run");

        Assert.AreEqual("Other:Worker:", Render(callees));
    }

    [TestMethod]
    public async Task FindCalleesAsync_ExtensionMethodCalledBothStyles_DedupedToSingleCallee()
    {
        const string source = @"
Imports System.Runtime.CompilerServices

Public Module Extensions
    <Extension()>
    Public Sub Ext(s As String)
    End Sub
End Module

Public Class Worker
    Public Sub Run(s As String)
        s.Ext()
        Extensions.Ext(s)
    End Sub
End Class
";
        var callees = await GetCalleesAsync(source, "Worker", "Run");

        Assert.AreEqual("Ext:Extensions:", Render(callees));
    }

    [TestMethod]
    public async Task FindCalleesAsync_WithBlock_MemberCallsAreDetected()
    {
        const string source = @"
Public Class Model
    Public Property Name As String
    Public Sub Save()
    End Sub
End Class

Public Class Worker
    Public Sub Run(m As Model)
        With m
            .Name = ""x""
            .Save()
        End With
    End Sub
End Class
";
        var callees = await GetCalleesAsync(source, "Worker", "Run");

        Assert.AreEqual("Name:Model:\nSave:Model:", Render(callees));
    }

    [TestMethod]
    public async Task FindCalleesAsync_HandlesClause_IsNotReportedAsCall()
    {
        const string source = @"
Public Class Button
    Public Event Click As EventHandler
End Class

Public Class Form1
    Private WithEvents btn As New Button()

    Public Sub Log()
    End Sub

    Private Sub btn_Click(sender As Object, e As EventArgs) Handles btn.Click
        Log()
    End Sub
End Class
";
        var callees = await GetCalleesAsync(source, "Form1", "btn_Click");

        Assert.AreEqual("Log:Form1:", Render(callees));
    }
}
