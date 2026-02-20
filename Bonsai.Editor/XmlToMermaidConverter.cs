using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Bonsai.Expressions;
using System.Reactive.Linq;
using System.IO;

namespace Bonsai.Editor
{
    public class XmlToMermaidConverter
    {
        //xml -> mermaid
        public static Type GetNodeType(XElement element, XDocument doc, XNamespace ns, XNamespace xsi)
        {
            XElement combinator = element.Element(ns + "Combinator");
            string typeName = combinator?.Attribute(xsi + "type")?.Value;

            if (typeName == null)
            {
                typeName = element.Attribute(xsi + "type")?.Value;
            }

            if (typeName == null)
            {
                Console.WriteLine($"{element}: Unknown type");
                return null;
            }

            string typeNamespaceAlias = null;
            string className = null;

            string[] parts = typeName.Split(':');

            if (parts.Length == 2)
            {
                typeNamespaceAlias = parts[0];
                className = parts[1];
            }
            else if (parts.Length == 1)
            {
                className = parts[0];
            }
            else
            {
                Console.WriteLine($"{element} invalid type name");
                return null;
            }


            string clrNs = null;
            string assemblyName = null;

            if (typeNamespaceAlias != null)
            {
                Dictionary<string, string> xmlns = doc.Root.Attributes()
                    .Where((XAttribute a) => a.IsNamespaceDeclaration && a.Name.Namespace == XNamespace.Xmlns)
                    .ToDictionary(
                        (XAttribute a) => a.Name.LocalName,
                        (XAttribute a) => a.Value
                    );

                if (!xmlns.TryGetValue(typeNamespaceAlias, out string clrNamespace))
                {
                    Console.WriteLine($"{element} wrong typenamespacealias");
                    return null;
                }

                string[] clrParts = clrNamespace.Replace("clr-namespace:", "").Split(';');
                if (clrParts.Length != 2)
                {
                    Console.WriteLine($"{element} invalid clr namespace format ");
                    return null;
                }

                clrNs = clrParts[0];
                assemblyName = clrParts[1].Replace("assembly=", "");
            }
            else
            {
                clrNs = "Bonsai.Expressions";
                assemblyName = "Bonsai.Core";
            }


            Assembly asm;
            try
            {
                asm = Assembly.Load(assemblyName);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{element} couldnt load assembly  '{assemblyName}': {ex.Message}");
                return null;
            }

            Type type = asm.GetType($"{clrNs}.{className}");
            if (type == null)
            {
                className += "Builder";

                type = asm.GetType($"{clrNs}.{className}");
                if (type == null)
                {
                    Console.WriteLine($"{element} couldnt find type '{clrNs}.{className}' in assembly '{assemblyName}'");
                }
            }

            return type;
        }

        public static string GetCategory(Type type)
        {
            while (type != null)
            {
                WorkflowElementCategoryAttribute attr = type.GetCustomAttribute<WorkflowElementCategoryAttribute>();
                if (attr != null)
                {
                    string category = attr.Category.ToString();
                    return category;
                }

                if (type.GetCustomAttribute<CombinatorAttribute>() != null)
                {
                    return "Combinator";
                }

                type = type.BaseType;
            }

            return "Unknown";
        }

        public static Tuple<string, string> GetName(XElement expr, XNamespace ns, XNamespace xsi, Dictionary<string, int> nodeTypeCounts, bool isSubGraph = false)
        {
            string xsiType = "";

            if (isSubGraph) xsiType = expr.Element(ns + "Name")?.Value;
            if (string.IsNullOrEmpty(xsiType))
            {
                XElement combinator = expr.Element(ns + "Combinator");

                if (combinator == null)
                {
                    xsiType = expr.Attribute(xsi + "type")?.Value;
                }
                else xsiType = combinator?.Attribute(xsi + "type")?.Value;
            }

            string idName = xsiType;
            if (idName == null) idName = "Unknown";


            if (!nodeTypeCounts.ContainsKey(idName))
            {
                nodeTypeCounts[idName] = 0;
            }
            else idName += nodeTypeCounts[idName];
            nodeTypeCounts[xsiType ?? "Unknown"]++;



            string? displayName = expr.Element(ns + "Name")?.Value;

            IEnumerable<XElement>? properties = expr.Elements(ns + "Property");

            string typeVal = expr.Attribute(xsi + "type")?.Value;
            if (typeVal == "PropertyMapping" || typeVal == "InputMapping")
            {
                properties = expr.Element(ns + "PropertyMappings")?.Elements(ns + "Property");
            }

            foreach (XElement property in properties)
            {
                string? currName = null;
                if (currName == null) currName = property.Attribute("DisplayName")?.Value;
                if (currName == null) currName = property.Attribute("Name")?.Value;
                if (currName == null) currName = property.Attributes().FirstOrDefault()?.Value;

                if (currName != null) displayName += $"{currName}, ";
            }
            if (displayName != null) displayName = displayName.TrimEnd(',', ' ');


            if (displayName == null)
            {
                if (isSubGraph) displayName += "Subgraph";
                displayName = xsiType.Split(':')[1];
            }


            



            return new Tuple<string, string>(idName, displayName);
        }

