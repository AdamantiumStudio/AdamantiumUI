package com.adamantium.auml

import com.intellij.lang.xml.XMLLanguage
import com.intellij.openapi.fileTypes.LanguageFileType
import com.intellij.openapi.util.IconLoader
import javax.swing.Icon

/**
 * The `.ascenario` scenario of Adamantium UI automation: steps that drive the application, built into a test. Backed by
 * the XML language for highlighting, structure and folding; completion and checks come from the AUML language server.
 */
object ScenarioFileType : LanguageFileType(XMLLanguage.INSTANCE) {
    private val icon: Icon = IconLoader.getIcon("/icons/ascenario.svg", ScenarioFileType::class.java)

    override fun getName(): String = "ASCENARIO"
    override fun getDescription(): String = "Adamantium UI automation scenario"
    override fun getDefaultExtension(): String = "ascenario"
    override fun getIcon(): Icon = icon
}
