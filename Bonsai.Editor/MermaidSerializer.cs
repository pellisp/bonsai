using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Bonsai.Editor.GraphModel
{
    public static class MermaidSerializer
    {
        internal static string Serialize(IEnumerable<GraphNode> nodes)
        {
            if (nodes == null) throw new ArgumentNullException(nameof(nodes));

            var sb = new StringBuilder();
            sb.AppendLine("flowchart LR");

            SerializeNodes(nodes, sb, 0);

            return sb.ToString();
        }

        private static void SerializeNodes(IEnumerable<GraphNode> nodes, StringBuilder sb, int idCounter)
        {
            int idOffset = idCounter;

            List<GraphNode> nodeList = nodes.ToList();
            for (int i = 0; i < nodeList.Count; i++)
            {
                GraphNode node = nodeList[i];
                node.Index = idCounter + i;

                string nodeId = $"N{node.Index}";
                string label = node.Text;

                sb.AppendLine($"    {nodeId}[\"{label}\"]");
                if (node.NestedCategory != null)
                {
                    sb.AppendLine($"    subgraph {nodeId}[\"{label}\"]");
                    IEnumerable<GraphNode> children = GetNestedChildren(node);
                    SerializeNodes(children, sb, idCounter + i + 1);
                    idCounter += children.Count();
                    sb.AppendLine("    end");
                }

                foreach (var edge in node.Successors ?? Enumerable.Empty<GraphEdge>())
                {
                    if (edge?.Node == null) continue;
                    var targetId = $"N{edge.Node.Index}";
                    sb.AppendLine($"    {nodeId} --> {targetId}");
                }
            }
        }

        static IEnumerable<GraphNode> GetNestedChildren(this GraphNode node)
        {
            if (node?.Successors == null) yield break;

            foreach (var edge in node.Successors)
            {
                if (edge?.Node != null)
                    yield return edge.Node;
            }
        }
    }
}
