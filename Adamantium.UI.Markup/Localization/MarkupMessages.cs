using Adamantium.Localization;

namespace Adamantium.UI.Markup.Localization;

/// <summary>What the markup's build and its tooling say - problems, and the descriptions of the x: directives - in
/// the language of <see cref="System.Globalization.CultureInfo.CurrentUICulture"/>; each method is one message. What the
/// build of language files says is the engine's <c>LanguageMessages</c>.</summary>
public static class MarkupMessages
{
    /// <summary>The catalog the messages are read from.</summary>
    public static MessageCatalog Catalog { get; } = new(typeof(MarkupMessages).Assembly, nameof(MarkupMessages));

    public static string Directive(string name) => Say("Directive" + name);

    public static string XmlnsMissingOnRoot(string element) => Say(nameof(XmlnsMissingOnRoot), new PhraseArgument("element", element));
    public static string XmlnsOnlyAtRoot() => Say(nameof(XmlnsOnlyAtRoot));
    public static string MarkupExtensionNotWrapped(string text) => Say(nameof(MarkupExtensionNotWrapped), new PhraseArgument("text", text));

    public static string PropertyElementAttribute(string element, string attribute) =>
        Say(nameof(PropertyElementAttribute), new PhraseArgument("element", element), new PhraseArgument("attribute", attribute));

    public static string PropertyElementAmidText(string element) =>
        Say(nameof(PropertyElementAmidText), new PhraseArgument("element", element));

    public static string PrefixNotDefined(string prefix) => Say(nameof(PrefixNotDefined), new PhraseArgument("prefix", prefix));

    public static string AssemblyNotFound(object assembly) => Say(nameof(AssemblyNotFound), new PhraseArgument("assembly", assembly));

    public static string AssemblyOfElementNotResolved(object assembly, string element, string @namespace, bool resolved) =>
        Say(nameof(AssemblyOfElementNotResolved), new PhraseArgument("assembly", assembly), new PhraseArgument("element", element),
            new PhraseArgument("namespace", @namespace), new PhraseArgument("resolved", resolved));

    public static string XmlNamespaceNotFound(string @namespace) =>
        Say(nameof(XmlNamespaceNotFound), new PhraseArgument("namespace", @namespace));

    public static string TypeNotInNamespace(string type, string @namespace) =>
        Say(nameof(TypeNotInNamespace), new PhraseArgument("type", type), new PhraseArgument("namespace", @namespace));

    public static string TypeNotInAnyAssembly(string type) => Say(nameof(TypeNotInAnyAssembly), new PhraseArgument("type", type));
    public static string TypeNotFoundCheckNamespace(string type) => Say(nameof(TypeNotFoundCheckNamespace), new PhraseArgument("type", type));
    public static string NotAMarkupExtension(string type) => Say(nameof(NotAMarkupExtension), new PhraseArgument("type", type));

    public static string PropertyNotFoundIn(string property, string type) =>
        Say(nameof(PropertyNotFoundIn), new PhraseArgument("property", property), new PhraseArgument("type", type));

    public static string UnknownPropertyOnType(string property, string type) =>
        Say(nameof(UnknownPropertyOnType), new PhraseArgument("property", property), new PhraseArgument("type", type));

    public static string NotCreatable(string type) => Say(nameof(NotCreatable), new PhraseArgument("type", type));
    public static string ConditionWithoutBinding() => Say(nameof(ConditionWithoutBinding));
    public static string ConditionWithoutProperty() => Say(nameof(ConditionWithoutProperty));

    public static string PropertySetTwice(string property, string element) =>
        Say(nameof(PropertySetTwice), new PhraseArgument("property", property), new PhraseArgument("element", element));

    public static string ReadOnlyProperty(string type, string property) =>
        Say(nameof(ReadOnlyProperty), new PhraseArgument("type", type), new PhraseArgument("property", property));

    public static string OneValue(string property) => Say(nameof(OneValue), new PhraseArgument("property", property));

    public static string WrongValueType(string value, string type, string property) =>
        Say(nameof(WrongValueType), new PhraseArgument("value", value), new PhraseArgument("type", type),
            new PhraseArgument("property", property));

    public static string ParagraphInsideSpan(string span) => Say(nameof(ParagraphInsideSpan), new PhraseArgument("span", span));
    public static string TemplateTakesOne(string type) => Say(nameof(TemplateTakesOne), new PhraseArgument("type", type));
    public static string NoPlaceForChild(string type) => Say(nameof(NoPlaceForChild), new PhraseArgument("type", type));

