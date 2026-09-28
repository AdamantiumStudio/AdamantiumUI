package com.adamantium.auml

import com.intellij.javaee.ExternalResourceManagerEx
import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.fileEditor.FileDocumentManager
import com.intellij.openapi.fileEditor.FileEditorManager
import com.intellij.openapi.fileEditor.FileEditorManagerListener
import com.intellij.openapi.vfs.VirtualFile

/**
 * Registers a .auml file's `clr-namespace:` xmlns URIs as ignored XML resources per file (like [AumlResourceProvider]
 * does for static namespaces); the language server remains the real validator.
 */
class AumlClrNamespaceRegistrar : FileEditorManagerListener {
    override fun fileOpened(source: FileEditorManager, file: VirtualFile) {
        if (!file.name.endsWith(".auml", ignoreCase = true)) return

        // Run after the open completes; reading the document + touching settings is safest off the
        // critical path, and re-highlighting then picks up the newly-ignored URIs.
        ApplicationManager.getApplication().invokeLater {
            val text = FileDocumentManager.getInstance().getDocument(file)?.text ?: return@invokeLater
            val manager = ExternalResourceManagerEx.getInstanceEx()
            val toIgnore = CLR_NAMESPACE.findAll(text)
                .map { it.value }
                .filterNot { manager.isIgnoredResource(it) }
                .distinct()
                .toList()
            if (toIgnore.isEmpty()) return@invokeLater

            // addIgnoredResources(List<String>, Disposable); scoped to the project so it clears on close.
            ApplicationManager.getApplication().runWriteAction(Runnable {
                try {
                    manager.addIgnoredResources(toIgnore, source.project)
                } catch (_: Throwable) {
                }
            })
        }
    }

    private companion object {
        val CLR_NAMESPACE = Regex("""clr-namespace:[^"']+""")
    }
}
