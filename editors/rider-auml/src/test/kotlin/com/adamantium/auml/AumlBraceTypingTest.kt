package com.adamantium.auml

import com.intellij.openapi.actionSystem.IdeActions
import com.intellij.testFramework.fixtures.BasePlatformTestCase

/** Braces in an attribute value come in pairs, as in the Pro text editor. */
class AumlBraceTypingTest : BasePlatformTestCase() {
    fun testABraceInAValue_ClosesAtOnce() {
        myFixture.configureByText("A.auml", "<Grid Tag=\"<caret>\"/>")
        myFixture.type("{")
        myFixture.checkResult("<Grid Tag=\"{<caret>}\"/>")
    }

    fun testTheCloser_IsTypedOver() {
        myFixture.configureByText("A.auml", "<Grid Tag=\"<caret>\"/>")
        myFixture.type("{Binding}")
        myFixture.checkResult("<Grid Tag=\"{Binding}<caret>\"/>")
    }

    fun testANestedBrace_ClosesToo_AndBothClosersAreTypedOver() {
        myFixture.configureByText("A.auml", "<Grid Tag=\"<caret>\"/>")
        myFixture.type("{ResourceLink Source={x:Type Grid}}")
        myFixture.checkResult("<Grid Tag=\"{ResourceLink Source={x:Type Grid}}<caret>\"/>")
    }

    fun testABraceBeforeAWord_IsOnlyTyped() {
        myFixture.configureByText("A.auml", "<Grid Tag=\"<caret>Binding\"/>")
        myFixture.type("{")
        myFixture.checkResult("<Grid Tag=\"{<caret>Binding\"/>")
    }

    fun testACloserTheValueNeeds_IsTyped() {
        myFixture.configureByText("A.auml", "<Grid Tag=\"{Binding <caret>}\"/>")
        myFixture.type("{")
        myFixture.type("}")
        myFixture.checkResult("<Grid Tag=\"{Binding {}<caret>}\"/>")
    }

    fun testABraceOutsideAValue_OrInAComment_IsOnlyTyped() {
        myFixture.configureByText("A.auml", "<Grid><caret></Grid>")
        myFixture.type("{")
        myFixture.checkResult("<Grid>{<caret></Grid>")

        myFixture.configureByText("B.auml", "<!-- <caret> --><Grid/>")
        myFixture.type("{")
        myFixture.checkResult("<!-- {<caret> --><Grid/>")
    }

    fun testOneUndo_TakesThePairAway() {
        myFixture.configureByText("A.auml", "<Grid Tag=\"<caret>\"/>")
        myFixture.type("{")
        myFixture.performEditorAction(IdeActions.ACTION_UNDO)
        myFixture.checkResult("<Grid Tag=\"<caret>\"/>")
    }
}
