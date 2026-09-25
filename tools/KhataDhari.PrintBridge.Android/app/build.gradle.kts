plugins {
    id("com.android.application")
}

android {
    namespace = "com.khatadhari.printbridge"
    compileSdk = 36

    defaultConfig {
        applicationId = "com.khatadhari.printbridge"
        minSdk = 23
        targetSdk = 36
        versionCode = 1
        versionName = "0.1.0-poc"
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
}

dependencies {
    testImplementation("junit:junit:4.13.2")
}
