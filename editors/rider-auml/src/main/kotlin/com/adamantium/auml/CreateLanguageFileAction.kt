package com.adamantium.auml

import com.intellij.openapi.actionSystem.ActionUpdateThread
import com.intellij.openapi.actionSystem.AnAction
import com.intellij.openapi.actionSystem.AnActionEvent
import com.intellij.openapi.actionSystem.LangDataKeys
import com.intellij.openapi.command.WriteCommandAction
import com.intellij.openapi.fileEditor.FileDocumentManager
import com.intellij.openapi.fileEditor.OpenFileDescriptor
import com.intellij.openapi.project.DumbAware
import com.intellij.openapi.project.Project
import com.intellij.openapi.ui.ComboBox
import com.intellij.openapi.ui.DialogWrapper
import com.intellij.openapi.ui.ValidationInfo
import com.intellij.openapi.vfs.VfsUtil
import com.intellij.openapi.vfs.VirtualFile
import com.intellij.ui.dsl.builder.AlignX
import com.intellij.ui.dsl.builder.panel
import java.util.Locale
import javax.swing.JComponent
import javax.xml.parsers.DocumentBuilderFactory
import org.w3c.dom.Element

private const val EXTENSION = "alang"
private const val ROOT = "Language"
private const val PHRASE = "Phrase"
private const val TEXT = "Text"
private const val COUNT = "Count"
private const val SELECT = "Select"
private val FORMS = listOf("Zero", "One", "Two", "Few", "Many", "Other")

// The forms each language counts in, as the build's PluralRules has them; a language not listed counts as English.
private val ONE_FEW_MANY = listOf("One", "Few", "Many", "Other")
private val FORMS_OF: Map<String, List<String>> = buildMap {
    listOf("ja", "zh", "ko", "vi", "th", "id", "ms", "lo", "my", "km", "jv", "yue").forEach { put(it, listOf("Other")) }
    listOf("ru", "be", "pl", "cs", "sk", "lt").forEach { put(it, ONE_FEW_MANY) }
    put("lv", listOf("Zero", "One", "Other"))
    listOf("ro", "hr", "sr", "bs").forEach { put(it, listOf("One", "Few", "Other")) }
    put("sl", listOf("One", "Two", "Few", "Other"))
    put("ar", FORMS)
    put("he", listOf("One", "Two", "Other"))
}

private fun formsOf(language: String): List<String> {
    var name = language
    while (true) {
        FORMS_OF.entries.firstOrNull { it.key.equals(name, ignoreCase = true) }?.let { return it.value }
        val dash = name.lastIndexOfAny(charArrayOf('-', '_'))
        if (dash <= 0) return listOf("One", "Other")
        name = name.substring(0, dash)
    }
}

/**
 * A string of a base file: its key, its text, and - for one written in cases - the placeholder it counts by, or the one
 * whose value selects a case with the cases it names.
 */
private data class BaseString(
    val key: String,
    val text: String,
    val count: String? = null,
    val select: String? = null,
    val cases: List<String> = emptyList()
)

/**
 * New | Language File: a table's base file in the project's base language (`NeutralLanguage`, else `en`), or a
 * translation of a table in the folder that lists every string of its base file, empty, under its base text - ready to
 * hand to a translator.
 */
