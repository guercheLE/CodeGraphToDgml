using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace CodeGraphToDgml.Core;

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
        var propertiesElement = EnsureChild(root, "Properties");

        EnsureCategories(categoriesElement);
        EnsureProperties(propertiesElement);

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

            if (node.Kind is "CodeSchema_Assembly" or "CodeSchema_Namespace" or "CodeSchema_Class" or "CodeSchema_Interface" or "CodeSchema_Struct" or "CodeSchema_Enum" or "CodeSchema_Delegate")
            {
                nodeElement.SetAttributeValue("Group", "Collapsed");
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
                CreatePropertiesElement(),
                CreateStylesElement()));
    }

    private static XElement CreateStylesElement()
    {
        return new XElement(Namespace + "Styles",
            // --- Node styles ---
            CreateNodeStyle("Assembly", "HasCategory('CodeSchema_Assembly')",
                "#FF094167", "#FFFFFFFF", stroke: "#FF094167", icon: "CodeSchema_Assembly"),
            CreateNodeStyle("Namespace", "HasCategory('CodeSchema_Namespace')",
                "#FF0E619A", "#FFFFFFFF", stroke: "#FF0E619A", icon: "CodeSchema_Namespace"),
            CreateNodeStyle("Interface", "HasCategory('CodeSchema_Interface')",
                "#FF1382CE", "#FFFFFFFF", stroke: "#FF1382CE", icon: "CodeSchema_Interface"),
            CreateNodeStyle("Struct", "HasCategory('CodeSchema_Struct')",
                "#FF1382CE", "#FFFFFFFF", stroke: "#FF1382CE", icon: "CodeSchema_Struct"),
            CreateNodeStyle("Enumeration", "HasCategory('CodeSchema_Enum')",
                "#FF1382CE", "#FFFFFFFF", stroke: "#FF1382CE", icon: "CodeSchema_Enum"),
            CreateNodeStyle("Delegate", "HasCategory('CodeSchema_Delegate')",
                "#FF1382CE", "#FFFFFFFF", stroke: "#FF1382CE", icon: "CodeSchema_Delegate"),
            CreateNodeStyle("Class", "HasCategory('CodeSchema_Type')",
                "#FF1382CE", "#FFFFFFFF", stroke: "#FF1382CE", icon: "CodeSchema_Class"),
            CreateNodeStyle("Property", "HasCategory('CodeSchema_Property')",
                "#FFE0E0E0", "#FF1E1E1E", stroke: "#FFE0E0E0", icon: "CodeSchema_Property"),
            CreateNodeStyle("Method", "HasCategory('CodeSchema_Method') Or HasCategory('CodeSchema_CallStackUnresolvedMethod')",
                "#FFE0E0E0", "#FF1E1E1E", stroke: "#FFE0E0E0", icon: "CodeSchema_Method"),
            CreateNodeStyle("Event", "HasCategory('CodeSchema_Event')",
                "#FFE0E0E0", "#FF1E1E1E", stroke: "#FFE0E0E0", icon: "CodeSchema_Event"),
            CreateNodeStyle("Field", "HasCategory('CodeSchema_Field')",
                "#FFE0E0E0", "#FF1E1E1E", stroke: "#FFE0E0E0", icon: "CodeSchema_Field"),
            CreateNodeStyle("Externals", "HasCategory('Externals')",
                "#FF424242", "#FFFFFFFF", stroke: "#FF424242"),
            // --- Link styles ---
            CreateLinkStyle("Inherits From", "HasCategory('InheritsFrom')",
                "#FF00A600", "2 0", drawArrow: true),
            CreateLinkStyle("Implements", "HasCategory('Implements')",
                "#8000A600", "2 2", drawArrow: true),
            CreateLinkStyle("Calls", "HasCategory('CodeSchema_Calls')",
                "#FFFF00FF", "2 0", drawArrow: true),
            CreateLinkStyle("Function Pointer", "HasCategory('CodeSchema_FunctionPointer')",
                "#FFFF00FF", "2 2", drawArrow: true),
            CreateLinkStyle("Contains", "HasCategory('Contains')",
                "#FF808080", "2 0", drawArrow: false),
            CreateLinkStyle("References", "HasCategory('References')",
                "#FF4488FF", "2 2", drawArrow: true),
            CreateLinkStyle("Used By", "HasCategory('UsedBy')",
                "#FFFF8C00", "2 0", drawArrow: true));
    }

    private static XElement CreateNodeStyle(
        string groupLabel, string expression,
        string background, string foreground,
        string? stroke = null,
        string? icon = null)
    {
        var style = new XElement(Namespace + "Style",
            new XAttribute("TargetType", "Node"),
            new XAttribute("GroupLabel", groupLabel),
            new XAttribute("ValueLabel", "Has category"),
            new XElement(Namespace + "Condition", new XAttribute("Expression", expression)),
            new XElement(Namespace + "Setter", new XAttribute("Property", "Background"), new XAttribute("Value", background)),
            new XElement(Namespace + "Setter", new XAttribute("Property", "Foreground"), new XAttribute("Value", foreground)));

        if (stroke is not null)
        {
            style.Add(new XElement(Namespace + "Setter",
                new XAttribute("Property", "Stroke"), new XAttribute("Value", stroke)));
        }

        if (icon is not null)
        {
            style.Add(new XElement(Namespace + "Setter",
                new XAttribute("Property", "Icon"), new XAttribute("Value", icon)));
        }

        return style;
    }

    private static XElement CreateLinkStyle(
        string groupLabel, string expression,
        string stroke, string strokeDashArray,
        bool drawArrow)
    {
        return new XElement(Namespace + "Style",
            new XAttribute("TargetType", "Link"),
            new XAttribute("GroupLabel", groupLabel),
            new XAttribute("ValueLabel", "True"),
            new XElement(Namespace + "Condition", new XAttribute("Expression", expression)),
            new XElement(Namespace + "Setter", new XAttribute("Property", "Stroke"), new XAttribute("Value", stroke)),
            new XElement(Namespace + "Setter", new XAttribute("Property", "StrokeDashArray"), new XAttribute("Value", strokeDashArray)),
            new XElement(Namespace + "Setter", new XAttribute("Property", "DrawArrow"), new XAttribute("Value", drawArrow ? "true" : "false")));
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
                categoriesElement.Add(CreateCategoryElement(category));
            }
        }
    }

    private static XElement CreateCategoryElement(CategoryDefinition category)
    {
        var element = new XElement(Namespace + "Category",
            new XAttribute("Id", category.Id),
            new XAttribute("Label", category.Label));

        if (category.BasedOn is not null)
            element.SetAttributeValue("BasedOn", category.BasedOn);
        if (category.CanBeDataDriven.HasValue)
            element.SetAttributeValue("CanBeDataDriven", category.CanBeDataDriven.Value ? "True" : "False");
        if (category.CanLinkedNodesBeDataDriven.HasValue)
            element.SetAttributeValue("CanLinkedNodesBeDataDriven", category.CanLinkedNodesBeDataDriven.Value ? "True" : "False");
        if (category.DefaultAction is not null)
            element.SetAttributeValue("DefaultAction", category.DefaultAction);
        if (category.Description is not null)
            element.SetAttributeValue("Description", category.Description);
        if (category.Icon is not null)
            element.SetAttributeValue("Icon", category.Icon);
        if (category.IncomingActionLabel is not null)
            element.SetAttributeValue("IncomingActionLabel", category.IncomingActionLabel);
        if (category.IsContainment.HasValue)
            element.SetAttributeValue("IsContainment", category.IsContainment.Value ? "True" : "False");
        if (category.NavigationActionLabel is not null)
            element.SetAttributeValue("NavigationActionLabel", category.NavigationActionLabel);
        if (category.OutgoingActionLabel is not null)
            element.SetAttributeValue("OutgoingActionLabel", category.OutgoingActionLabel);

        return element;
    }

    private static void EnsureProperties(XElement propertiesElement)
    {
        var existing = new HashSet<string>(
            propertiesElement.Elements(Namespace + "Property")
                .Select(element => (string?)element.Attribute("Id"))
                .Where(value => !string.IsNullOrWhiteSpace(value))!
                .Cast<string>(),
            StringComparer.Ordinal);

        foreach (var property in GetPropertyDefinitions())
        {
            if (existing.Add(property.Id))
            {
                var element = new XElement(Namespace + "Property",
                    new XAttribute("Id", property.Id),
                    new XAttribute("DataType", property.DataType));

                if (property.Label is not null)
                    element.SetAttributeValue("Label", property.Label);
                if (property.Description is not null)
                    element.SetAttributeValue("Description", property.Description);

                propertiesElement.Add(element);
            }
        }
    }

    private static XElement CreateCategoriesElement()
    {
        var element = new XElement(Namespace + "Categories");
        EnsureCategories(element);
        return element;
    }

    private static XElement CreatePropertiesElement()
    {
        var element = new XElement(Namespace + "Properties");
        EnsureProperties(element);
        return element;
    }

    private static IEnumerable<CategoryDefinition> GetCategoryDefinitions()
    {
        yield return new CategoryDefinition("CodeSchema_Assembly", "Assembly",
            Icon: "CodeSchema_Assembly",
            DefaultAction: "Microsoft.Contains",
            CanBeDataDriven: true,
            NavigationActionLabel: "Assemblies");

        yield return new CategoryDefinition("CodeSchema_Namespace", "Namespace",
            Icon: "CodeSchema_Namespace",
            DefaultAction: "Node:Both:CodeSchema_Type",
            CanBeDataDriven: true,
            NavigationActionLabel: "Namespaces");

        yield return new CategoryDefinition("CodeSchema_Class", "Class",
            BasedOn: "CodeSchema_Type",
            Icon: "CodeSchema_Class",
            DefaultAction: "Node:Both:CodeSchema_Member",
            CanBeDataDriven: true,
            NavigationActionLabel: "Classes");

        yield return new CategoryDefinition("CodeSchema_Interface", "Interface",
            BasedOn: "CodeSchema_Type",
            Icon: "CodeSchema_Interface",
            DefaultAction: "Node:Both:CodeSchema_Member",
            CanBeDataDriven: true,
            NavigationActionLabel: "Interfaces");

        yield return new CategoryDefinition("CodeSchema_Struct", "Struct",
            BasedOn: "CodeSchema_Type",
            Icon: "CodeSchema_Struct",
            DefaultAction: "Node:Both:CodeSchema_Member",
            CanBeDataDriven: true,
            NavigationActionLabel: "Structs");

        yield return new CategoryDefinition("CodeSchema_Enum", "Enum",
            BasedOn: "CodeSchema_Type",
            Icon: "CodeSchema_Enum",
            DefaultAction: "Node:Both:CodeSchema_Member",
            CanBeDataDriven: true,
            NavigationActionLabel: "Enums");

        yield return new CategoryDefinition("CodeSchema_Delegate", "Delegate",
            BasedOn: "CodeSchema_Type",
            Icon: "CodeSchema_Delegate",
            CanBeDataDriven: true,
            NavigationActionLabel: "Delegates");

        yield return new CategoryDefinition("CodeSchema_Type", "Type",
            Icon: "CodeSchema_Class",
            DefaultAction: "Node:Both:CodeSchema_Member",
            CanBeDataDriven: true,
            NavigationActionLabel: "Types");

        yield return new CategoryDefinition("CodeSchema_Method", "Method",
            Icon: "CodeSchema_Method",
            CanBeDataDriven: true);

        yield return new CategoryDefinition("CodeSchema_Property", "Property",
            Icon: "CodeSchema_Property",
            CanBeDataDriven: true);

        yield return new CategoryDefinition("CodeSchema_Event", "Event",
            Icon: "CodeSchema_Event",
            CanBeDataDriven: true);

        yield return new CategoryDefinition("CodeSchema_Field", "Field",
            Icon: "CodeSchema_Field",
            CanBeDataDriven: true);

        yield return new CategoryDefinition("CodeSchema_Calls", "Calls",
            CanBeDataDriven: true,
            CanLinkedNodesBeDataDriven: true,
            IncomingActionLabel: "Called By",
            OutgoingActionLabel: "Calls");

        yield return new CategoryDefinition("CodeSchema_FunctionPointer", "Function Pointer",
            CanBeDataDriven: true,
            CanLinkedNodesBeDataDriven: true,
            IncomingActionLabel: "Called By",
            OutgoingActionLabel: "Calls");

        yield return new CategoryDefinition("Implements", "Implements",
            CanBeDataDriven: true,
            CanLinkedNodesBeDataDriven: true,
            IncomingActionLabel: "Implemented By",
            OutgoingActionLabel: "Implements");

        yield return new CategoryDefinition("Contains", "Contains",
            Description: "Whether the source of the link contains the target object",
            CanBeDataDriven: false,
            CanLinkedNodesBeDataDriven: true,
            IncomingActionLabel: "Contained By",
            OutgoingActionLabel: "Contains",
            IsContainment: true);

        yield return new CategoryDefinition("References", "References",
            CanBeDataDriven: true,
            CanLinkedNodesBeDataDriven: true,
            IncomingActionLabel: "Referenced By",
            OutgoingActionLabel: "References");

        yield return new CategoryDefinition("InheritsFrom", "Inherits From",
            CanBeDataDriven: true,
            CanLinkedNodesBeDataDriven: true,
            IncomingActionLabel: "Inherited By",
            OutgoingActionLabel: "Inherits From");

        yield return new CategoryDefinition("UsedBy", "Used By",
            CanBeDataDriven: true,
            CanLinkedNodesBeDataDriven: true,
            IncomingActionLabel: "Used By",
            OutgoingActionLabel: "Uses");

        yield return new CategoryDefinition("Externals", "Externals",
            CanBeDataDriven: true);
    }

    private static IEnumerable<PropertyDefinition> GetPropertyDefinitions()
    {
        yield return new PropertyDefinition("FilePath", "File Path", "File Path");
        yield return new PropertyDefinition("Line", "Line", "Source line number", "System.Int32");
        yield return new PropertyDefinition("Group", "Group",
            "Display the node as a group",
            "Microsoft.VisualStudio.GraphModel.GraphGroupStyle");
        yield return new PropertyDefinition("Icon", "Icon", "Icon");
        yield return new PropertyDefinition("Label", "Label",
            "Displayable label of an Annotatable object");
        yield return new PropertyDefinition("GraphDirection", "Graph Direction",
            "Graph layout direction");
        yield return new PropertyDefinition("IsContainment",
            DataType: "System.Boolean");
        yield return new PropertyDefinition("CanBeDataDriven", "CanBeDataDriven",
            "CanBeDataDriven", "System.Boolean");
        yield return new PropertyDefinition("CanLinkedNodesBeDataDriven", "CanLinkedNodesBeDataDriven",
            "CanLinkedNodesBeDataDriven", "System.Boolean");
        yield return new PropertyDefinition("IncomingActionLabel", "IncomingActionLabel",
            "IncomingActionLabel");
        yield return new PropertyDefinition("OutgoingActionLabel", "OutgoingActionLabel",
            "OutgoingActionLabel");
        yield return new PropertyDefinition("DefaultAction", "DefaultAction",
            "DefaultAction");
        yield return new PropertyDefinition("NavigationActionLabel", "NavigationActionLabel",
            "NavigationActionLabel");
        yield return new PropertyDefinition("Stroke", "Stroke",
            "Stroke");
        yield return new PropertyDefinition("StrokeDashArray", "Stroke Dash Array",
            "Stroke Dash Array");
        yield return new PropertyDefinition("DrawArrow", "Draw Arrow",
            "Draw Arrow", "System.Boolean");
        yield return new PropertyDefinition("DataVirtualized", "Data Virtualized",
            "Indicates whether the graph data is virtualized", "System.Boolean");
        yield return new PropertyDefinition("LayoutSettings", "Layout Settings",
            "Layout Settings");
        yield return new PropertyDefinition("Visibility", "Visibility",
            "Visibility",
            "System.Windows.Visibility");
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