        public static List<string> GetInfo(XDocument doc, XElement expr, XNamespace ns, XNamespace xsi)
        {
            List<string> elements = new List<string>();

            string type = expr.Attribute(xsi + "type")?.Value;
            if (type == "Combinator")
            {
                expr = expr.Element(ns + "Combinator");
                type = expr.Attribute(xsi + "type")?.Value;
            }

            foreach (XElement e in expr.Elements())
            {
                if (e.Name != ns + "Workflow")
                {
                    elements.Add(e.ToString());
                }
            }

            return elements;
        }


        public static List<string> GetMermaid(XElement parentWorkflow, XDocument doc, XNamespace ns, XNamespace xsi, Dictionary<string, int> nodeTypeCounts, bool displaySubgraphs)
        {
            List<string> res = new List<string>();

            IEnumerable<XElement> nodes = parentWorkflow
                .Element(ns + "Workflow")?
                .Element(ns + "Nodes")?
                .Elements(ns + "Expression")
                ?? Enumerable.Empty<XElement>();

            IEnumerable<XElement> edgeExpressions = parentWorkflow
                .Element(ns + "Workflow")?
                .Element(ns + "Edges")?
                .Elements(ns + "Edge")
                ?? Enumerable.Empty<XElement>();

            Dictionary<int, string> nodeNames = new Dictionary<int, string>();

            for (int i = 0; i < nodes.Count(); i++)
            {
                XElement expr = nodes.ElementAt(i);

                Type exprType = GetNodeType(expr, doc, ns, xsi);
                string category = GetCategory(exprType);

                Tuple<string, string> nameTuple = GetName(expr, ns, xsi, nodeTypeCounts);

                res.Add($"{nameTuple.Item1}({nameTuple.Item2}):::{category}");

                nodeNames[i] = nameTuple.Item1;

                List<string> info = GetInfo(doc, expr, ns, xsi);

                foreach (string s in info)
                {
                    string[] lines = s.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                    foreach (string line in lines.Where(line => line != "")) res.Add($"%%c {line}");

                }

                var workflow = expr.Element(ns + "Workflow");
                if (workflow != null)
                {
                    Tuple<string, string> name = GetName(expr, ns, xsi, nodeTypeCounts, true);

                    List<string> subGraph = GetMermaid(expr, doc, ns, xsi, nodeTypeCounts, displaySubgraphs);
                    if (displaySubgraphs)
                    {
                        res.Add($"subgraph {name.Item1}[{name.Item2}]");
                        res.AddRange(subGraph.Select(s => $"{s}"));
                        res.Add("end");
                    }
                    else
                    {
                        res.Add($"%% subgraph {name.Item1}[{name.Item2}]");

                        foreach (string s in subGraph)
                        {
                            if (!s.StartsWith("%%")) res.Add($"%%  {s}");
                            else res.Add(s);
                        }

                        res.Add("%% end");
                    }

                    string? target = subGraph.FirstOrDefault();
                    if (!string.IsNullOrEmpty(target))
                    {
                        int index = target.IndexOf('(');
                        if (index >= 0) target = target.Substring(0, index);

                        if (displaySubgraphs) res.Add($"{nodeNames[i]} <--> {target}");
                        else res.Add($"%%  {nodeNames[i]} <--> {target}");
                    }
                }

            }

            List<Tuple<int, int>> edges = edgeExpressions
                .Select(e => new Tuple<int, int>(
                    Convert.ToInt32(e.Attribute("From")?.Value),
                    Convert.ToInt32(e.Attribute("To")?.Value)
                ))
                .Where(e => nodeNames.ContainsKey(e.Item1) && nodeNames.ContainsKey(e.Item2))
                .ToList();

            foreach (Tuple<int, int> edge in edges)
            {
                string from = nodeNames[edge.Item1];
                string to = nodeNames[edge.Item2];
                res.Add($"    {from} --> {to}");
            }
            return res;
        }

