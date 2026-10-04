using System;
using System.Collections.Generic;
using System.Reflection;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.MarkupExtensions;

namespace Adamantium.UI.Core.Localization;

/// <summary><c>{Localize Table.Key}</c>: a binding to a string of a language table, in the application's language. It
/// follows the language, and the bindings that fill the string's placeholders:
/// <c>{Localize Strings.PageOf, page={Binding Page}, count={Binding PageCount}}</c>. One-way.</summary>
public class Localize : BindingBase
{
    public Localize()
    {
    }

    public Localize(LocalizedStrings table, string key)
    {
        Table = table;
        Key = key;
    }

    /// <summary>The table the string is in: a generated table's <c>Current</c>.</summary>
    public LocalizedStrings Table { get; set; }

    /// <summary>Where the table comes from when it is not known before the application runs - the words of a thing
    /// that brings its own, as a module loaded from a file does: <c>{Localize Table={Binding Phrases}, Key={Binding Name}}</c>.
    /// A binding, or in a control's template a <see cref="TemplateBinding"/>. Until it gives a table, and for a key the
    /// table lacks, the key is said as it is.</summary>
    public object TableSource { get; set; }

    /// <summary>The string's key. In markup the positional argument names the table too, <c>Strings.Close</c>, and the
    /// build resolves it.</summary>
    [DefaultProperty]
    public string Key { get; set; }

    /// <summary>Where the key comes from when it is not known before the application runs - the word for a kind of
    /// thing: <c>{Localize CanvasStrings, Key={Binding Sort}}</c>. A binding, or in a control's template a
    /// <see cref="TemplateBinding"/>. A key the table lacks is said as it is, and so are names an application gave its
    /// own things.</summary>
    public object KeySource { get; set; }

    /// <summary>The values of the string's placeholders by name: a binding, followed as it changes; in a control's
    /// template a <see cref="TemplateBinding"/> to a property of that control; or a plain value.</summary>
    public Dictionary<string, object> Arguments { get; } = new();

    public override object Clone()
    {
        var clone = new Localize(Table, Key)
        {
            TableSource = TableSource is BindingBase tableSource ? tableSource.Clone() : TableSource,
            KeySource = KeySource is BindingBase source ? source.Clone() : KeySource,
            Delay = Delay,
            FallbackValue = FallbackValue,
            Culture = Culture,
            TargetNullValue = TargetNullValue,
            IsAsync = IsAsync,
            IsImmediate = IsImmediate,
        };
        foreach (var (name, value) in Arguments)
        {
            clone.Arguments[name] = value is BindingBase binding ? binding.Clone() : value;
        }

        return clone;
    }

    public override BindingExpressionBase CreateExpression(IAdamantiumComponent target, AdamantiumProperty targetProperty) =>
        new LocalizeExpression(target, targetProperty, this);

    /// <summary>The table a generated table type stands for, its <c>Current</c>; null for a type that is not one.</summary>
    public static LocalizedStrings TableOf(Type type) =>
        type?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as LocalizedStrings;
}
