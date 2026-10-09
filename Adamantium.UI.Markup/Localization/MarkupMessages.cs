namespace Adamantium.UI.Markup.Localization;

/// <summary>What the markup's build and its tooling say - problems, and the descriptions of the x: directives - in
/// the language of <see cref="System.Globalization.CultureInfo.CurrentUICulture"/>; each method is one message.</summary>
public static class MarkupMessages
{
    /// <summary>The catalog the messages are read from.</summary>
    public static MessageCatalog Catalog { get; } = new(typeof(MarkupMessages).Assembly, nameof(MarkupMessages));

    public static string Directive(string name) => Say("Directive" + name);

    public static string XmlnsMissingOnRoot(string element) => Say(nameof(XmlnsMissingOnRoot), ("element", element));
    public static string XmlnsOnlyAtRoot() => Say(nameof(XmlnsOnlyAtRoot));
    public static string MarkupExtensionNotWrapped(string text) => Say(nameof(MarkupExtensionNotWrapped), ("text", text));
    public static string PropertyElementAttribute(string element, string attribute) =>
        Say(nameof(PropertyElementAttribute), ("element", element), ("attribute", attribute));

    public static string PrefixNotDefined(string prefix) => Say(nameof(PrefixNotDefined), ("prefix", prefix));

    public static string AssemblyNotFound(object assembly) => Say(nameof(AssemblyNotFound), ("assembly", assembly));

    public static string AssemblyOfElementNotResolved(object assembly, string element, string @namespace, bool resolved) =>
        Say(nameof(AssemblyOfElementNotResolved), ("assembly", assembly), ("element", element), ("namespace", @namespace),
            ("resolved", resolved));

    public static string XmlNamespaceNotFound(string @namespace) => Say(nameof(XmlNamespaceNotFound), ("namespace", @namespace));

    public static string TypeNotInNamespace(string type, string @namespace) =>
        Say(nameof(TypeNotInNamespace), ("type", type), ("namespace", @namespace));

    public static string TypeNotInAnyAssembly(string type) => Say(nameof(TypeNotInAnyAssembly), ("type", type));
    public static string TypeNotFoundCheckNamespace(string type) => Say(nameof(TypeNotFoundCheckNamespace), ("type", type));

    public static string PropertyNotFoundIn(string property, string type) =>
        Say(nameof(PropertyNotFoundIn), ("property", property), ("type", type));

    public static string UnknownPropertyOnType(string property, string type) =>
        Say(nameof(UnknownPropertyOnType), ("property", property), ("type", type));

    public static string NotCreatable(string type) => Say(nameof(NotCreatable), ("type", type));

    public static string PropertySetTwice(string property, string element) =>
        Say(nameof(PropertySetTwice), ("property", property), ("element", element));

    public static string ReadOnlyProperty(string type, string property) => Say(nameof(ReadOnlyProperty), ("type", type), ("property", property));
    public static string OneValue(string property) => Say(nameof(OneValue), ("property", property));

    public static string WrongValueType(string value, string type, string property) =>
        Say(nameof(WrongValueType), ("value", value), ("type", type), ("property", property));

    public static string TemplateTakesOne(string type) => Say(nameof(TemplateTakesOne), ("type", type));
    public static string NoPlaceForChild(string type) => Say(nameof(NoPlaceForChild), ("type", type));
    public static string ContentSetTwice(string type, string property) => Say(nameof(ContentSetTwice), ("type", type), ("property", property));
    public static string NoPlaceForText(string type) => Say(nameof(NoPlaceForText), ("type", type));
    public static string PartNotFound(string template, string name) => Say(nameof(PartNotFound), ("template", template), ("name", name));

    public static string BindingPathNotFound(string type, string member, string path) =>
        Say(nameof(BindingPathNotFound), ("type", type), ("member", member), ("path", path));

    public static string NameTwice(string name) => Say(nameof(NameTwice), ("name", name));
    public static string NameNotIdentifier(string name) => Say(nameof(NameNotIdentifier), ("name", name));
    public static string KeyTwice(string key) => Say(nameof(KeyTwice), ("key", key));

    public static string ContentTakesOne(string type, string property) =>
        Say(nameof(ContentTakesOne), ("type", type), ("property", property));

    public static string NotAttached(string owner, string property, string element) =>
        Say(nameof(NotAttached), ("owner", owner), ("property", property), ("element", element));

    public static string PropertyTypeNotFound(string type, string property) =>
        Say(nameof(PropertyTypeNotFound), ("type", type), ("property", property));

