package com.adamantium.auml

import com.intellij.icons.AllIcons
import com.intellij.ide.util.PropertiesComponent
import com.intellij.openapi.actionSystem.ActionGroup
import com.intellij.openapi.actionSystem.ActionUpdateThread
import com.intellij.openapi.actionSystem.AnActionEvent
import com.intellij.openapi.actionSystem.DefaultActionGroup
import com.intellij.openapi.actionSystem.Separator
import com.intellij.openapi.actionSystem.ToggleAction
import com.intellij.openapi.fileEditor.FileEditor
import com.intellij.openapi.fileEditor.TextEditor
import com.intellij.openapi.fileEditor.TextEditorWithPreview
import com.intellij.openapi.project.DumbAware

/**
 * The AUML markup and its live preview. Beside the platform's three layouts (editor, both, preview) it offers to put
 * the preview under the markup instead of beside it; the choice is remembered for every AUML file.
 */
class AumlSplitEditor(editor: TextEditor, preview: FileEditor) :
    TextEditorWithPreview(editor, preview, "AUML", Layout.SHOW_EDITOR_AND_PREVIEW, previewBelow) {

    override fun createViewActionGroup(): ActionGroup = DefaultActionGroup(
        showEditorAction, showEditorAndPreviewAction, showPreviewAction, Separator.create(), PreviewBelowAction())

    private inner class PreviewBelowAction :
        ToggleAction("Preview Below", "Place the preview under the markup instead of beside it", AllIcons.Actions.SplitHorizontally),
        DumbAware {

        override fun isSelected(e: AnActionEvent): Boolean = isVerticalSplit()

        override fun setSelected(e: AnActionEvent, state: Boolean) {
            setVerticalSplit(state)
            previewBelow = state
        }

        override fun getActionUpdateThread(): ActionUpdateThread = ActionUpdateThread.EDT
    }

    private companion object {
        private const val PREVIEW_BELOW_KEY = "adamantium.auml.previewBelow"

        var previewBelow: Boolean
            get() = PropertiesComponent.getInstance().getBoolean(PREVIEW_BELOW_KEY, false)
            set(value) = PropertiesComponent.getInstance().setValue(PREVIEW_BELOW_KEY, value, false)
    }
}
