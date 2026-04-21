using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace CodeGraphToDgml.Core;

public interface IDgmlSchemaProvider
{
    IEnumerable<CategoryDefinition> GetCategories();
    IEnumerable<PropertyDefinition> GetProperties();
    IEnumerable<XElement> GetStyles(XNamespace xmlNamespace);
}

public sealed class DefaultDgmlSchemaProvider : IDgmlSchemaProvider
{
    public IEnumerable<CategoryDefinition> GetCategories()
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

    public IEnumerable<PropertyDefinition> GetProperties()
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

    public IEnumerable<XElement> GetStyles(XNamespace xmlNamespace)
    {
        yield return CreateNodeStyle(xmlNamespace, "Assembly", "HasCategory('CodeSchema_Assembly')", "#FF094167", "#FFFFFFFF", stroke: "#FF094167", icon: "CodeSchema_Assembly");
        yield return CreateNodeStyle(xmlNamespace, "Namespace", "HasCategory('CodeSchema_Namespace')", "#FF0E619A", "#FFFFFFFF", stroke: "#FF0E619A", icon: "CodeSchema_Namespace");
        yield return CreateNodeStyle(xmlNamespace, "Interface", "HasCategory('CodeSchema_Interface')", "#FF1382CE", "#FFFFFFFF", stroke: "#FF1382CE", icon: "CodeSchema_Interface");
        yield return CreateNodeStyle(xmlNamespace, "Struct", "HasCategory('CodeSchema_Struct')", "#FF1382CE", "#FFFFFFFF", stroke: "#FF1382CE", icon: "CodeSchema_Struct");
        yield return CreateNodeStyle(xmlNamespace, "Enumeration", "HasCategory('CodeSchema_Enum')", "#FF1382CE", "#FFFFFFFF", stroke: "#FF1382CE", icon: "CodeSchema_Enum");
        yield return CreateNodeStyle(xmlNamespace, "Delegate", "HasCategory('CodeSchema_Delegate')", "#FF1382CE", "#FFFFFFFF", stroke: "#FF1382CE", icon: "CodeSchema_Delegate");
        yield return CreateNodeStyle(xmlNamespace, "Class", "HasCategory('CodeSchema_Type')", "#FF1382CE", "#FFFFFFFF", stroke: "#FF1382CE", icon: "CodeSchema_Class");
        yield return CreateNodeStyle(xmlNamespace, "Property", "HasCategory('CodeSchema_Property')", "#FFE0E0E0", "#FF1E1E1E", stroke: "#FFE0E0E0", icon: "CodeSchema_Property");
        yield return CreateNodeStyle(xmlNamespace, "Method", "HasCategory('CodeSchema_Method') Or HasCategory('CodeSchema_CallStackUnresolvedMethod')", "#FFE0E0E0", "#FF1E1E1E", stroke: "#FFE0E0E0", icon: "CodeSchema_Method");
        yield return CreateNodeStyle(xmlNamespace, "Event", "HasCategory('CodeSchema_Event')", "#FFE0E0E0", "#FF1E1E1E", stroke: "#FFE0E0E0", icon: "CodeSchema_Event");
        yield return CreateNodeStyle(xmlNamespace, "Field", "HasCategory('CodeSchema_Field')", "#FFE0E0E0", "#FF1E1E1E", stroke: "#FFE0E0E0", icon: "CodeSchema_Field");
        yield return CreateNodeStyle(xmlNamespace, "Externals", "HasCategory('Externals')", "#FF424242", "#FFFFFFFF", stroke: "#FF424242");
        yield return CreateLinkStyle(xmlNamespace, "Inherits From", "HasCategory('InheritsFrom')", "#FF00A600", "2 0", drawArrow: true);
        yield return CreateLinkStyle(xmlNamespace, "Implements", "HasCategory('Implements')", "#8000A600", "2 2", drawArrow: true);
        yield return CreateLinkStyle(xmlNamespace, "Calls", "HasCategory('CodeSchema_Calls')", "#FFFF00FF", "2 0", drawArrow: true);
        yield return CreateLinkStyle(xmlNamespace, "Function Pointer", "HasCategory('CodeSchema_FunctionPointer')", "#FFFF00FF", "2 2", drawArrow: true);
        yield return CreateLinkStyle(xmlNamespace, "Contains", "HasCategory('Contains')", "#FF808080", "2 0", drawArrow: false);
        yield return CreateLinkStyle(xmlNamespace, "References", "HasCategory('References')", "#FF4488FF", "2 2", drawArrow: true);
        yield return CreateLinkStyle(xmlNamespace, "Used By", "HasCategory('UsedBy')", "#FFFF8C00", "2 0", drawArrow: true);
    }

    private static XElement CreateNodeStyle(
        XNamespace ns,
        string groupLabel, string expression,
        string background, string foreground,
        string? stroke = null,
        string? icon = null)
    {
        var style = new XElement(ns + "Style",
            new XAttribute("TargetType", "Node"),
            new XAttribute("GroupLabel", groupLabel),
            new XAttribute("ValueLabel", "Has category"),
            new XElement(ns + "Condition", new XAttribute("Expression", expression)),
            new XElement(ns + "Setter", new XAttribute("Property", "Background"), new XAttribute("Value", background)),
            new XElement(ns + "Setter", new XAttribute("Property", "Foreground"), new XAttribute("Value", foreground)));

        if (stroke is not null)
        {
            style.Add(new XElement(ns + "Setter",
                new XAttribute("Property", "Stroke"), new XAttribute("Value", stroke)));
        }

        if (icon is not null)
        {
            style.Add(new XElement(ns + "Setter",
                new XAttribute("Property", "Icon"), new XAttribute("Value", icon)));
        }

        return style;
    }