        public static List<string> ParseToMermaid(XDocument doc, bool displaySubgraphs)
        {
            string projectBaseDirectory = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\"));
            string outputMermaidPath = Path.Combine(projectBaseDirectory, "workflow.mmd");

            XNamespace ns = doc.Root.Name.Namespace;
            XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";

            List<string> mermaidLines = new List<string>();

            mermaidLines.Add($"%% displaySubgraphs: {displaySubgraphs}");

            foreach (XAttribute attr in doc.Root.Attributes())
            {
                mermaidLines.Add($"%% {attr.ToString()}");

            }
            mermaidLines.Add("graph LR");

            mermaidLines.Add("classDef Source fill:#c8f7c5,stroke:#2d862d,stroke-width:2px;");
            mermaidLines.Add("classDef Transform fill:#cce5ff,stroke:#0059b3,stroke-width:2px;");
            mermaidLines.Add("classDef Sink fill:#e6ccff,stroke:#663399,stroke-width:2px;");
            mermaidLines.Add("classDef Combinator fill:#fff3cd,stroke:#b38f00,stroke-width:2px;");
            mermaidLines.Add("classDef Property fill:#e0e0e0,stroke:#999999,stroke-dasharray: 5 5;");
            mermaidLines.Add("classDef Workflow fill:#fff3cd,stroke:#b38f00,stroke-width:2px,stroke-dasharray: 5 5;");
            mermaidLines.Add("classDef Unknown fill:#e0e0e0,stroke:#999999,stroke-dasharray: 5 5;");

            mermaidLines.AddRange(GetMermaid(doc.Root, doc, ns, xsi, new Dictionary<string, int>(), displaySubgraphs));

            return mermaidLines;

        }


        //mermaid -> xml 
        public static XDocument ParseToXml(List<string> mermaidLines)
        {
            XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";

            List<XAttribute> attributes = ParseAttributes(mermaidLines);

            XAttribute nsAttr = attributes.FirstOrDefault(a => a.Name == "xmlns");
            XNamespace ns = nsAttr != null ? nsAttr.Value : "http://bonsai-project.io/WorkflowBuilder";

            XDocument doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(ns + "WorkflowBuilder"));

            foreach (XAttribute a in attributes) doc.Root.Add(a);

            bool displaySubgraphs = mermaidLines[0].Split(' ')[2] == "True" ? true : false;

            doc.Root.Add(GetXml(mermaidLines, ns, xsi, new Dictionary<string, int>(), attributes, displaySubgraphs));

            return doc;
        }

        public static List<XAttribute> ParseAttributes(List<string> lines)
        {
            List<XAttribute> attrs = new List<XAttribute>();
            Regex regex = new Regex(@"^%%\s*(?<name>[^\s=]+)\s*=\s*""(?<value>[^""]*)""");

            foreach (string line in lines)
            {
                var match = regex.Match(line);
                if (!match.Success)
                    continue;

                string name = match.Groups["name"].Value;
                string value = match.Groups["value"].Value;

                if (name.StartsWith("xmlns:"))
                {
                    string prefix = name.Substring("xmlns:".Length);
                    attrs.Add(new XAttribute(XNamespace.Xmlns + prefix, value));
                }
                else if (name == "xmlns")
                {
                    attrs.Add(new XAttribute("xmlns", value));
                }
                else
                {
                    attrs.Add(new XAttribute(name, value));
                }
            }

            return attrs;
        }

