import org.gradle.api.tasks.Exec
import org.gradle.api.tasks.JavaExec
import org.gradle.api.tasks.bundling.Zip

plugins {
    id("java")
    id("org.jetbrains.kotlin.jvm") version "2.4.20"
    id("org.jetbrains.intellij.platform") version "2.19.0"
}

group = "com.adamantium"
version = "1.3.3"

repositories {
    mavenCentral()
    intellijPlatform {
        defaultRepositories()
    }
}

dependencies {
    intellijPlatform {
        // Built against Rider itself, the IDE it is used in: compiled against the platform it runs on, not an older
        // IntelliJ it merely stays compatible with. Set platformVersion in gradle.properties.
        rider(providers.gradleProperty("platformVersion"))

        // The LSP4IJ plugin provides the LanguageServerFactory / connection-provider API.
        plugin(providers.gradleProperty("lsp4ijPlugin"))
    }
}

intellijPlatform {
    pluginConfiguration {
        ideaVersion {
            sinceBuild = providers.gradleProperty("sinceBuild")
            untilBuild = provider { null }   // don't cap the upper IDE version
        }
    }

    // `publishPlugin` uploads to JetBrains Marketplace with a token from the Marketplace profile (My Tokens). The first
    // version of a plugin cannot be published this way: it is uploaded by hand on the site.
    publishing {
        token = providers.environmentVariable("PUBLISH_TOKEN")
    }
}

kotlin {
    jvmToolchain(21)
}

// --- Bundle the AUML language server inside the plugin -----------------------------------------
// Framework-dependent publish (needs .NET 10 on the user's machine; keeps the server's type
// resolution working — a self-contained single-file build hides the runtime assemblies it needs).

val serverCsproj = file("../../Adamantium.UI.LanguageServer/Adamantium.UI.LanguageServer.csproj")
val serverConfiguration = providers.gradleProperty("serverConfiguration").getOrElse("Release")
val serverPublishDir = layout.buildDirectory.dir("server-publish")
val serverPackDir = layout.buildDirectory.dir("server-pack")

val publishServer by tasks.registering(Exec::class) {
    group = "build"
    description = "Publishes the AUML language server (framework-dependent) for bundling."
    // The server's own folder and the two it compiles in, Markup and the generator's type model: a change in either
    // must republish it.
    for (project in listOf(serverCsproj.parentFile, file("../../Adamantium.UI.Markup"), file("../../Adamantium.UI.Generators"))) {
        inputs.files(fileTree(project) {
            include("**/*.cs", "**/*.csproj")
            exclude("**/bin/**", "**/obj/**")
        })
    }
    outputs.dir(serverPublishDir)
    // --disable-build-servers runs MSBuild + the C# compiler in-process instead of reusing the persistent
    // build-server nodes. Those nodes can wedge (especially while Rider is building the same solution),
    // which made this publish hang indefinitely; in-process keeps it self-contained and fast.
    commandLine(
        "dotnet", "publish", serverCsproj.absolutePath,
        "--disable-build-servers",
        "-c", serverConfiguration,
        "-o", serverPublishDir.get().asFile.absolutePath
    )
}

val packServer by tasks.registering(Zip::class) {
    dependsOn(publishServer)
    from(serverPublishDir)
    archiveFileName.set("server.zip")
    destinationDirectory.set(serverPackDir)
}

tasks.processResources {
    dependsOn(packServer)
    from(serverPackDir) {
        include("server.zip")
        into("server")
    }
}

// --- Live preview host ------------------------------------------------------------------------
// Point the AUML live preview at the locally built designer host so `runIde` works without manual
// setup. A project on the Adamantium.UI package needs nothing: its build tells the plugin where the
// packaged host is. ADAMANTIUM_DESIGNER_HOST is for projects on the UI's own sources, in a real Rider
// as well as in the runIde sandbox.
val designerHostExe = listOf("Debug", "Release")
    .map { file("../../artifacts/designer-host/$it/net10.0/Adamantium.UI.Designer.Host.exe") }
    .firstOrNull { it.exists() }

tasks.named<JavaExec>("runIde") {
    designerHostExe?.let { environment("ADAMANTIUM_DESIGNER_HOST", it.absolutePath) }
}