    public static string ContentSetTwice(string type, string property) =>
        Say(nameof(ContentSetTwice), new PhraseArgument("type", type), new PhraseArgument("property", property));

    public static string NoPlaceForText(string type) => Say(nameof(NoPlaceForText), new PhraseArgument("type", type));

    public static string PartNotFound(string template, string name) =>
        Say(nameof(PartNotFound), new PhraseArgument("template", template), new PhraseArgument("name", name));

    public static string BindingPathNotFound(string type, string member, string path) =>
        Say(nameof(BindingPathNotFound), new PhraseArgument("type", type), new PhraseArgument("member", member),
            new PhraseArgument("path", path));

    public static string NameTwice(string name) => Say(nameof(NameTwice), new PhraseArgument("name", name));
    public static string NameNotIdentifier(string name) => Say(nameof(NameNotIdentifier), new PhraseArgument("name", name));
    public static string KeyTwice(string key) => Say(nameof(KeyTwice), new PhraseArgument("key", key));

    public static string KeyNotTextOrStatic() => Say(nameof(KeyNotTextOrStatic));

    public static string KeyStaticNotText(string member, string type) =>
        Say(nameof(KeyStaticNotText), new PhraseArgument("member", member), new PhraseArgument("type", type));

    public static string ContentTakesOne(string type, string property) =>
        Say(nameof(ContentTakesOne), new PhraseArgument("type", type), new PhraseArgument("property", property));

    public static string NotAttached(string owner, string property, string element) =>
        Say(nameof(NotAttached), new PhraseArgument("owner", owner), new PhraseArgument("property", property),
            new PhraseArgument("element", element));

    public static string PropertyTypeNotFound(string type, string property) =>
        Say(nameof(PropertyTypeNotFound), new PhraseArgument("type", type), new PhraseArgument("property", property));

    public static string TypeNotDerived(string type, string @base, string member, string fullBase) =>
        Say(nameof(TypeNotDerived), new PhraseArgument("type", type), new PhraseArgument("base", @base),
            new PhraseArgument("member", member), new PhraseArgument("fullBase", fullBase));

    public static string NoTypeParser(string where, string type) =>
        Say(nameof(NoTypeParser), new PhraseArgument("where", where), new PhraseArgument("type", type));

    public static string InvalidLiteral(string text, string type, string property, string expected, int line, int position) =>
        Say(nameof(InvalidLiteral), new PhraseArgument("text", text), new PhraseArgument("type", type),
            new PhraseArgument("property", property), new PhraseArgument("expected", expected), new PhraseArgument("line", line),
            new PhraseArgument("position", position));

    public static string ExpectedNumber() => Say(nameof(ExpectedNumber));
    public static string ExpectedWholeNumber() => Say(nameof(ExpectedWholeNumber));
    public static string ExpectedNonNegativeWholeNumber() => Say(nameof(ExpectedNonNegativeWholeNumber));
    public static string FontWeightInvalid(string value) => Say(nameof(FontWeightInvalid), new PhraseArgument("value", value));
    public static string FontStretchInvalid(string value) => Say(nameof(FontStretchInvalid), new PhraseArgument("value", value));
    public static string MarkupItemNoProperty(string type) => Say(nameof(MarkupItemNoProperty), new PhraseArgument("type", type));

    public static string InlineResourceNeedsKey(string property) =>
        Say(nameof(InlineResourceNeedsKey), new PhraseArgument("property", property));

    public static string ResourceWithoutKey(string extension, string property, int line, int position) =>
        Say(nameof(ResourceWithoutKey), new PhraseArgument("extension", extension), new PhraseArgument("property", property),
            new PhraseArgument("line", line), new PhraseArgument("position", position));

    public static string LocalizeIntoBindingProperty(string type, string property, string propertyType) =>
        Say(nameof(LocalizeIntoBindingProperty), new PhraseArgument("type", type), new PhraseArgument("property", property),
            new PhraseArgument("propertyType", propertyType));

    public static string LiveResourceOnPlainProperty(string extension, string key, string type, string property) =>
        Say(nameof(LiveResourceOnPlainProperty), new PhraseArgument("extension", extension), new PhraseArgument("key", key),
            new PhraseArgument("type", type), new PhraseArgument("property", property));

