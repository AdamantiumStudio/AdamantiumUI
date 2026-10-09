package com.adamantium.auml

import com.intellij.testFramework.fixtures.BasePlatformTestCase

class AumlNamespaceHighlightFilterTest : BasePlatformTestCase() {
    fun testALibrarysXmlns_IsNotAnUnregisteredUri() {
        myFixture.configureByText("A.auml",
            "<Grid xmlns=\"https://adamantium/ui\" xmlns:pro=\"http://adamantium/ui/pro\"\n" +
                "      xmlns:local=\"clr-namespace:App.Views;assembly=App\"/>")

        val problems = myFixture.doHighlighting().filter { it.description?.contains("URI is not registered") == true }

        assertEmpty(problems.map { it.description })
    }
}
