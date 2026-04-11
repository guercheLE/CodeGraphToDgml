using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace CallHierarchyToDgml.Core;

public sealed class DgmlSerializer
{
    private static readonly XNamespace Namespace = "http://schemas.microsoft.com/vs/2009/dgml";

    public string Merge(string? existingDgml, TraversalGraph graph, bool replaceContents)
    {
        var document = string.IsNullOrWhiteSpace(existingDgml)
            ? CreateEmptyDocument()
            : XDocument.Parse(existingDgml, LoadOptions.PreserveWhitespace);

        document.Declaration = new XDeclaration("1.0", "utf-8", "yes");

        var root = EnsureRoot(document);
        root.SetAttributeValue("GraphDirection", "TopToBottom");

        var nodesElement = EnsureChild(root, "Nodes");
        var linksElement = EnsureChild(root, "Links");
        var categoriesElement = EnsureChild(root, "Categories");

        EnsureCategories(categoriesElement);

        if (replaceContents)
        {
            nodesElement.RemoveAll();
            linksElement.RemoveAll();
        }

        var existingNodeIds = new HashSet<string>(
            nodesElement.Elements(Namespace + "Node")
                .Select(element => (string?)element.Attribute("Id"))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Cast<string>(),
            StringComparer.Ordinal);

        foreach (var node in graph.Nodes)
        {
            if (!existingNodeIds.Add(node.Id))
            {
                continue;
            }

            var nodeElement = new XElement(Namespace + "Node",
                new XAttribute("Id", node.Id),
                new XAttribute("Label", node.Label),
                new XAttribute("Category", node.Kind));

            if (node.Kind is "Project" or "Namespace" or "Class" or "Interface" or "Struct" or "Enum" or "Delegate")
            {
                nodeElement.SetAttributeValue("Group", "Expanded");
            }

            if (!string.IsNullOrWhiteSpace(node.FilePath))
            {
                nodeElement.SetAttributeValue("FilePath", node.FilePath);
            }

            if (node.Line.HasValue)
            {
                nodeElement.SetAttributeValue("Line", node.Line.Value);
            }

            nodesElement.Add(nodeElement);
        }

        var existingLinkKeys = new HashSet<string>(
            linksElement.Elements(Namespace + "Link")
                .Select(CreateLinkKey)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Cast<string>(),
            StringComparer.Ordinal);

        foreach (var link in graph.Links)
        {
            var key = CreateLinkKey(link.SourceId, link.TargetId, link.Category);
            if (!existingLinkKeys.Add(key))
            {
                continue;
            }

            linksElement.Add(new XElement(Namespace + "Link",
                new XAttribute("Source", link.SourceId),
                new XAttribute("Target", link.TargetId),
                new XAttribute("Category", link.Category)));
        }

        return Serialize(document);
    }

    public static string CreateEmptyText()
    {
        return Serialize(CreateEmptyDocument());
    }

