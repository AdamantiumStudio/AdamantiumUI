package com.adamantium.auml

import com.intellij.codeInsight.daemon.impl.HighlightInfo
import com.intellij.codeInsight.daemon.impl.HighlightInfoFilter
import com.intellij.lang.annotation.HighlightSeverity
import com.intellij.psi.PsiFile
import com.intellij.psi.util.PsiTreeUtil
import com.intellij.psi.xml.XmlAttribute

/**
 * Suppresses "URI is not registered" on every xmlns declaration - a `clr-namespace:` or a URI some assembly declares
 * with [XmlnsDefinition] - matched by PSI structure rather than message text; the language server validates them
 * against the project's references instead.
 */
class AumlNamespaceHighlightFilter : HighlightInfoFilter {
    override fun accept(info: HighlightInfo, file: PsiFile?): Boolean {
        if (file?.viewProvider?.virtualFile?.fileType != AumlFileType) return true
        if (info.severity < HighlightSeverity.WEAK_WARNING) return true   // leave info/markers untouched

        val leaf = file.findElementAt(info.startOffset) ?: return true
        val attribute = PsiTreeUtil.getParentOfType(leaf, XmlAttribute::class.java) ?: return true

        return !(attribute.name == "xmlns" || attribute.name.startsWith("xmlns:"))
    }
}
