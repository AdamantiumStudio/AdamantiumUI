package com.adamantium.auml

import com.intellij.openapi.application.PathManager
import com.intellij.openapi.util.SystemInfo
import java.io.IOException
import java.io.UncheckedIOException
import java.nio.ByteBuffer
import java.nio.file.Files
import java.nio.file.Path
import java.nio.file.StandardCopyOption
import java.security.MessageDigest
import kotlin.io.path.createDirectories
import kotlin.io.path.deleteIfExists
import kotlin.io.path.exists
import kotlin.io.path.fileSize
import kotlin.io.path.getLastModifiedTime
import kotlin.io.path.isDirectory
import kotlin.io.path.isRegularFile
import kotlin.io.path.listDirectoryEntries
import kotlin.io.path.name

/**
 * Runs the designer host from a copy of the folder it loads its assemblies from - its own build folder, or the
 * previewed project's build output. A running host keeps those dlls loaded, so a build that writes the folder failed on
 * every one of them; the copy leaves the folder to the build.
 */
internal object DesignerHostShadowCopy {
    private const val SETTLE_MS = 1500L
    private const val MAX_WAIT_MS = 120_000L
    private const val DAY_MS = 24L * 60 * 60 * 1000
    private const val LOGS = "logs"
    private const val STAGING = ".staging"

    // Every host loads it from its copy, so on Windows a copy whose file cannot be deleted is one a host runs from.
    private const val HELD_BY_A_HOST = "Adamantium.UI.dll"

    private val root: Path = PathManager.getSystemDir().resolve("adamantium-designer-host")

    /** A fingerprint of [folder]; any file a build writes changes it. */
    fun stamp(folder: Path): String {
        val digest = MessageDigest.getInstance("SHA-256")
        digest.update(folder.toAbsolutePath().normalize().toString().toByteArray())
        try {
            Files.walk(folder).use { paths ->
                paths.filter { it.isRegularFile() && !it.startsWith(folder.resolve(LOGS)) }
                    .sorted()
                    .forEach { file ->
                        digest.update(folder.relativize(file).toString().toByteArray())
                        digest.update(ByteBuffer.allocate(16)
                            .putLong(file.fileSize())
                            .putLong(file.getLastModifiedTime().toMillis())
                            .array())
                    }
            }
        } catch (_: IOException) {
            return "changing-${System.nanoTime()}"
        } catch (_: UncheckedIOException) {
            return "changing-${System.nanoTime()}"
        }
        return digest.digest().take(8).joinToString("") { "%02x".format(it) }
    }

    /** True when [stamp] still holds a moment later, i.e. no build is writing the folder. */
    fun isSettled(folder: Path, stamp: String): Boolean {
        Thread.sleep(SETTLE_MS)
        return stamp(folder) == stamp
    }

    /** The stamp of a build that is no longer being written, waiting for a running build to finish. */
    fun settledStamp(folder: Path): String {
        var stamp = stamp(folder)
        if (root.resolve(stamp).exists()) {
            return stamp
        }

        val deadline = System.currentTimeMillis() + MAX_WAIT_MS
        while (!isSettled(folder, stamp) && System.currentTimeMillis() < deadline) {
            stamp = stamp(folder)
        }
        return stamp
    }

    /**
     * The copy of [folder] as of [stamp], made on first use. Other copies go once no host runs from them: at once on
     * Windows, which will not delete a loaded dll, after a day elsewhere.
     */
    fun copyOf(folder: Path, stamp: String): Path {
        val target = root.resolve(stamp)
        if (!target.exists()) {
            val staging = root.resolve("$stamp.${ProcessHandle.current().pid()}.${System.nanoTime()}$STAGING")
            copyTree(folder, staging)
            try {
                Files.move(staging, target, StandardCopyOption.ATOMIC_MOVE)
            } catch (_: IOException) {
                staging.toFile().deleteRecursively()
            }
        }

        purgeUnused(target)
        return target
    }

    private fun copyTree(source: Path, target: Path) {
        Files.walk(source).use { paths ->
            paths.filter { !it.startsWith(source.resolve(LOGS)) }.forEach { path ->
                val destination = target.resolve(source.relativize(path).toString())
                if (path.isDirectory()) {
                    destination.createDirectories()
                } else {
                    Files.copy(path, destination, StandardCopyOption.COPY_ATTRIBUTES, StandardCopyOption.REPLACE_EXISTING)
                }
            }
        }
    }

    private fun purgeUnused(keep: Path) {
        val dayAgo = System.currentTimeMillis() - DAY_MS
        for (dir in root.listDirectoryEntries()) {
            if (dir == keep || !dir.isDirectory()) {
                continue
            }

            val waitsADay = dir.name.endsWith(STAGING) || !SystemInfo.isWindows
            if (waitsADay && dir.getLastModifiedTime().toMillis() > dayAgo) {
                continue
            }

            try {
                dir.resolve(HELD_BY_A_HOST).deleteIfExists()
                dir.toFile().deleteRecursively()
            } catch (_: IOException) {
            }
        }
    }
}