    public static string TypeNotDerived(string type, string @base, string member, string fullBase) =>
        Say(nameof(TypeNotDerived), ("type", type), ("base", @base), ("member", member), ("fullBase", fullBase));

    public static string NoTypeParser(string where, string type) => Say(nameof(NoTypeParser), ("where", where), ("type", type));

    public static string InvalidLiteral(string text, string type, string property, string expected, int line, int position) =>
        Say(nameof(InvalidLiteral), ("text", text), ("type", type), ("property", property), ("expected", expected),
            ("line", line), ("position", position));

    public static string ExpectedNumber() => Say(nameof(ExpectedNumber));
    public static string ExpectedWholeNumber() => Say(nameof(ExpectedWholeNumber));
    public static string ExpectedNonNegativeWholeNumber() => Say(nameof(ExpectedNonNegativeWholeNumber));
    public static string FontWeightInvalid(string value) => Say(nameof(FontWeightInvalid), ("value", value));
    public static string FontStretchInvalid(string value) => Say(nameof(FontStretchInvalid), ("value", value));
    public static string MarkupItemNoProperty(string type) => Say(nameof(MarkupItemNoProperty), ("type", type));

    public static string InlineResourceNeedsKey(string property) => Say(nameof(InlineResourceNeedsKey), ("property", property));

    public static string ResourceWithoutKey(string extension, string property, int line, int position) =>
        Say(nameof(ResourceWithoutKey), ("extension", extension), ("property", property), ("line", line), ("position", position));

    public static string LiveResourceOnPlainProperty(string extension, string key, string type, string property) =>
        Say(nameof(LiveResourceOnPlainProperty), ("extension", extension), ("key", key), ("type", type), ("property", property));

    public static string TemplateBindingOutsideTemplate() => Say(nameof(TemplateBindingOutsideTemplate));
    public static string AncestorTypeNotResolved(string type) => Say(nameof(AncestorTypeNotResolved), ("type", type));
    public static string AncestorStopTypeNotResolved(string type) => Say(nameof(AncestorStopTypeNotResolved), ("type", type));
    public static string AncestorConverterInvalid() => Say(nameof(AncestorConverterInvalid));
    public static string AncestorUnknownArgument(string argument) => Say(nameof(AncestorUnknownArgument), ("argument", argument));
    public static string SelfConverterInvalid() => Say(nameof(SelfConverterInvalid));
    public static string SelfUnknownArgument(string argument) => Say(nameof(SelfUnknownArgument), ("argument", argument));
    public static string UnknownBindingProperty(string name) => Say(nameof(UnknownBindingProperty), ("name", name));

    public static string DirectiveBelongsOnElement(string directive) => Say(nameof(DirectiveBelongsOnElement), ("directive", directive));
    public static string DirectiveBelongsInValue(string directive) => Say(nameof(DirectiveBelongsInValue), ("directive", directive));
    public static string UnknownDirective(string directive) => Say(nameof(UnknownDirective), ("directive", directive));
    public static string DirectiveMissingValue(string directive) => Say(nameof(DirectiveMissingValue), ("directive", directive));
    public static string StaticShape(string body) => Say(nameof(StaticShape), ("body", body));
    public static string StaticTypeNotResolved(string type) => Say(nameof(StaticTypeNotResolved), ("type", type));
    public static string StaticNoMember(string type, string member) => Say(nameof(StaticNoMember), ("type", type), ("member", member));
    public static string KeepAliveExpects(string modes, string mode) => Say(nameof(KeepAliveExpects), ("modes", modes), ("mode", mode));
    public static string LoadExpects(string condition) => Say(nameof(LoadExpects), ("condition", condition));
    public static string LoadTrueHoldsNothing() => Say(nameof(LoadTrueHoldsNothing));
    public static string LoadInsideTemplate() => Say(nameof(LoadInsideTemplate));
    public static string LoadInsideContainer(string container) => Say(nameof(LoadInsideContainer), ("container", container));
    public static string TargetNameHeldBack(string name) => Say(nameof(TargetNameHeldBack), ("name", name));
    public static string DataTypeNotResolved(string type) => Say(nameof(DataTypeNotResolved), ("type", type));
    public static string ViewModelNotResolved(string type) => Say(nameof(ViewModelNotResolved), ("type", type));
    public static string DataTemplateSetTwoDefaults() => Say(nameof(DataTemplateSetTwoDefaults));
    public static string DataTemplateSetTwoForType(string type) => Say(nameof(DataTemplateSetTwoForType), ("type", type));

