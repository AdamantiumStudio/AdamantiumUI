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
 * Runs the designer host from a copy of its build folder. A running host keeps its dlls loaded, so a build that writes
 * that folder failed on every one of them; the copy leaves the build folder to the build.
 */
internal object DesignerHostShadowCopy {
    private const val SETTLE_MS = 1500L
    private const val MAX_WAIT_MS = 120_000L
    private const val DAY_MS = 24L * 60 * 60 * 1000
    private const val LOGS = "logs"
    private const val STAGING = ".staging"

    private val root: Path = PathManager.getSystemDir().resolve("adamantium-designer-host")

    /** A fingerprint of the build folder of [exe]; any file a build writes changes it. */
    fun stamp(exe: Path): String {
        val source = exe.parent
        val digest = MessageDigest.getInstance("SHA-256")
        digest.update(source.toAbsolutePath().normalize().toString().toByteArray())
        try {
            Files.walk(source).use { paths ->
                paths.filter { it.isRegularFile() && !it.startsWith(source.resolve(LOGS)) }
                    .sorted()
                    .forEach { file ->
                        digest.update(source.relativize(file).toString().toByteArray())
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
    fun isSettled(exe: Path, stamp: String): Boolean {
        Thread.sleep(SETTLE_MS)
        return stamp(exe) == stamp
    }

    /** The stamp of a build that is no longer being written, waiting for a running build to finish. */
    fun settledStamp(exe: Path): String {
        var stamp = stamp(exe)
        if (root.resolve(stamp).exists()) {
            return stamp
        }

        val deadline = System.currentTimeMillis() + MAX_WAIT_MS
        while (!isSettled(exe, stamp) && System.currentTimeMillis() < deadline) {
            stamp = stamp(exe)
        }
        return stamp
    }

    /**
     * The host executable inside the copy of the build [stamp] names, copied on first use. Other copies go once no host
     * runs from them: at once on Windows, which will not delete a running exe, after a day elsewhere.
     */
    fun copyOf(exe: Path, stamp: String): Path {
        val target = root.resolve(stamp)
        if (!target.exists()) {
            val staging = root.resolve("$stamp.${ProcessHandle.current().pid()}.${System.nanoTime()}$STAGING")
            copyTree(exe.parent, staging)
            try {
                Files.move(staging, target, StandardCopyOption.ATOMIC_MOVE)
            } catch (_: IOException) {
                staging.toFile().deleteRecursively()
            }
        }

        purgeUnused(target, exe.fileName)
        return target.resolve(exe.fileName)
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

    private fun purgeUnused(keep: Path, exeName: Path) {
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
                dir.resolve(exeName).deleteIfExists()
                dir.toFile().deleteRecursively()
            } catch (_: IOException) {
            }
        }
    }
}
