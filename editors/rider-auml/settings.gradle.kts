// The JDK the build compiles with (jvmToolchain in build.gradle.kts) is downloaded when the machine has none of that
// version, so ./gradlew buildPlugin needs only a JDK to start Gradle itself.
plugins {
    id("org.gradle.toolchains.foojay-resolver-convention") version "1.0.0"
}

rootProject.name = "adamantium-auml"