    public static string LocalizeShape(string text) => Say(nameof(LocalizeShape), ("text", text));
    public static string LocalizeNoString(string table, string key) => Say(nameof(LocalizeNoString), ("table", table), ("key", key));

    public static string LocalizeKeyFromBinding(string table, string argument) =>
        Say(nameof(LocalizeKeyFromBinding), ("table", table), ("argument", argument));

    public static string LocalizeTableTwice(string table, string argument) =>
        Say(nameof(LocalizeTableTwice), ("table", table), ("argument", argument));

    public static string LocalizeBothFromBinding(string tableArgument, string keyArgument) =>
        Say(nameof(LocalizeBothFromBinding), ("tableArgument", tableArgument), ("keyArgument", keyArgument));

    public static string LocalizeNoTable(string name, string table) => Say(nameof(LocalizeNoTable), ("name", name), ("table", table));
    public static string PlaceholdersNone() => Say(nameof(PlaceholdersNone));
    public static string PlaceholdersFilled(string placeholders) => Say(nameof(PlaceholdersFilled), ("placeholders", placeholders));

    public static string PlaceholderUnknown(string table, string key, string names, string fills) =>
        Say(nameof(PlaceholderUnknown), ("table", table), ("key", key), ("names", names), ("fills", fills));

    public static string PlaceholderMissing(string table, string key, string names, string fills) =>
        Say(nameof(PlaceholderMissing), ("table", table), ("key", key), ("names", names), ("fills", fills));

    public static string BlueprintTooMany(int count, string files) => Say(nameof(BlueprintTooMany), ("count", count), ("files", files));
    public static string EntryPointHandWritten(string file, string type) => Say(nameof(EntryPointHandWritten), ("file", file), ("type", type));
    public static string BlueprintNoApplication(string file) => Say(nameof(BlueprintNoApplication), ("file", file));

    public static string BlueprintManyApplications(string file, int count, string applications) =>
        Say(nameof(BlueprintManyApplications), ("file", file), ("count", count), ("applications", applications));

    public static string NoRootNamespaceOption() => Say(nameof(NoRootNamespaceOption));

    public static string QuickAccessMenuControls(string file, int line, int position, string command) =>
        Say(nameof(QuickAccessMenuControls), ("file", file), ("line", line), ("position", position), ("command", command));

    public static string NoAutomationId(string file, int line, int position, string type) =>
        Say(nameof(NoAutomationId), ("file", file), ("line", line), ("position", position), ("type", type));

    public static string GenerationFailed(string file, string error) => Say(nameof(GenerationFailed), ("file", file), ("error", error));

    public static string NoRootNamespace() => Say(nameof(NoRootNamespace));

    public static string LanguageFileUnnamed(string file, string extension) =>
        Say(nameof(LanguageFileUnnamed), ("file", file), ("extension", extension));

    public static string LanguageTableNameInvalid(string table) => Say(nameof(LanguageTableNameInvalid), ("table", table));
    public static string LanguageNameInvalid(string language) => Say(nameof(LanguageNameInvalid), ("language", language));
    public static string LanguageFileRoot(string root, string phrase) => Say(nameof(LanguageFileRoot), ("root", root), ("phrase", phrase));

    public static string LanguageRootAttribute(string root, string attribute) =>
        Say(nameof(LanguageRootAttribute), ("root", root), ("attribute", attribute));

    public static string LanguageFormatTwice(string format) => Say(nameof(LanguageFormatTwice), ("format", format));

    public static string LanguageStrayElement(string element, string phrase, string format) =>
        Say(nameof(LanguageStrayElement), ("element", element), ("phrase", phrase), ("format", format));

    public static string PhraseNeedsKey(string phrase) => Say(nameof(PhraseNeedsKey), ("phrase", phrase));
    public static string PhraseKeyInvalid(string key) => Say(nameof(PhraseKeyInvalid), ("key", key));
    public static string PhraseKeyTwice(string key) => Say(nameof(PhraseKeyTwice), ("key", key));
    public static string PhraseHoldsElements(string key) => Say(nameof(PhraseHoldsElements), ("key", key));
    public static string PhraseTextTwice(string key, string text) => Say(nameof(PhraseTextTwice), ("key", key), ("text", text));

    public static string PhraseCasesNeedChooser(string key, string count, string select) =>
        Say(nameof(PhraseCasesNeedChooser), ("key", key), ("count", count), ("select", select));