    private static XElement CreateLinkStyle(
        XNamespace ns,
        string groupLabel, string expression,
        string stroke, string strokeDashArray,
        bool drawArrow)
    {
        return new XElement(ns + "Style",
            new XAttribute("TargetType", "Link"),
            new XAttribute("GroupLabel", groupLabel),
            new XAttribute("ValueLabel", "True"),
            new XElement(ns + "Condition", new XAttribute("Expression", expression)),
            new XElement(ns + "Setter", new XAttribute("Property", "Stroke"), new XAttribute("Value", stroke)),
            new XElement(ns + "Setter", new XAttribute("Property", "StrokeDashArray"), new XAttribute("Value", strokeDashArray)),
            new XElement(ns + "Setter", new XAttribute("Property", "DrawArrow"), new XAttribute("Value", drawArrow ? "true" : "false")));
    }
}

public sealed class DgmlSerializer
{
    private static readonly XNamespace Namespace = "http://schemas.microsoft.com/vs/2009/dgml";
    private readonly IDgmlSchemaProvider _schemaProvider;

    public DgmlSerializer(IDgmlSchemaProvider? schemaProvider = null)
    {
        _schemaProvider = schemaProvider ?? new DefaultDgmlSchemaProvider();
    }

    public string Merge(string? existingDgml, TraversalGraph graph, bool replaceContents, bool collapseGroups = false)
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

            if (IsContainerKind(node.Kind))
            {
                nodeElement.SetAttributeValue("Group", collapseGroups ? "Collapsed" : "Expanded");
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

    public string CreateEmptyText()
    {
        return Serialize(CreateEmptyDocument());
    }

    private XDocument CreateEmptyDocument()
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

    private XElement CreateStylesElement()
    {
        var element = new XElement(Namespace + "Styles");
        foreach (var style in _schemaProvider.GetStyles(Namespace))
        {
            element.Add(style);
        }
        return element;
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

    private void EnsureCategories(XElement categoriesElement)
    {
        var existing = new HashSet<string>(
            categoriesElement.Elements(Namespace + "Category")
                .Select(element => (string?)element.Attribute("Id"))
                .Where(value => !string.IsNullOrWhiteSpace(value))!
                .Cast<string>(),
            StringComparer.Ordinal);

        foreach (var category in _schemaProvider.GetCategories())
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

        SetAttributeIfNotNull(element, "BasedOn", category.BasedOn);
        SetAttributeIfHasValue(element, "CanBeDataDriven", category.CanBeDataDriven);
        SetAttributeIfHasValue(element, "CanLinkedNodesBeDataDriven", category.CanLinkedNodesBeDataDriven);
        SetAttributeIfNotNull(element, "DefaultAction", category.DefaultAction);
        SetAttributeIfNotNull(element, "Description", category.Description);
        SetAttributeIfNotNull(element, "Icon", category.Icon);
        SetAttributeIfNotNull(element, "IncomingActionLabel", category.IncomingActionLabel);
        SetAttributeIfHasValue(element, "IsContainment", category.IsContainment);
        SetAttributeIfNotNull(element, "NavigationActionLabel", category.NavigationActionLabel);
        SetAttributeIfNotNull(element, "OutgoingActionLabel", category.OutgoingActionLabel);

        return element;
    }

    private void EnsureProperties(XElement propertiesElement)
    {
        var existing = new HashSet<string>(
            propertiesElement.Elements(Namespace + "Property")
                .Select(element => (string?)element.Attribute("Id"))
                .Where(value => !string.IsNullOrWhiteSpace(value))!
                .Cast<string>(),
            StringComparer.Ordinal);

        foreach (var property in _schemaProvider.GetProperties())
        {
            if (existing.Add(property.Id))
            {
                var element = new XElement(Namespace + "Property",
                    new XAttribute("Id", property.Id),
                    new XAttribute("DataType", property.DataType));

                SetAttributeIfNotNull(element, "Label", property.Label);
                SetAttributeIfNotNull(element, "Description", property.Description);

                propertiesElement.Add(element);
            }
        }
    }

    private XElement CreateCategoriesElement()
    {
        var element = new XElement(Namespace + "Categories");
        EnsureCategories(element);
        return element;
    }

    private XElement CreatePropertiesElement()
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

    private static bool IsContainerKind(string kind)
    {
        return kind is "CodeSchema_Assembly" or "CodeSchema_Namespace" or "CodeSchema_Class"
            or "CodeSchema_Interface" or "CodeSchema_Struct" or "CodeSchema_Enum" or "CodeSchema_Delegate";
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

    private static void SetAttributeIfNotNull(XElement element, string name, string? value)
    {
        if (value is not null)
        {
            element.SetAttributeValue(name, value);
        }
    }

    private static void SetAttributeIfHasValue(XElement element, string name, bool? value)
    {
        if (value.HasValue)
        {
            element.SetAttributeValue(name, value.Value ? "True" : "False");
        }
    }
}
