package com.adamantium.auml

import com.intellij.openapi.actionSystem.ActionUpdateThread
import com.intellij.openapi.actionSystem.AnAction
import com.intellij.openapi.actionSystem.AnActionEvent

/** Tools | Install Adamantium UI Templates: installs or updates the project templates for File | New Solution. */
internal class InstallAumlTemplatesAction : AnAction() {
    override fun actionPerformed(e: AnActionEvent) = AumlTemplates.install()

    override fun getActionUpdateThread(): ActionUpdateThread = ActionUpdateThread.BGT
}