class CreateLanguageFileAction : AnAction(
    "Language File",
    "Creates an Adamantium UI language file: a table's base strings or their translation",
    AlangFileType.getIcon()
), DumbAware {

    override fun getActionUpdateThread(): ActionUpdateThread = ActionUpdateThread.BGT

    override fun update(e: AnActionEvent) {
        e.presentation.isEnabledAndVisible =
            e.project != null && e.getData(LangDataKeys.IDE_VIEW)?.directories?.isNotEmpty() == true
    }

    override fun actionPerformed(e: AnActionEvent) {
        val project = e.project ?: return
        val directory = e.getData(LangDataKeys.IDE_VIEW)?.orChooseDirectory?.virtualFile ?: return

        val tables = tablesIn(directory)
        val neutral = neutralLanguage(directory)
        val dialog = LanguageFileDialog(project, directory, tables, neutral)
        if (!dialog.showAndGet()) return

        val table = dialog.table
        val language = dialog.language
        val base = if (language.equals(neutral, ignoreCase = true)) emptyList() else baseStrings(directory, table, neutral)
        val content = content(base, language)
        val caret = if (base.isEmpty()) content.indexOf("\n    \n") + 5 else Regex("=\"\"").find(content)!!.range.first + 2

        var created: VirtualFile? = null
        WriteCommandAction.runWriteCommandAction(project, "Create Language File", null, {
            val file = directory.createChildData(this, "$table.$language.$EXTENSION")
            VfsUtil.saveText(file, content)
            created = file
        })
        created?.let { OpenFileDescriptor(project, it, caret).navigate(true) }
    }

    // A base file starts empty; a translation lists the base strings, each under its base text - one the base counts
    // with the forms the new language has.
    private fun content(base: List<BaseString>, language: String): String {
        if (base.isEmpty()) return "<$ROOT>\n    \n</$ROOT>\n"
        val entries = base.joinToString("") { string ->
            val comment = string.text.replace("\r", "").replace('\n', ' ').trim().replace("--", "- -")
            val said = when {
                string.count != null -> "$COUNT=\"${string.count}\" " + formsOf(language).joinToString(" ") { "$it=\"\"" }
                string.select != null -> "$SELECT=\"${string.select}\" " + string.cases.joinToString(" ") { "$it=\"\"" }
                else -> "$TEXT=\"\""
            }
            (if (comment.isEmpty()) "" else "    <!-- $comment -->\n") + "    <$PHRASE Key=\"${string.key}\" $said/>\n"
        }
        return "<$ROOT>\n$entries</$ROOT>\n"
    }

    /** The tables of the folder's language files, each with the languages it already has. */
    private fun tablesIn(directory: VirtualFile): Map<String, Set<String>> = directory.children
        .filter { it.extension.equals(EXTENSION, ignoreCase = true) }
        .mapNotNull { file ->
            val name = file.nameWithoutExtension
            val dot = name.lastIndexOf('.')
            if (dot <= 0 || dot == name.length - 1) null else name.substring(0, dot) to name.substring(dot + 1)
        }
        .groupBy({ it.first }, { it.second })
        .mapValues { (_, languages) -> languages.map { it.lowercase() }.toSet() }

    /** The strings of the table's base file; empty when there is none or it does not read. */
    private fun baseStrings(directory: VirtualFile, table: String, neutral: String): List<BaseString> {
        val file = directory.findChild("$table.$neutral.$EXTENSION") ?: return emptyList()
        val text = FileDocumentManager.getInstance().getDocument(file)?.text ?: String(file.contentsToByteArray(), Charsets.UTF_8)
        return try {
            val document = DocumentBuilderFactory.newInstance().newDocumentBuilder().parse(text.byteInputStream(Charsets.UTF_8))
            val strings = document.documentElement.getElementsByTagName(PHRASE)
            (0 until strings.length)
                .map { strings.item(it) as Element }
                .filter { it.getAttribute("Key").isNotEmpty() }
                .map {
                    // Beside Select, Text and Count are cases: an enum may have members of those names.
                    val select = if (it.hasAttribute(SELECT)) it.getAttribute(SELECT) else null
                    val count = if (select == null && it.hasAttribute(COUNT)) it.getAttribute(COUNT) else null
                    val named = if (select != null) setOf("Key", SELECT) else setOf("Key", TEXT, COUNT, SELECT)
                    val cases = (0 until it.attributes.length).map { i -> it.attributes.item(i).nodeName }.filter { n -> n !in named }
                    val text = when {
                        count != null || select != null -> it.getAttribute("Other").ifEmpty { cases.firstOrNull()?.let(it::getAttribute) ?: "" }
                        it.hasAttribute(TEXT) -> it.getAttribute(TEXT)
                        else -> it.textContent
                    }
                    BaseString(it.getAttribute("Key"), text, count, select, if (select != null) cases else emptyList())
                }
        } catch (e: Exception) {
            emptyList()
        }
    }

    /** NeutralLanguage of the project the folder is in (its .csproj, then the Directory.Build.props above it), else en. */
    private fun neutralLanguage(directory: VirtualFile): String {
        var folder: VirtualFile? = directory
        var csproj: VirtualFile? = null
        while (folder != null && csproj == null) {
            csproj = folder.children.firstOrNull { it.extension.equals("csproj", ignoreCase = true) }
            folder = folder.parent
        }

        val sources = mutableListOf<VirtualFile>()
        csproj?.let { sources.add(it) }
        var above = csproj?.parent
        while (above != null) {
            val props = above.findChild("Directory.Build.props")
            if (props != null) {
                sources.add(props)
                break
            }
            above = above.parent
        }

        val property = Regex("<NeutralLanguage>\\s*([^<$]+?)\\s*</NeutralLanguage>")
        for (source in sources) {
            val text = FileDocumentManager.getInstance().getDocument(source)?.text ?: String(source.contentsToByteArray(), Charsets.UTF_8)
            property.find(text)?.let { return it.groupValues[1] }
        }
        return "en"
    }
}

