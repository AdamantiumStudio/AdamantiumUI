package com.adamantium.auml

import com.intellij.codeInsight.editorActions.TypedHandlerDelegate
import com.intellij.openapi.editor.Editor
import com.intellij.openapi.fileTypes.FileType
import com.intellij.openapi.project.Project
import com.intellij.psi.PsiDocumentManager
import com.intellij.psi.PsiFile
import com.intellij.psi.util.PsiTreeUtil
import com.intellij.psi.xml.XmlAttributeValue

/**
 * Braces in an attribute value come in pairs, as the Pro text editor pairs them: a `{` typed in a value closes at once -
 * `Text="{|}"` - unless a word follows the caret, and a `}` typed before a `}` the value does not need steps over it.
 */
class AumlBraceTypedHandler : TypedHandlerDelegate() {
    override fun beforeCharTyped(c: Char, project: Project, editor: Editor, file: PsiFile, fileType: FileType): Result {
        val type = file.viewProvider.virtualFile.fileType
        if ((c != '{' && c != '}') || (type != AumlFileType && type != AlangFileType)) return Result.CONTINUE

        val document = editor.document
        val offset = editor.caretModel.offset
        if (editor.selectionModel.hasSelection()) return Result.CONTINUE

        PsiDocumentManager.getInstance(project).commitDocument(document)
        val value = PsiTreeUtil.getParentOfType(file.findElementAt(offset), XmlAttributeValue::class.java, false)
            ?: return Result.CONTINUE
        val range = value.textRange
        if (offset <= range.startOffset) return Result.CONTINUE

        val text = document.charsSequence
        val next = if (offset < text.length) text[offset] else '\n'
        if (c == '}') {
            val braces = value.text
            if (next != '}' || braces.count { it == '{' } > braces.count { it == '}' }) return Result.CONTINUE
            editor.caretModel.moveToOffset(offset + 1)
            return Result.STOP
        }

        if (!next.isWhitespace() && next !in CLOSE_BEFORE) return Result.CONTINUE
        document.insertString(offset, "{}")
        editor.caretModel.moveToOffset(offset + 1)
        return Result.STOP
    }

    private companion object {
        const val CLOSE_BEFORE = ";:.,=}])>\"'"
    }
}
