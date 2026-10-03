package com.adamantium.auml

import com.intellij.lang.xml.XMLLanguage
import com.intellij.openapi.fileTypes.LanguageFileType
import com.intellij.openapi.util.IconLoader
import javax.swing.Icon

/**
 * The `.alang` language file of Adamantium UI (`Strings.en.alang`): one table of strings in one language. Backed by
 * the XML language for highlighting, structure and folding; completion and checks come from the AUML language server.
 */
object AlangFileType : LanguageFileType(XMLLanguage.INSTANCE) {
    private val icon: Icon = IconLoader.getIcon("/icons/alang.svg", AlangFileType::class.java)

    override fun getName(): String = "ALANG"
    override fun getDescription(): String = "Adamantium UI language file"
    override fun getDefaultExtension(): String = "alang"
    override fun getIcon(): Icon = icon
}