private class LanguageFileDialog(
    project: Project,
    private val directory: VirtualFile,
    private val tables: Map<String, Set<String>>,
    private val neutral: String
) : DialogWrapper(project) {

    private val tableBox = ComboBox(tables.keys.sorted().ifEmpty { listOf("Strings") }.toTypedArray()).apply { isEditable = true }
    private val languageBox = ComboBox(languages()).apply { isEditable = true }

    init {
        title = "New Language File"
        val firstTable = tables.keys.minOrNull()
        val hasBase = firstTable?.let { tables[it] }?.contains(neutral.lowercase()) == true
        languageBox.editor.item = if (hasBase) "" else neutral
        init()
    }

    val table: String
        get() = text(tableBox)

    val language: String
        get() = text(languageBox).substringBefore(' ')

    override fun createCenterPanel(): JComponent = panel {
        row("Table:") { cell(tableBox).align(AlignX.FILL) }
        row("Language:") { cell(languageBox).align(AlignX.FILL) }
        row {
            comment(
                "The base language is $neutral: its file is the table. A file in another language translates it and " +
                    "starts with every string of the base file, empty, under its base text."
            )
        }
    }

    override fun getPreferredFocusedComponent(): JComponent = languageBox

    override fun continuousValidation(): Boolean = true

    override fun doValidate(): ValidationInfo? {
        if (!Regex("[A-Za-z_][A-Za-z0-9_]*").matches(table)) {
            return ValidationInfo("A table becomes a class: its name is letters, digits and _", tableBox)
        }
        val locale = Locale.forLanguageTag(language)
        if (!Regex("[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*").matches(language) || locale.language.isEmpty()) {
            return ValidationInfo("Not a language such as en, ru or pt-BR", languageBox)
        }
        if (tables[table]?.contains(language.lowercase()) == true || directory.findChild("$table.$language.$EXTENSION") != null) {
            return ValidationInfo("$table already has a $language file here", languageBox)
        }
        return null
    }

    private fun text(box: ComboBox<String>): String = ((box.editor.item ?: box.selectedItem) as? String ?: "").trim()

    // Language and region names as .NET cultures write them, each with its English name.
    private fun languages(): Array<String> = Locale.getAvailableLocales()
        .filter { it.language.isNotEmpty() && it.variant.isEmpty() && it.script.isEmpty() && !it.hasExtensions() }
        .map { it.toLanguageTag() to it.getDisplayName(Locale.ENGLISH) }
        .distinctBy { it.first }
        .sortedBy { it.first }
        .map { (tag, name) -> "$tag  $name" }
        .toTypedArray()
}