        public static XElement GetXml(List<string> mermaidLines, XNamespace ns, XNamespace xsi, Dictionary<string, int> indexMap, List<XAttribute> attributes, bool displaySubgraphs, int nodeCount = 0, int recursionOffset = 0)
        {
            recursionOffset = nodeCount;
            XElement element = new XElement(ns + "Workflow");
            XElement nodes = new XElement(ns + "Nodes");
            XElement edges = new XElement(ns + "Edges");

            element.Add(nodes);
            element.Add(edges);

            Regex nodeRgx = new Regex(@"([^()]+)\(([^)]+)\):::(\w+)");
            Regex edgeRgx = new Regex(@"([^\s]+)\s+-->\s*([^\s]+)");
            Regex subStartRgx = new Regex(@"subgraph\s+([^\s\[]+)\[?([^\]]*)\]?");
            Regex subEndRgx = new Regex(@"^\s*end\s*$");

            Dictionary<int, int> targetEdgeCounts = new Dictionary<int, int>();
            XElement subGraphElement = null;

            for (int i = 0; i < mermaidLines.Count; i++)
            {
                if (mermaidLines[i].StartsWith("%%")) mermaidLines[i] = mermaidLines[i].Substring(2).Trim();
            }

            for (int i = 0; i < mermaidLines.Count; i++)
            {
                string line = mermaidLines[i];

                if (string.IsNullOrEmpty(line)) continue;

                if (nodeRgx.Match(line).Success)
                {
                    Match match = nodeRgx.Match(line);

                    string id = match.Groups[1].Value;
                    string name = match.Groups[2].Value;

                    indexMap[id] = nodeCount;

                    while (int.TryParse(id.Last().ToString(), out _))
                    {
                        id = id.Substring(0, id.Length - 1);
                    }

                    Type nodeType = GetBonsaiType(id);
                    bool isCombinator = IsCombinator(nodeType);

                    string xsiName = GetXsiType(attributes, nodeType); //nodetype null

                    if (xsiName.Contains("Builder"))
                    {
                        xsiName = xsiName.Replace("Builder", "");
                    }

                    XElement contained = null;
                    bool subGraph = false;

                    string xmlText = "";
                    while (i + 1 < mermaidLines.Count && mermaidLines[i + 1].StartsWith("c"))
                    {
                        i++;
                        xmlText += mermaidLines[i].Substring(2).Trim();
                        xmlText += "\n";

                        if (i + 1 < mermaidLines.Count) if (subStartRgx.Match(mermaidLines[i + 1]).Success)
                        {
                            subGraph = true;
                            break;
                        }
                    }
                    if (xmlText != "")
                    {
                        string containedStr = "<container>" + xmlText + "</container>";
                        contained = XElement.Parse(containedStr);
                    }


                    XElement node = null;
                    if (isCombinator)
                    {
                        node = new XElement(ns + "Expression",
                            new XAttribute(xsi + "type", "Combinator"));

                        XElement combinatorElement = new XElement(ns + "Combinator",
                            new XAttribute(xsi + "type", xsiName));

                        if (contained != null)
                        {
                            foreach (XElement e in contained.Elements())
                            {
                                combinatorElement.Add(e);
                            }
                        }

                        node.Add(combinatorElement);
                    }
                    else
                    {
                        node = new XElement(ns + "Expression",
                            new XAttribute(xsi + "type", xsiName));

                        if (contained != null)
                        {
                            foreach (XElement e in contained.Elements())
                            {
                                node.Add(e);
                            }
                        }
                    }

                    if (subGraph) subGraphElement = node;
                    else nodes.Add(node);
                    nodeCount++;
                }
                if (edgeRgx.Match(line).Success)
                {
                    Match match = edgeRgx.Match(line);

                    int from = indexMap[match.Groups[1].Value] - recursionOffset;
                    int to = indexMap[match.Groups[2].Value] - recursionOffset;

                    int pos = 1;
                    if (targetEdgeCounts.ContainsKey(to))
                    {
                        pos = targetEdgeCounts[to] + 1;
                    }
                    else targetEdgeCounts[to] = 1;

                    XElement edge = new XElement(ns + "Edge",
                        new XAttribute("From", from),
                        new XAttribute("To", to),
                        new XAttribute("Label", $"Source{pos}")
                    );

                    edges.Add(edge);
                }
                if (subStartRgx.Match(line).Success)
                {
                    i++;
                    int nestingDepth = 1;
                    List<string> subGraphLines = new List<string>();
                    while (i < mermaidLines.Count)
                    {
                        string subLine = mermaidLines[i];

                        if (subStartRgx.Match(subLine).Success) nestingDepth++;
                        else if (subEndRgx.Match(subLine).Success) nestingDepth--;
                        if (nestingDepth == 0) break;

                        subGraphLines.Add(mermaidLines[i]);
                        i++;
                    }
                    XElement subworkflow = GetXml(subGraphLines, ns, xsi, indexMap, attributes, displaySubgraphs, nodeCount);

                    subGraphElement.Add(subworkflow);

                    nodes.Add(subGraphElement);
                }


            }

            return element;
        }

        public static Type GetBonsaiType(string name)
        {
            name = name.Split(':').Last();

            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = asm.GetTypes().FirstOrDefault(t => t.Name == name);
                if (type != null) return type;
            }
            name += "Builder";
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = asm.GetTypes().FirstOrDefault(t => t.Name == name);
                if (type != null) return type;
            }

            return typeof(ExternalizedMapping); //assumes name without type is externalizedmapping
        }

        public static bool IsCombinator(Type type)
        {
            while (type != null)
            {
                if (type.GetCustomAttribute<CombinatorAttribute>() != null)
                {
                    return true;
                }

                type = type.BaseType;
            }

            return false;
        }

        public static string GetXsiType(List<XAttribute> rootAttributes, Type type)
        {
            string clrNs = $"clr-namespace:{type.Namespace};assembly={type.Assembly.GetName().Name}";

            foreach (XAttribute attr in rootAttributes)
            {
                if (attr.IsNamespaceDeclaration)
                {
                    string prefix = attr.Name.LocalName;
                    string nsValue = attr.Value;

                    if (nsValue == clrNs)
                    {
                        return $"{prefix}:{type.Name}";
                    }
                }
            }

            return type.Name;
        }

        public static void ExportMermaid(string exportFileName, string bonsaiFilePath)
        {
            XDocument doc = XDocument.Load(bonsaiFilePath);

            List<string> mermaidLines = XmlToMermaidConverter.ParseToMermaid(doc, true);

            File.WriteAllLines(exportFileName, mermaidLines);
        }
    }
}