    public static string TemplateBindingOutsideTemplate() => Say(nameof(TemplateBindingOutsideTemplate));
    public static string AncestorTypeNotResolved(string type) => Say(nameof(AncestorTypeNotResolved), new PhraseArgument("type", type));

    public static string AncestorStopTypeNotResolved(string type) =>
        Say(nameof(AncestorStopTypeNotResolved), new PhraseArgument("type", type));

    public static string ConverterInvalid(string binding) => Say(nameof(ConverterInvalid), new PhraseArgument("binding", binding));

    public static string DelayNotMilliseconds(string binding, string delay) =>
        Say(nameof(DelayNotMilliseconds), new PhraseArgument("binding", binding), new PhraseArgument("delay", delay));

    public static string AncestorUnknownArgument(string argument) =>
        Say(nameof(AncestorUnknownArgument), new PhraseArgument("argument", argument));

    public static string SelfUnknownArgument(string argument) => Say(nameof(SelfUnknownArgument), new PhraseArgument("argument", argument));
    public static string UnknownBindingProperty(string name) => Say(nameof(UnknownBindingProperty), new PhraseArgument("name", name));

    public static string DirectiveBelongsOnElement(string directive) =>
        Say(nameof(DirectiveBelongsOnElement), new PhraseArgument("directive", directive));

    public static string DirectiveBelongsInValue(string directive) =>
        Say(nameof(DirectiveBelongsInValue), new PhraseArgument("directive", directive));

    public static string UnknownDirective(string directive) => Say(nameof(UnknownDirective), new PhraseArgument("directive", directive));

    public static string DirectiveMissingValue(string directive) =>
        Say(nameof(DirectiveMissingValue), new PhraseArgument("directive", directive));

    public static string StaticShape(string body) => Say(nameof(StaticShape), new PhraseArgument("body", body));
    public static string StaticTypeNotResolved(string type) => Say(nameof(StaticTypeNotResolved), new PhraseArgument("type", type));

    public static string StaticNoMember(string type, string member) =>
        Say(nameof(StaticNoMember), new PhraseArgument("type", type), new PhraseArgument("member", member));

    public static string KeepAliveExpects(string modes, string mode) =>
        Say(nameof(KeepAliveExpects), new PhraseArgument("modes", modes), new PhraseArgument("mode", mode));

    public static string LoadExpects(string condition) => Say(nameof(LoadExpects), new PhraseArgument("condition", condition));
    public static string LoadTrueHoldsNothing() => Say(nameof(LoadTrueHoldsNothing));
    public static string LoadInsideTemplate() => Say(nameof(LoadInsideTemplate));
    public static string LoadInsideContainer(string container) => Say(nameof(LoadInsideContainer), new PhraseArgument("container", container));
    public static string TargetNameHeldBack(string name) => Say(nameof(TargetNameHeldBack), new PhraseArgument("name", name));
    public static string DataTypeNotResolved(string type) => Say(nameof(DataTypeNotResolved), new PhraseArgument("type", type));
    public static string ViewModelNotResolved(string type) => Say(nameof(ViewModelNotResolved), new PhraseArgument("type", type));
    public static string DataTemplateSetTwoDefaults() => Say(nameof(DataTemplateSetTwoDefaults));
    public static string DataTemplateSetTwoForType(string type) => Say(nameof(DataTemplateSetTwoForType), new PhraseArgument("type", type));

    public static string LocalizeShape(string text) => Say(nameof(LocalizeShape), new PhraseArgument("text", text));

    public static string LocalizeNoString(string table, string key) =>
        Say(nameof(LocalizeNoString), new PhraseArgument("table", table), new PhraseArgument("key", key));

    public static string LocalizeKeyFromBinding(string table, string argument) =>
        Say(nameof(LocalizeKeyFromBinding), new PhraseArgument("table", table), new PhraseArgument("argument", argument));

    public static string LocalizeTableTwice(string table, string argument) =>
        Say(nameof(LocalizeTableTwice), new PhraseArgument("table", table), new PhraseArgument("argument", argument));

    public static string LocalizeBothFromBinding(string tableArgument, string keyArgument) =>
        Say(nameof(LocalizeBothFromBinding), new PhraseArgument("tableArgument", tableArgument),
            new PhraseArgument("keyArgument", keyArgument));

    public static string LocalizeNoTable(string name, string table) =>
        Say(nameof(LocalizeNoTable), new PhraseArgument("name", name), new PhraseArgument("table", table));