    private static XDocument CreateEmptyDocument()
    {
        return new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XElement(Namespace + "DirectedGraph",
                new XAttribute("GraphDirection", "TopToBottom"),
                new XElement(Namespace + "Nodes"),
                new XElement(Namespace + "Links"),
                CreateCategoriesElement(),
                new XElement(Namespace + "Styles",
                    new XElement(Namespace + "Style",
                        new XAttribute("TargetType", "Node"),
                        new XAttribute("GroupLabel", "Project"),
                        new XAttribute("ValueLabel", "Has category"),
                        new XElement(Namespace + "Condition", new XAttribute("Expression", "HasCategory('Project')")),
                        new XElement(Namespace + "Setter", new XAttribute("Property", "Background"), new XAttribute("Value", "#FF005571")),
                        new XElement(Namespace + "Setter", new XAttribute("Property", "Foreground"), new XAttribute("Value", "#FFFFFFFF"))),
                    new XElement(Namespace + "Style",
                        new XAttribute("TargetType", "Node"),
                        new XAttribute("GroupLabel", "Namespace"),
                        new XAttribute("ValueLabel", "Has category"),
                        new XElement(Namespace + "Condition", new XAttribute("Expression", "HasCategory('Namespace')")),
                        new XElement(Namespace + "Setter", new XAttribute("Property", "Background"), new XAttribute("Value", "#FF0E706A")),
                        new XElement(Namespace + "Setter", new XAttribute("Property", "Foreground"), new XAttribute("Value", "#FFFFFFFF"))),
                    new XElement(Namespace + "Style",
                        new XAttribute("TargetType", "Node"),
                        new XAttribute("GroupLabel", "Class"),
                        new XAttribute("ValueLabel", "Has category"),
                        new XElement(Namespace + "Condition", new XAttribute("Expression", "HasCategory('Class')")),
                        new XElement(Namespace + "Setter", new XAttribute("Property", "Background"), new XAttribute("Value", "#FF1E5652")),
                        new XElement(Namespace + "Setter", new XAttribute("Property", "Foreground"), new XAttribute("Value", "#FFFFFFFF"))),
                    new XElement(Namespace + "Style",
                        new XAttribute("TargetType", "Node"),
                        new XAttribute("GroupLabel", "Interface"),
                        new XAttribute("ValueLabel", "Has category"),
                        new XElement(Namespace + "Condition", new XAttribute("Expression", "HasCategory('Interface')")),
                        new XElement(Namespace + "Setter", new XAttribute("Property", "Background"), new XAttribute("Value", "#FF093E46")),
                        new XElement(Namespace + "Setter", new XAttribute("Property", "Foreground"), new XAttribute("Value", "#FFFFFFFF"))),
                    new XElement(Namespace + "Style",
                        new XAttribute("TargetType", "Node"),
                        new XAttribute("GroupLabel", "Method"),
                        new XAttribute("ValueLabel", "Has category"),
                        new XElement(Namespace + "Condition", new XAttribute("Expression", "HasCategory('Method') Or HasCategory('Property') Or HasCategory('Event')")),
                        new XElement(Namespace + "Setter", new XAttribute("Property", "Background"), new XAttribute("Value", "#FF2B8B82")),
                        new XElement(Namespace + "Setter", new XAttribute("Property", "Foreground"), new XAttribute("Value", "#FFFFFFFF")))
                )));
    }

    private static XElement EnsureRoot(XDocument document)
    {
        var root = document.Root;
        if (root is not null)
        {
            return root;
        }

        root = new XElement(Namespace + "DirectedGraph");
        document.Add(root);
        return root;
    }

    private static XElement EnsureChild(XElement root, string localName)
    {
        var child = root.Element(Namespace + localName);
        if (child is not null)
        {
            return child;
        }

        child = new XElement(Namespace + localName);
        root.Add(child);
        return child;
    }

    private static void EnsureCategories(XElement categoriesElement)
    {
        var existing = new HashSet<string>(
            categoriesElement.Elements(Namespace + "Category")
                .Select(element => (string?)element.Attribute("Id"))
                .Where(value => !string.IsNullOrWhiteSpace(value))!
                .Cast<string>(),
            StringComparer.Ordinal);

        foreach (var category in GetCategoryDefinitions())
        {
            if (existing.Add(category.Id))
            {
                categoriesElement.Add(new XElement(Namespace + "Category",
                    new XAttribute("Id", category.Id),
                    new XAttribute("Label", category.Label)));
            }
        }
    }

    private static XElement CreateCategoriesElement()
    {
        var element = new XElement(Namespace + "Categories");
        EnsureCategories(element);
        return element;
    }

    private static IEnumerable<(string Id, string Label)> GetCategoryDefinitions()
    {
        yield return ("Method", "Method");
        yield return ("Property", "Property");
        yield return ("Event", "Event");
        yield return ("Class", "Class");
        yield return ("Interface", "Interface");
        yield return ("Struct", "Struct");
        yield return ("Enum", "Enum");
        yield return ("Delegate", "Delegate");
        yield return ("Calls", "Calls");
        yield return ("Implements/Overrides", "Implements/Overrides");
        yield return ("Contains", "Contains");
    }

    private static string CreateLinkKey(XElement element)
    {
        return CreateLinkKey(
            (string?)element.Attribute("Source") ?? string.Empty,
            (string?)element.Attribute("Target") ?? string.Empty,
            (string?)element.Attribute("Category") ?? string.Empty);
    }

    private static string CreateLinkKey(string sourceId, string targetId, string category)
    {
        return string.Concat(sourceId, "->", targetId, ":", category);
    }

    private static string Serialize(XDocument document)
    {
        using var stream = new MemoryStream();
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            OmitXmlDeclaration = false,
            NamespaceHandling = NamespaceHandling.OmitDuplicates,
        };

        using (var writer = XmlWriter.Create(stream, settings))
        {
            document.Save(writer);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
