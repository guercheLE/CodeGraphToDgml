using System.Collections.Generic;
using CodeGraphToDgml.Core;
using Microsoft.CodeAnalysis;

namespace CodeGraphToDgml.Vsix.Providers;

// Allocates stable, unique Mermaid participant ids for the types met during one traversal.
internal sealed class ParticipantTable
{
    private readonly Dictionary<string, string> _symbolIdToMermaidId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _baseNameCount = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CallSequenceParticipant> _participants = new();

    public IReadOnlyList<CallSequenceParticipant> Participants => _participants;

    public string GetOrAdd(INamedTypeSymbol type)
    {
        var symbolId = type.GetDocumentationCommentId() ?? type.ToDisplayString();
        if (_symbolIdToMermaidId.TryGetValue(symbolId, out var existing))
            return existing;

        var mermaidId = AllocateId(type.Name, symbolId);
        _participants.Add(new CallSequenceParticipant(mermaidId, type.Name));
        return mermaidId;
    }

    public string GetOrAddRaw(string name)
    {
        if (_symbolIdToMermaidId.TryGetValue(name, out var existing))
            return existing;

        var mermaidId = AllocateId(name, name);
        _participants.Add(new CallSequenceParticipant(mermaidId, name));
        return mermaidId;
    }

    private string AllocateId(string typeName, string symbolKey)
    {
        var baseName = SanitizeName(typeName);
        if (string.IsNullOrEmpty(baseName)) baseName = "Unknown";

        string mermaidId;
        if (!_baseNameCount.TryGetValue(baseName, out var count))
        {
            _baseNameCount[baseName] = 1;
            mermaidId = baseName;
        }
        else
        {
            _baseNameCount[baseName] = count + 1;
            mermaidId = $"{baseName}_{count}";
        }

        _symbolIdToMermaidId[symbolKey] = mermaidId;
        return mermaidId;
    }

    private static string SanitizeName(string name)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
                sb.Append(c);
            else
                sb.Append('_');
        }

        var result = sb.ToString().TrimStart('_');
        if (result.Length == 0) return "Unknown";
        return char.IsDigit(result[0]) ? "_" + result : result;
    }
}
