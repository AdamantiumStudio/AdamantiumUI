package com.adamantium.auml

import com.intellij.ide.actions.CreateFileFromTemplateAction
import com.intellij.ide.actions.CreateFileFromTemplateDialog
import com.intellij.openapi.project.DumbAware
import com.intellij.openapi.project.Project
import com.intellij.psi.PsiDirectory

/**
 * Adds "AUML File" to New/Add: creates a `.auml` file of the chosen kind (Window, View, Docking Pane, Ribbon Tab, Ribbon
 * Group, Empty, Theme, StyleSet, ResourceDictionary) from the bundled fileTemplates/internal templates.
 */
class CreateAumlFileAction : CreateFileFromTemplateAction(
    "AUML File",
    "Creates a new Adamantium UI markup file",
    AumlFileType.getIcon()
), DumbAware {

    override fun buildDialog(project: Project, directory: PsiDirectory, builder: CreateFileFromTemplateDialog.Builder) {
        builder
            .setTitle("New AUML File")
            .addKind("Window", AumlFileType.getIcon(), "Adamantium Window")
            .addKind("View", AumlFileType.getIcon(), "Adamantium View")
            .addKind("Docking Pane", AumlFileType.getIcon(), "Adamantium DockingPane")
            .addKind("Ribbon Tab", AumlFileType.getIcon(), "Adamantium RibbonTab")
            .addKind("Ribbon Group", AumlFileType.getIcon(), "Adamantium RibbonGroup")
            .addKind("Empty", AumlFileType.getIcon(), "Adamantium Empty")
            .addKind("Theme", AumlFileType.getIcon(), "Adamantium Theme")
            .addKind("Style Set", AumlFileType.getIcon(), "Adamantium StyleSet")
            .addKind("Resource Dictionary", AumlFileType.getIcon(), "Adamantium ResourceDictionary")
    }

    override fun getActionName(directory: PsiDirectory, newName: String, templateName: String): String =
        "Create AUML File: $newName"
}
