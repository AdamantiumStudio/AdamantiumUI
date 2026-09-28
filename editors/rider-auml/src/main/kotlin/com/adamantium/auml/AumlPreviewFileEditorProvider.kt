package com.adamantium.auml

import com.intellij.openapi.fileEditor.FileEditor
import com.intellij.openapi.fileEditor.FileEditorPolicy
import com.intellij.openapi.fileEditor.TextEditor
import com.intellij.openapi.fileEditor.TextEditorWithPreviewProvider
import com.intellij.openapi.project.DumbAware

/**
 * Opens `.auml` files in a split editor: markup and the live preview ([AumlPreviewFileEditor]). Uses the platform's
 * split-editor provider, which keeps folding and editor state.
 */
class AumlPreviewFileEditorProvider : TextEditorWithPreviewProvider(AumlPreviewProvider()), DumbAware {
    override fun createSplitEditor(firstEditor: TextEditor, secondEditor: FileEditor): FileEditor =
        AumlSplitEditor(firstEditor, secondEditor)

    override fun getEditorTypeId(): String = "auml-split-editor"

    // Replace the default text editor with our split (the split still contains a full text editor).
    override fun getPolicy(): FileEditorPolicy = FileEditorPolicy.HIDE_DEFAULT_EDITOR
}
