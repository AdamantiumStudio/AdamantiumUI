package com.adamantium.auml

import java.nio.file.Path
import kotlin.io.path.exists
import kotlin.io.path.isDirectory
import kotlin.io.path.listDirectoryEntries
import kotlin.io.path.name
import kotlin.io.path.readText

/**
 * How the designer host is started for a file: the folder it runs from, which is copied first
 * ([DesignerHostShadowCopy]) and whose rebuild restarts it, and the command line inside that copy.
 *
 * A project on the Adamantium.UI package describes its build in obj/adamantium.designer.json: the host the package
 * carries runs on the project's own build output - the engine and the UI the application itself runs on. A library's
 * output holds only itself: its packages are probed in the package folder, and it runs on the host's own runtime
 * configuration. A project without that file (the UI's own sources, referenced as projects) uses the host
 * ADAMANTIUM_DESIGNER_HOST names, from that host's own build folder.
 */
internal class DesignerHostLaunch private constructor(
    val folder: Path,
    private val commandIn: (Path) -> List<String>,
) {
    fun command(copy: Path): List<String> = commandIn(copy)

    companion object {
        private const val HOST_ENV = "ADAMANTIUM_DESIGNER_HOST"
        private const val MANIFEST = "adamantium.designer.json"

        /** The launch for the project of [sourcePath]; throws, saying what to do, when there is none. */
        fun forFile(sourcePath: String?): DesignerHostLaunch {
            val projectDir = sourcePath?.let { projectDirOf(Path.of(it)) }
            val manifest = projectDir?.resolve("obj")?.resolve(MANIFEST)
            if (manifest != null && manifest.exists()) {
                return fromManifest(projectDir, manifest)
            }

            val override = System.getenv(HOST_ENV)?.takeIf { it.isNotBlank() }
            if (override != null) {
                return fromSources(Path.of(override))
            }

            if (projectDir == null) {
                error("This file is in no project: the designer previews the .auml files of a project that references Adamantium.UI.")
            }
            error("Build ${projectDir.name} first: the designer runs on its build output, which the build describes in " +
                "obj/$MANIFEST. Working on the UI's own sources? Set $HOST_ENV to the designer host you built.")
        }

        private fun fromSources(exe: Path): DesignerHostLaunch {
            if (!exe.exists()) {
                error("$HOST_ENV points to a missing file: $exe")
            }

            return DesignerHostLaunch(exe.parent.toAbsolutePath().normalize()) { copy ->
                listOf(copy.resolve(exe.fileName).toString(), "serve")
            }
        }

        private fun fromManifest(projectDir: Path, manifest: Path): DesignerHostLaunch {
            val json = MiniJson.parse(manifest.readText()) as? Map<*, *> ?: error("$manifest is not readable")
            fun optional(key: String): Path? = (json[key] as? String)?.takeIf { it.isNotBlank() }?.let { Path.of(it) }
            fun path(key: String): Path = optional(key) ?: error("$manifest names no $key")

            val host = path("host")
            val depsFile = path("depsFile")
            if (!host.exists()) {
                error("The Adamantium.UI package has no designer host: $host")
            }

            val appConfig = optional("runtimeConfig")?.takeIf { it.exists() }
            val hostConfig = host.resolveSibling("${host.fileName.toString().removeSuffix(".dll")}.runtimeconfig.json")
            if (appConfig == null && !hostConfig.exists()) {
                error("${projectDir.name} is a library, and this Adamantium.UI package cannot preview one: update the package.")
            }
            val packages = if (appConfig == null) optional("packageFolder") else null

            return DesignerHostLaunch(depsFile.parent.toAbsolutePath().normalize()) { copy ->
                val config = appConfig?.let { copy.resolve(it.fileName) } ?: hostConfig
                buildList {
                    addAll(listOf("dotnet", "exec", "--depsfile", copy.resolve(depsFile.fileName).toString()))
                    addAll(listOf("--runtimeconfig", config.toString()))
                    if (packages != null) {
                        addAll(listOf("--additionalprobingpath", packages.toString()))
                    }
                    addAll(listOf(host.toString(), "serve"))
                }
            }
        }

        private fun projectDirOf(file: Path): Path? {
            var dir = file.toAbsolutePath().parent
            while (dir != null) {
                if (dir.isDirectory() && dir.listDirectoryEntries("*.csproj").isNotEmpty()) {
                    return dir
                }
                dir = dir.parent
            }
            return null
        }
    }
}
