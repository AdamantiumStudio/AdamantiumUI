package com.adamantium.auml

import com.intellij.openapi.actionSystem.IdeActions
import com.intellij.testFramework.fixtures.BasePlatformTestCase

/** Markup tags edited in pairs, as in the Pro text editor: closed at once, made self-closing, renamed together. */
class AumlTagEditingTest : BasePlatformTestCase() {
    fun testTheEndOfAnOpeningTag_PutsItsClosingTagIn() {
        myFixture.configureByText("A.auml", "<Grid>\n    <StackPanel Margin=\"8\"<caret>\n</Grid>")
        myFixture.type(">")
        myFixture.checkResult("<Grid>\n    <StackPanel Margin=\"8\"><caret></StackPanel>\n</Grid>")
    }

    fun testEditingATagsName_EditsItsPartnerToo() {
        myFixture.configureByText("A.auml", "<Grid>\n    <Stack<caret>>\n        <Grid/>\n    </Stack>\n</Grid>")
        myFixture.type("Panel")
        myFixture.checkResult("<Grid>\n    <StackPanel<caret>>\n        <Grid/>\n    </StackPanel>\n</Grid>")
    }

    fun testASlashBeforeTheEndOfAnEmptyElement_TakesItsClosingTagAway() {
        myFixture.configureByText("A.auml", "<Grid>\n    <Border <caret>>\n    </Border>\n</Grid>")
        myFixture.type("/")
        myFixture.checkResult("<Grid>\n    <Border /<caret>>\n</Grid>")
    }

    fun testASlashBeforeTheEndOfAnElementWithContent_IsOnlyTyped() {
        myFixture.configureByText("A.auml", "<Border <caret>><Grid/></Border>")
        myFixture.type("/")
        myFixture.checkResult("<Border /<caret>><Grid/></Border>")
    }

    fun testASlashInAValueOrAComment_IsOnlyTyped() {
        myFixture.configureByText("A.auml", "<Grid Text=\"a<caret>>\"></Grid>")
        myFixture.type("/")
        myFixture.checkResult("<Grid Text=\"a/<caret>>\"></Grid>")

        myFixture.configureByText("B.auml", "<!-- <Grid <caret>></Grid> -->")
        myFixture.type("/")
        myFixture.checkResult("<!-- <Grid /<caret>></Grid> -->")
    }

    fun testOneUndo_PutsTheClosingTagBack() {
        myFixture.configureByText("A.auml", "<Grid <caret>></Grid>")
        myFixture.type("/")
        myFixture.performEditorAction(IdeActions.ACTION_UNDO)
        myFixture.checkResult("<Grid <caret>></Grid>")
    }

    fun testALanguageFile_TakesTheClosingTagAwayToo() {
        myFixture.configureByText("Strings.en.alang", "<Strings>\n    <Text Key=\"Save\" <caret>></Text>\n</Strings>")
        myFixture.type("/")
        myFixture.checkResult("<Strings>\n    <Text Key=\"Save\" /<caret>>\n</Strings>")
    }
}
