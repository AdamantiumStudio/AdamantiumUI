using Adamantium.UI.Markup.AST.MarkupExtension;
using Adamantium.UI.Markup.AST.TypeReference;

namespace Adamantium.UI.Markup.AST
{
    public static class AumlExtensions
    {
        public static IReadOnlyList<AumlAstPropertyNode> GetProperties(this IAumlAstNode node)
        {
            return node is AumlAstObjectNode objectNode ? ChildrenOf<AumlAstPropertyNode>(objectNode) : Array.Empty<AumlAstPropertyNode>();
        }

        public static IReadOnlyList<AumlAstObjectNode> GetLogicalChildrenObjects(this IAumlAstNode node)
        {
            return node is AumlAstObjectNode objectNode ? ChildrenOf<AumlAstObjectNode>(objectNode) : Array.Empty<AumlAstObjectNode>();
        }

        // A copy, so that a caller may change the tree while it walks them; none at all when there is nothing to copy.
        private static IReadOnlyList<T> ChildrenOf<T>(AumlAstObjectNode node) where T : class, IAumlAstNode
        {
            List<T> found = null;
            var children = node.Children;
            for (var i = 0; i < children.Count; i++)
            {
                if (children[i] is T child)
                {
                    (found ??= new List<T>(children.Count - i)).Add(child);
                }
            }

            return found ?? (IReadOnlyList<T>)Array.Empty<T>();
        }

        public static bool IsOfType(this IAumlAstNode node, string type)
        {
            if (node is AumlAstObjectNode objectNode)
            {
                return objectNode.TypeReference.Name == type;
            }

            return false;
        }

        public static IEnumerable<AumlAstPropertyNode> GetResourceDictionaries(this IAumlAstNode node)
        {
            var properties = new List<AumlAstPropertyNode>();
            if (node is AumlAstObjectNode objectNode)
            {
                var stack = new Stack<IAumlAstNode>();
                stack.Push(objectNode);
                while(stack.Count > 0)
                {
                    var current = stack.Pop();

                    if (current is AumlAstPropertyNode propertyNode)
                    {
                        properties.Add(propertyNode);

                        foreach(var value in propertyNode.Values)
                        {
                            stack.Push(value);
                        }
                    }

                    if (current is AumlAstObjectNode objNode)
                    {
                        foreach(var value in objNode.Children)
                        {
                            stack.Push(value);
                        }
                    }
                }

                var resourceDictionaries = properties.Where(x => x.Property is AumlAstPropertyReference prop && prop.Name == "Source").ToList();
                
                return resourceDictionaries;
            }

            return [];
        }

        public static bool IsTextNode(this IAumlAstValueNode valueNode)
        {
            return valueNode is AumlAstTextNode;
        }

        public static string GetTextValue(this IAumlAstValueNode valueNode)
        {
            return valueNode switch
            {
                AumlAstTextNode textNode => textNode.Text,
                _ => string.Empty
            };
        }

        public static int GetPropertyValuesCount(this IAumlAstNode node)
        {
            if (node is AumlAstPropertyNode property)
            {
                return property.Values.Count;
            }

            return 0;
        }

        public static string GetTextValue(this AumlAstPropertyNode property, int index = 0)
        {
            return property.Values[index].ToString();
        }

        public static string GetFullTypeValue(this AumlAstPropertyNode property, int index = 0)
        {
            if (property.Values[index] is IAumlAstValueNode node)
            {
                return node.TypeReference.GetFullTypeName();
            }
            return property.Values[index].ToString();
        }

        public static bool IsSetter(this AumlAstPropertyNode property)
        {
            return property.Property is AumlAstPropertyReference propertyReference &&
                   propertyReference.OwnerType.Name == "Setter";
        }
        
        public static IAumlAstTypeReference GetTypeReference(this IAumlAstNode node)
        {
            if (node is IAumlAstValueNode valueNode)
            {
                return valueNode.TypeReference;
            }
            
            return null;
        }
    }
}