    public static string PlaceholdersNone() => Say(nameof(PlaceholdersNone));

    public static string PlaceholdersFilled(string placeholders) =>
        Say(nameof(PlaceholdersFilled), new PhraseArgument("placeholders", placeholders));

    public static string PlaceholderUnknown(string table, string key, string names, string fills) =>
        Say(nameof(PlaceholderUnknown), new PhraseArgument("table", table), new PhraseArgument("key", key),
            new PhraseArgument("names", names), new PhraseArgument("fills", fills));

    public static string PlaceholderMissing(string table, string key, string names, string fills) =>
        Say(nameof(PlaceholderMissing), new PhraseArgument("table", table), new PhraseArgument("key", key),
            new PhraseArgument("names", names), new PhraseArgument("fills", fills));

    public static string BlueprintTooMany(int count, string files) =>
        Say(nameof(BlueprintTooMany), new PhraseArgument("count", count), new PhraseArgument("files", files));

    public static string EntryPointHandWritten(string file, string type) =>
        Say(nameof(EntryPointHandWritten), new PhraseArgument("file", file), new PhraseArgument("type", type));

    public static string BlueprintNoApplication(string file) => Say(nameof(BlueprintNoApplication), new PhraseArgument("file", file));

    public static string BlueprintManyApplications(string file, int count, string applications) =>
        Say(nameof(BlueprintManyApplications), new PhraseArgument("file", file), new PhraseArgument("count", count),
            new PhraseArgument("applications", applications));

    public static string NoRootNamespaceOption() => Say(nameof(NoRootNamespaceOption));

    public static string QuickAccessMenuControls(string file, int line, int position, string command) =>
        Say(nameof(QuickAccessMenuControls), new PhraseArgument("file", file), new PhraseArgument("line", line),
            new PhraseArgument("position", position), new PhraseArgument("command", command));

    public static string NoAutomationId(string file, int line, int position, string type) =>
        Say(nameof(NoAutomationId), new PhraseArgument("file", file), new PhraseArgument("line", line),
            new PhraseArgument("position", position), new PhraseArgument("type", type));

    public static string GenerationFailed(string file, string error) =>
        Say(nameof(GenerationFailed), new PhraseArgument("file", file), new PhraseArgument("error", error));

    public static string NoRootNamespace() => Say(nameof(NoRootNamespace));

    public static string ScenarioNotXml(string problem) => Say(nameof(ScenarioNotXml), new PhraseArgument("problem", problem));
    public static string ScenarioNameNotIdentifier(string name) => Say(nameof(ScenarioNameNotIdentifier), new PhraseArgument("name", name));

    public static string ScenarioNameTaken(string name, string file) =>
        Say(nameof(ScenarioNameTaken), new PhraseArgument("name", name), new PhraseArgument("file", file));

    public static string ScenarioNotASelector(string problem) => Say(nameof(ScenarioNotASelector), new PhraseArgument("problem", problem));

    public static string ScenarioDragNeeds(string step, string attributes) =>
        Say(nameof(ScenarioDragNeeds), new PhraseArgument("step", step), new PhraseArgument("attributes", attributes));

    public static string ScenarioOnlyForDrag(string step, string attribute) =>
        Say(nameof(ScenarioOnlyForDrag), new PhraseArgument("step", step), new PhraseArgument("attribute", attribute));

    public static string ScenarioHeldPenButtons() => Say(nameof(ScenarioHeldPenButtons));
    public static string ScenarioChecksNothing(string step) => Say(nameof(ScenarioChecksNothing), new PhraseArgument("step", step));
    public static string ScenarioVisibleOnlyByName() => Say(nameof(ScenarioVisibleOnlyByName));
    public static string ScenarioNoApplication() => Say(nameof(ScenarioNoApplication));
    public static string ScenarioNotInTestProject() => Say(nameof(ScenarioNotInTestProject));

    public static string ScenarioRootNotScenario(string @namespace) =>
        Say(nameof(ScenarioRootNotScenario), new PhraseArgument("namespace", @namespace));

    public static string ScenarioNotOnStep(string step, string attribute) =>
        Say(nameof(ScenarioNotOnStep), new PhraseArgument("step", step), new PhraseArgument("attribute", attribute));

    private static string Say(string key, params PhraseArgument[] values) => Catalog.Say(key, values);
}
