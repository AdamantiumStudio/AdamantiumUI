package com.adamantium.auml

import com.intellij.codeInsight.daemon.impl.HighlightInfo
import com.intellij.codeInsight.daemon.impl.HighlightInfoFilter
import com.intellij.lang.annotation.HighlightSeverity
import com.intellij.psi.PsiFile
import com.intellij.psi.util.PsiTreeUtil
import com.intellij.psi.xml.XmlAttribute

/**
 * Suppresses "URI is not registered" on `clr-namespace:` xmlns declarations, matched by PSI structure rather than
 * message text; the language server validates them instead.
 */
class AumlNamespaceHighlightFilter : HighlightInfoFilter {
    override fun accept(info: HighlightInfo, file: PsiFile?): Boolean {
        if (file?.fileType != AumlFileType) return true
        if (info.severity < HighlightSeverity.WEAK_WARNING) return true   // leave info/markers untouched

        val leaf = file.findElementAt(info.startOffset) ?: return true
        val attribute = PsiTreeUtil.getParentOfType(leaf, XmlAttribute::class.java) ?: return true

        val isXmlns = attribute.name == "xmlns" || attribute.name.startsWith("xmlns:")
        val value = attribute.value ?: return true
        return !(isXmlns && value.startsWith("clr-namespace:"))
    }
}