    public static string PhraseStrayAttribute(string phrase, string text, string count, string select, string attribute) =>
        Say(nameof(PhraseStrayAttribute), ("phrase", phrase), ("text", text), ("count", count), ("select", select),
            ("attribute", attribute));

    public static string PhraseTextAndCases(string key) => Say(nameof(PhraseTextAndCases), ("key", key));
    public static string ChooserInvalid(string chooser, string example) => Say(nameof(ChooserInvalid), ("chooser", chooser), ("example", example));
    public static string NotAPluralForm(string name, string forms) => Say(nameof(NotAPluralForm), ("name", name), ("forms", forms));
    public static string PhraseNeedsOther(string key, string other) => Say(nameof(PhraseNeedsOther), ("key", key), ("other", other));

    public static string LanguageHasNoForm(string language, string form, string forms) =>
        Say(nameof(LanguageHasNoForm), ("language", language), ("form", form), ("forms", forms));

    public static string PhraseLacksForms(string key, IReadOnlyCollection<string> forms, string language, string other) =>
        Say(forms.Count > 1 ? nameof(PhraseLacksForms) : "PhraseLacksForm", ("key", key), ("forms", string.Join(", ", forms)),
            ("language", language), ("other", other));

    public static string PluralRulesUnknown(string language, string one, string other) =>
        Say(nameof(PluralRulesUnknown), ("language", language), ("one", one), ("other", other));

    public static string CaseNameInvalid(string name, string key) => Say(nameof(CaseNameInvalid), ("name", name), ("key", key));
    public static string PhraseNoCases(string key, string chooser) => Say(nameof(PhraseNoCases), ("key", key), ("chooser", chooser));
    public static string NotAFormat(string name, string formats) => Say(nameof(NotAFormat), ("name", name), ("formats", formats));
    public static string FirstDayOfWeekInvalid(string value) => Say(nameof(FirstDayOfWeekInvalid), ("value", value));

    public static string LanguageInAnotherFile(string language, string table) =>
        Say(nameof(LanguageInAnotherFile), ("language", language), ("table", table));

    public static string KeyIsTableMember(string key, string table) => Say(nameof(KeyIsTableMember), ("key", key), ("table", table));
    public static string KeyNotInBase(string key, string file) => Say(nameof(KeyNotInBase), ("key", key), ("file", file));

    public static string ChooserDiffers(string key, string chooser, string other) =>
        Say(nameof(ChooserDiffers), ("key", key), ("chooser", chooser), ("other", other));

    public static string KeyIsCaseName(string key) => Say(nameof(KeyIsCaseName), ("key", key));

    public static string LacksCases(string key, IReadOnlyCollection<string> cases) =>
        Say(cases.Count > 1 ? nameof(LacksCases) : "LacksCase", ("key", key), ("cases", string.Join(", ", cases)));

    public static string TableNotFound(string table, string file, string language) =>
        Say(nameof(TableNotFound), ("table", table), ("file", file), ("language", language));

    public static string TableAmbiguous(string table, string tables) => Say(nameof(TableAmbiguous), ("table", table), ("tables", tables));
    public static string TableHasNoKey(string table, string key) => Say(nameof(TableHasNoKey), ("table", table), ("key", key));
    public static string FormatsInLibrary() => Say(nameof(FormatsInLibrary));
    public static string FormatsTwice(string language) => Say(nameof(FormatsTwice), ("language", language));
    public static string PlaceholdersExtra(string names) => Say(nameof(PlaceholdersExtra), ("names", names));
    public static string PlaceholdersLacking(string names) => Say(nameof(PlaceholdersLacking), ("names", names));

    public static string PlaceholderProblems(string key, IReadOnlyList<string> problems) => problems.Count > 1
        ? Say(nameof(PlaceholderProblems), ("key", key), ("first", problems[0]), ("second", problems[1]))
        : Say("PlaceholderProblem", ("key", key), ("problem", problems[0]));

    public static string LanguageLacksStrings(string file, int count, string table, string shown, string language) =>
        Say(nameof(LanguageLacksStrings), ("file", file), ("count", count), ("table", table), ("shown", shown),
            ("language", language));

    public static string AndMore(string shown, int count) => Say(nameof(AndMore), ("shown", shown), ("count", count));
    public static string BraceNotClosed() => Say(nameof(BraceNotClosed));
    public static string PlaceholderNoName(string text) => Say(nameof(PlaceholderNoName), ("text", text));
    public static string BraceNotOpened() => Say(nameof(BraceNotOpened));

    private static string Say(string key, params (string Name, object Value)[] values) => Catalog.Say(key, values);
}
