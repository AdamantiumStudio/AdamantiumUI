package com.adamantium.auml

import com.intellij.ide.AppLifecycleListener
import com.intellij.openapi.application.ApplicationManager

/** Offers, once per IDE start, to install the Adamantium UI project templates when they are missing. */
internal class AumlTemplatesOffer : AppLifecycleListener {
    override fun appFrameCreated(commandLineArgs: MutableList<String>) {
        ApplicationManager.getApplication().executeOnPooledThread { AumlTemplates.offerIfMissing() }
    }
}
