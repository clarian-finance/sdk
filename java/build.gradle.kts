plugins {
    java
    `maven-publish`
}

group = "finance.clarian"
version = "0.6.0"

java {
    toolchain {
        languageVersion.set(JavaLanguageVersion.of(17))
    }
    withSourcesJar()
    withJavadocJar()
}

repositories {
    mavenCentral()
}

dependencies {
    testImplementation("org.junit.jupiter:junit-jupiter:5.11.4")
    testRuntimeOnly("org.junit.platform:junit-platform-launcher")
}

tasks.test {
    useJUnitPlatform()
}

publishing {
    publications {
        create<MavenPublication>("maven") {
            from(components["java"])
            groupId = "finance.clarian"
            artifactId = "clarian-sdk"
            version = project.version.toString()
        }
    }
}

// CLARIAN_API_KEY=cl_test_sk_... CLARIAN_WORKSPACE_ID=... CLARIAN_BASE_URL=http://localhost:3000 gradle e2e
tasks.register<JavaExec>("e2e") {
    classpath = sourceSets["test"].runtimeClasspath
    mainClass.set("finance.clarian.sdk.SandboxE2E")
    args(rootProject.file("../openapi.json").absolutePath)
}
