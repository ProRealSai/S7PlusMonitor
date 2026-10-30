using S7PlusMonitor.Core.Models;

namespace S7PlusMonitor.Core.Helpers;

/// <summary>符号树构建（当前闲置，Phase 5 树浏览备用）。</summary>

public sealed class SymbolTreeNodeData
{
    public required string Name { get; init; }

    public required string FullPath { get; init; }

    public VariableInfo? Variable { get; init; }

    public List<SymbolTreeNodeData> Children { get; } = [];
}

public static class SymbolTreeBuilder
{
    public static IList<SymbolTreeNodeData> Build(IEnumerable<VariableInfo> variables)
    {
        var root = new List<SymbolTreeNodeData>();
        var nodes = new Dictionary<string, SymbolTreeNodeData>(StringComparer.OrdinalIgnoreCase);

        foreach (var variable in variables
                     .Where(static item => !string.IsNullOrWhiteSpace(item.Symbol))
                     .OrderBy(item => item.Symbol, StringComparer.OrdinalIgnoreCase))
        {
            var segments = SymbolPathTokenizer.Split(variable.Symbol);
            if (segments.Count == 0)
            {
                continue;
            }

            SymbolTreeNodeData? parent = null;
            var path = string.Empty;

            for (var index = 0; index < segments.Count; index++)
            {
                path = index == 0 ? segments[index] : $"{path}.{segments[index]}";
                if (!nodes.TryGetValue(path, out var node))
                {
                    var isLeaf = index == segments.Count - 1;
                    node = isLeaf
                        ? CreateLeafNode(variable, path)
                        : CreateFolderNode(segments[index], path);

                    nodes[path] = node;
                    if (parent == null)
                    {
                        root.Add(node);
                    }
                    else
                    {
                        parent.Children.Add(node);
                    }
                }
                else if (index == segments.Count - 1 && node.Variable == null)
                {
                    var replacement = CreateLeafNode(variable, path);
                    CopyChildren(node, replacement);
                    ReplaceNode(parent, node, replacement, root);
                    nodes[path] = replacement;
                    node = replacement;
                }

                parent = node;
            }
        }

        return root;
    }

    private static SymbolTreeNodeData CreateFolderNode(string name, string fullPath) =>
        new()
        {
            Name = name,
            FullPath = fullPath
        };

    private static SymbolTreeNodeData CreateLeafNode(VariableInfo variable, string fullPath) =>
        new()
        {
            Name = GetDisplayName(variable.Symbol, fullPath),
            FullPath = fullPath,
            Variable = variable
        };

    private static void CopyChildren(SymbolTreeNodeData source, SymbolTreeNodeData target)
    {
        target.Children.AddRange(source.Children);
    }

    private static void ReplaceNode(
        SymbolTreeNodeData? parent,
        SymbolTreeNodeData oldNode,
        SymbolTreeNodeData newNode,
        IList<SymbolTreeNodeData> root)
    {
        if (parent == null)
        {
            var index = root.IndexOf(oldNode);
            if (index >= 0)
            {
                root[index] = newNode;
            }

            return;
        }

        var childIndex = parent.Children.IndexOf(oldNode);
        if (childIndex >= 0)
        {
            parent.Children[childIndex] = newNode;
        }
    }

    private static string GetDisplayName(string symbol, string fullPath)
    {
        var separator = symbol.LastIndexOf('.');
        if (separator >= 0 && separator < symbol.Length - 1)
        {
            return symbol[(separator + 1)..];
        }

        separator = fullPath.LastIndexOf('.');
        return separator >= 0 ? fullPath[(separator + 1)..] : fullPath;
    }
}
