package com.adamantium.auml

import com.intellij.codeInsight.editorActions.TypedHandlerDelegate
import com.intellij.openapi.editor.Editor
import com.intellij.openapi.fileTypes.FileType
import com.intellij.openapi.project.Project
import com.intellij.psi.PsiDocumentManager
import com.intellij.psi.PsiFile
import com.intellij.psi.xml.XmlTag
import com.intellij.psi.xml.XmlTokenType
import com.intellij.xml.util.XmlTagUtil

/**
 * A slash typed before the end of an empty element's opening tag makes the element self-closing: `<Border |></Border>`
 * becomes `<Border /|>`, its closing tag gone. An element with content only gets the slash.
 */
class AumlSelfClosingTypedHandler : TypedHandlerDelegate() {
    override fun beforeCharTyped(c: Char, project: Project, editor: Editor, file: PsiFile, fileType: FileType): Result {
        val type = file.viewProvider.virtualFile.fileType
        if (c != '/' || (type != AumlFileType && type != AlangFileType)) return Result.CONTINUE

        val document = editor.document
        val offset = editor.caretModel.offset
        if (offset >= document.textLength || document.charsSequence[offset] != '>') return Result.CONTINUE

        PsiDocumentManager.getInstance(project).commitDocument(document)
        val end = file.findElementAt(offset) ?: return Result.CONTINUE
        val tag = end.parent as? XmlTag ?: return Result.CONTINUE
        if (end.node.elementType != XmlTokenType.XML_TAG_END || end == tag.lastChild ||
            XmlTagUtil.getEndTagNameElement(tag) == null || tag.lastChild.node.elementType != XmlTokenType.XML_TAG_END ||
            tag.subTags.isNotEmpty() || tag.value.text.isNotBlank()
        ) return Result.CONTINUE

        document.replaceString(offset, tag.textRange.endOffset, "/>")
        editor.caretModel.moveToOffset(offset + 1)
        return Result.STOP
    }
}
