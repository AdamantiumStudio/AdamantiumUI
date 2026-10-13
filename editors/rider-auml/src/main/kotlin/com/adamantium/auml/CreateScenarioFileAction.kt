package com.adamantium.auml

import com.intellij.ide.actions.CreateFileFromTemplateAction
import com.intellij.ide.actions.CreateFileFromTemplateDialog
import com.intellij.openapi.project.DumbAware
import com.intellij.openapi.project.Project
import com.intellij.psi.PsiDirectory

/** Adds "Scenario" to New/Add: an `.ascenario` file, which the test project's build turns into a test of its own. */
class CreateScenarioFileAction : CreateFileFromTemplateAction(
    "Scenario",
    "Creates an Adamantium UI automation scenario, built into a test",
    ScenarioFileType.getIcon()
), DumbAware {

    override fun buildDialog(project: Project, directory: PsiDirectory, builder: CreateFileFromTemplateDialog.Builder) {
        builder
            .setTitle("New Scenario")
            .addKind("Scenario", ScenarioFileType.getIcon(), "Adamantium Scenario")
    }

    override fun getActionName(directory: PsiDirectory, newName: String, templateName: String): String =
        "Create Scenario: $newName"
}
