using Adamantium.UI.Markup.Parsers;

namespace Adamantium.UI.Markup.CodeGeneration;

/// <summary>What a property element can hold - the build checks it, the language server's completion offers by it.</summary>
public static class PropertyValues
{
    /// <summary>The attribute a list of markup items carries, naming the type of its items.</summary>
    public const string MarkupItemAttribute = "Adamantium.UI.Core.MarkupItemAttribute";

    /// <summary>Whether the property takes any number of elements: a collection, a list of markup items or
    /// resources.</summary>
    public static bool TakesMany(IResolvedType propertyType) =>
        propertyType.IsCollection() || propertyType.HasAttribute(MarkupItemAttribute) ||
        propertyType.ImplementsInterface("IResourceDictionary") || propertyType.ImplementsInterface("IResourceContainer");

    /// <summary>Whether an element of type <paramref name="value"/> can be the one value of a property of type
    /// <paramref name="property"/>.</summary>
    public static bool Fits(IResolvedType value, IResolvedType property) =>
        property.SpecialType == ResolvedSpecialType.System_Object || property.IsGenericType || value is MetadataResolvedType ||
        value.IsAssignableTo(property.FullName) ||
        (property.TypeKind == ResolvedTypeKind.Interface && value.ImplementsInterface(property.Name)) ||
        (value.TypeKind == ResolvedTypeKind.Struct && property.TypeKind == ResolvedTypeKind.Struct) ||
        value.InheritsFromMarkupExtension(AumlParser.MarupExtensionClassFullName);
}
