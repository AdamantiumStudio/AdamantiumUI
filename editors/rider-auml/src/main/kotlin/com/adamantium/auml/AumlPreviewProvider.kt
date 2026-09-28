package com.adamantium.auml

import com.intellij.openapi.fileEditor.FileEditor
import com.intellij.openapi.fileEditor.FileEditorPolicy
import com.intellij.openapi.fileEditor.FileEditorProvider
import com.intellij.openapi.project.DumbAware
import com.intellij.openapi.project.Project
import com.intellij.openapi.vfs.VirtualFile

/**
 * The preview half of the AUML split editor on its own. Not registered: [AumlPreviewFileEditorProvider] pairs it with
 * the platform's text editor.
 */
class AumlPreviewProvider : FileEditorProvider, DumbAware {
    override fun accept(project: Project, file: VirtualFile): Boolean = file.fileType == AumlFileType

    override fun createEditor(project: Project, file: VirtualFile): FileEditor = AumlPreviewFileEditor(project, file)

    override fun getEditorTypeId(): String = "auml-preview"

    override fun getPolicy(): FileEditorPolicy = FileEditorPolicy.PLACE_AFTER_DEFAULT_EDITOR
}
