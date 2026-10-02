package com.adamantium.auml

import com.intellij.execution.ExecutionException
import com.intellij.execution.configurations.GeneralCommandLine
import com.intellij.execution.process.CapturingProcessHandler
import com.intellij.execution.process.ProcessOutput
import com.intellij.ide.util.PropertiesComponent
import com.intellij.notification.NotificationAction
import com.intellij.notification.NotificationGroupManager
import com.intellij.notification.NotificationType
import com.intellij.openapi.diagnostic.logger
import com.intellij.openapi.progress.ProgressIndicator
import com.intellij.openapi.progress.ProgressManager
import com.intellij.openapi.progress.Task
import java.nio.charset.StandardCharsets

/**
 * The Adamantium UI project templates of `dotnet new`: with them installed, File | New Solution creates Adamantium UI
 * applications and libraries.
 */
internal object AumlTemplates {
    private const val DONT_ASK_KEY = "adamantium.auml.templatesOffer.dontAsk"
    private const val OFFER_GROUP = "Adamantium AUML templates offer"
    private const val RESULT_GROUP = "Adamantium AUML"
    private const val TEMPLATES_PACKAGE = "Adamantium.UI.Templates"
    private const val APP_TEMPLATE = "adamantium-app"
    private const val TEMPLATE_NOT_FOUND = 103
    private const val LIST_TIMEOUT_MS = 60_000
    private const val INSTALL_TIMEOUT_MS = 300_000

    private val LOG = logger<AumlTemplates>()

    /** Offers to install the templates when `dotnet new` has none, unless the offer was turned down for good. Blocks. */
    fun offerIfMissing() {
        if (PropertiesComponent.getInstance().getBoolean(DONT_ASK_KEY, false)) return
        if (dotnet(LIST_TIMEOUT_MS, "new", "list", APP_TEMPLATE)?.exitCode != TEMPLATE_NOT_FOUND) return

        notification(
            "Install them to create Adamantium UI applications and libraries from File | New Solution. " +
                "Later: Tools | Install Adamantium UI Templates.",
            NotificationType.INFORMATION,
            OFFER_GROUP)
            .addAction(NotificationAction.createSimpleExpiring("Install") { install() })
            .addAction(NotificationAction.createSimpleExpiring("Don't ask again") {
                PropertiesComponent.getInstance().setValue(DONT_ASK_KEY, true, false)
            })
            .notify(null)
    }

    /** Installs the latest templates from nuget.org, or reinstalls them, in the background. */
    fun install() {
        ProgressManager.getInstance().run(object : Task.Backgroundable(null, "Installing Adamantium UI templates", false) {
            override fun run(indicator: ProgressIndicator) {
                val output = dotnet(INSTALL_TIMEOUT_MS, "new", "install", TEMPLATES_PACKAGE, "--force")
                if (output != null && output.exitCode == 0 && !output.isTimeout) {
                    notification(
                        "Installed: File | New Solution lists Adamantium UI Application, View Library and Control Library.",
                        NotificationType.INFORMATION).notify(null)
                    return
                }

                val reason = output?.stderrLines?.lastOrNull { it.isNotBlank() }
                    ?: output?.stdoutLines?.lastOrNull { it.isNotBlank() }
                    ?: "dotnet could not be started"
                notification(
                    "Could not install them: $reason. Run dotnet new install $TEMPLATES_PACKAGE in a terminal.",
                    NotificationType.WARNING).notify(null)
            }
        })
    }

    private fun notification(content: String, type: NotificationType, group: String = RESULT_GROUP) =
        NotificationGroupManager.getInstance().getNotificationGroup(group)
            .createNotification("Adamantium UI templates", content, type)

    private fun dotnet(timeoutMs: Int, vararg args: String): ProcessOutput? = try {
        CapturingProcessHandler(GeneralCommandLine("dotnet", *args).withCharset(StandardCharsets.UTF_8))
            .runProcess(timeoutMs)
    } catch (e: ExecutionException) {
        LOG.info("dotnet ${args.joinToString(" ")} could not start", e)
        null
    }
}
