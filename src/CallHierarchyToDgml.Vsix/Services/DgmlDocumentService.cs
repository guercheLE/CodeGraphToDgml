using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using CallHierarchyToDgml.Core;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;

namespace CallHierarchyToDgml.Vsix;

internal sealed class DgmlDocumentService
{
    private readonly ToolkitPackage _package;

    public DgmlDocumentService(ToolkitPackage package)
    {
        _package = package;
    }

    public async Task<IReadOnlyList<OpenDgmlDocument>> GetOpenDgmlDocumentsAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var runningDocumentTable = await _package.GetServiceAsync(typeof(SVsRunningDocumentTable)).ConfigureAwait(true) as IVsRunningDocumentTable;
        if (runningDocumentTable is null)
        {
            return Array.Empty<OpenDgmlDocument>();
        }

        ErrorHandler.ThrowOnFailure(runningDocumentTable.GetRunningDocumentsEnum(out var enumerator));

        var cookies = new uint[1];
        var documents = new List<OpenDgmlDocument>();

        while (enumerator.Next(1, cookies, out var fetched) == VSConstants.S_OK && fetched == 1)
        {
            IntPtr docDataPointer = IntPtr.Zero;

            try
            {
                ErrorHandler.ThrowOnFailure(runningDocumentTable.GetDocumentInfo(
                    cookies[0],
                    out _,
                    out _,
                    out _,
                    out var moniker,
                    out _,
                    out _,
                    out docDataPointer));

                if (Path.GetExtension(moniker).Equals(".dgml", StringComparison.OrdinalIgnoreCase))
                {
                    documents.Add(new OpenDgmlDocument(moniker));
                }
            }
            finally
            {
                if (docDataPointer != IntPtr.Zero)
                {
                    Marshal.Release(docDataPointer);
                }
            }
        }

        return documents
            .GroupBy(document => document.FullPath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(document => document.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<string?> GetActiveDgmlDocumentPathAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var dte = await _package.GetServiceAsync(typeof(DTE)).ConfigureAwait(true) as DTE2;
        var fullName = dte?.ActiveDocument?.FullName;
        return Path.GetExtension(fullName ?? string.Empty).Equals(".dgml", StringComparison.OrdinalIgnoreCase)
            ? fullName
            : null;
    }

    public async Task<DgmlDocumentSession> OpenOrCreateTemporaryDocumentAsync()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"CallHierarchy-{DateTime.Now:yyyyMMdd-HHmmssfff}.dgml");
        File.WriteAllText(filePath, DgmlSerializer.CreateEmptyText(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return await OpenDocumentSessionAsync(filePath).ConfigureAwait(true);
    }

    public async Task<DgmlDocumentSession> OpenDocumentSessionAsync(string fullPath)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        VsShellUtilities.OpenDocument(
            _package,
            fullPath,
            VSConstants.LOGVIEWID_Primary,
            out _,
            out _,
            out var windowFrame);

        ErrorHandler.ThrowOnFailure(windowFrame.Show());

        var (textLines, invisibleEditor) = await GetTextLinesAsync(fullPath).ConfigureAwait(true);
        return new DgmlDocumentSession(_package, fullPath, windowFrame, textLines, invisibleEditor);
    }

    private async Task<(IVsTextLines?, IVsInvisibleEditor?)> GetTextLinesAsync(string fullPath)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var invisibleEditorManager = await _package.GetServiceAsync(typeof(SVsInvisibleEditorManager)).ConfigureAwait(true) as IVsInvisibleEditorManager;
        if (invisibleEditorManager is null)
        {
            throw new InvalidOperationException("Invisible editor manager is unavailable.");
        }

        ErrorHandler.ThrowOnFailure(invisibleEditorManager.RegisterInvisibleEditor(
            fullPath,
            null,
            2 | 4, // RIEF_ENABLEUNDO | RIEF_ENABLEPASTING
            null,
            out var invisibleEditor));

        var guid = VSConstants.IID_IUnknown;
        ErrorHandler.ThrowOnFailure(invisibleEditor.GetDocData(
            1, // fEnsureWritable
            ref guid,
            out var docDataPointer));

        try
        {
            var docData = Marshal.GetObjectForIUnknown(docDataPointer);

            if (docData is IVsTextLines textLines)
            {
                return (textLines, invisibleEditor);
            }

            if (docData is IVsTextBufferProvider textBufferProvider)
            {
                ErrorHandler.ThrowOnFailure(textBufferProvider.GetTextBuffer(out var providedTextLines));
                return (providedTextLines, invisibleEditor);
            }

            return (null, null);
        }
        catch (Exception)
        {
            throw;
        }
        finally
        {
            if (docDataPointer != IntPtr.Zero)
            {
                Marshal.Release(docDataPointer);
            }
        }
    }
}

internal sealed class OpenDgmlDocument
{
    public OpenDgmlDocument(string fullPath)
    {
        FullPath = fullPath;
    }

    public string FullPath { get; }

    public string DisplayName => Path.GetFileName(FullPath);
}

internal sealed class DgmlDocumentSession
{
    private readonly ToolkitPackage _package;
    private IVsWindowFrame _windowFrame;
    private readonly IVsTextLines? _textLines;
    private readonly IVsInvisibleEditor? _invisibleEditor;

    public DgmlDocumentSession(ToolkitPackage package, string fullPath, IVsWindowFrame windowFrame, IVsTextLines? textLines, IVsInvisibleEditor? invisibleEditor)
    {
        _package = package;
        FullPath = fullPath;
        _windowFrame = windowFrame;
        _textLines = textLines;
        _invisibleEditor = invisibleEditor;
    }

    public string FullPath { get; }

    public string ReadAllText()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (_textLines is null)
        {
            return File.ReadAllText(FullPath);
        }

        ErrorHandler.ThrowOnFailure(_textLines.GetLastLineIndex(out var lastLine, out var lastIndex));
        ErrorHandler.ThrowOnFailure(_textLines.GetLineText(0, 0, lastLine, lastIndex, out var text));
        return text ?? string.Empty;
    }

    public void ReplaceAllText(string newText)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (_textLines is null)
        {
            ErrorHandler.ThrowOnFailure(_windowFrame.GetProperty((int)__VSFPROPID.VSFPROPID_DocData, out var docDataObj));
            var persistDocData = docDataObj as IVsPersistDocData;
            var persistDocData2 = docDataObj as IVsPersistDocData2;

            if (persistDocData2 != null)
            {
                ErrorHandler.ThrowOnFailure(persistDocData2.SetDocDataDirty(1));
            }

            // For DGML files not backed by text buffers, ReloadDocData prompts the user.
            // Close the frame without saving, overwrite the file, and open it again.
            _windowFrame.CloseFrame((uint)__FRAMECLOSE.FRAMECLOSE_NoSave);

            File.WriteAllText(FullPath, newText, new UTF8Encoding(false));

            VsShellUtilities.OpenDocument(
                _package,
                FullPath,
                VSConstants.LOGVIEWID_Primary,
                out _,
                out _,
                out _windowFrame);

            return;
        }

        ErrorHandler.ThrowOnFailure(_textLines.GetLastLineIndex(out var lastLine, out var lastIndex));

        var textPointer = Marshal.StringToHGlobalUni(newText);
        try
        {
            ErrorHandler.ThrowOnFailure(_textLines.ReplaceLines(0, 0, lastLine, lastIndex, textPointer, newText.Length, null));
        }
        finally
        {
            Marshal.FreeHGlobal(textPointer);
        }
    }

    public void Activate()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        ErrorHandler.ThrowOnFailure(_windowFrame.Show());
    }
}
